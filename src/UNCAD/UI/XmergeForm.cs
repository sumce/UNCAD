using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using UNCAD.Core.Dwg;

namespace UNCAD.UI
{
    /// <summary>Drag-and-drop file picker used by Xmerge.</summary>
    public sealed class XmergeForm : DpiAwareForm
    {
        private readonly ListBox _files;
        private readonly Label _hint;
        private readonly Button _merge;
        private readonly List<string> _pendingInputs = new List<string>();
        private bool _scanning;

        public XmergeForm(IEnumerable<string> initial = null)
        {
            DialogLayout.Apply(this, "Xmerge · 合并 DWG", new Size(760, 520),
                new Size(600, 380));
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            Panel header = UiTheme.Header("批量合并 DWG", "可拖入 DWG 文件或文件夹；文件夹会递归扫描子目录。\n"
                + "确认后将按 XLAYOUT 的横向排版规则合并到当前图纸。");
            _files = new ListBox
            {
                Name = "XmergeFiles",
                Dock = DockStyle.Fill,
                HorizontalScrollbar = true,
                IntegralHeight = false,
                AllowDrop = true,
                BackColor = UiTheme.Surface,
                BorderStyle = BorderStyle.FixedSingle
            };
            _files.DragEnter += OnDragEnter;
            _files.DragDrop += OnDragDrop;
            _hint = new Label
            {
                Text = "将文件或文件夹拖到此处",
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(UiTheme.SpaceS, 0, 0, 0)
            };
            var add = UiTheme.Button("添加文件");
            add.Click += (sender, args) => AddFilesWithDialog();
            var folder = UiTheme.Button("添加文件夹");
            folder.Click += (sender, args) => AddFolderWithDialog();
            var remove = UiTheme.Button("移除选中");
            remove.Click += (sender, args) => RemoveSelected();
            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            _merge = UiTheme.PrimaryButton("开始合并", DialogResult.OK);
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Controls.Add(cancel);
            commands.Controls.Add(_merge);
            commands.Controls.Add(remove);
            commands.Controls.Add(folder);
            commands.Controls.Add(add);
            Controls.Add(_files);
            Controls.Add(_hint);
            Controls.Add(commands);
            Controls.Add(header);
            AcceptButton = _merge;
            CancelButton = cancel;
            _merge.Click += (sender, args) =>
            {
                if (Files.Count == 0)
                {
                    MessageBox.Show(this, "请先添加至少一个 DWG 文件。", "Xmerge",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                }
            };
            if (initial != null) Shown += async (sender, args) =>
                await AddPathsAsync(initial);
        }

        public IReadOnlyList<string> Files => _files.Items.Cast<string>().ToList();

        private void OnDragEnter(object sender, DragEventArgs args)
        {
            args.Effect = args.Data != null && args.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void OnDragDrop(object sender, DragEventArgs args)
        {
            if (args.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                await AddPathsAsync(paths);
        }

        private async void AddFilesWithDialog()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "AutoCAD 图纸 (*.dwg)|*.dwg",
                Multiselect = true,
                CheckFileExists = true,
                Title = "添加 DWG 文件"
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    await AddPathsAsync(dialog.FileNames);
            }
        }

        private async void AddFolderWithDialog()
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择包含 DWG 的文件夹" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    await AddPathsAsync(new[] { dialog.SelectedPath });
            }
        }

        private async Task AddPathsAsync(IEnumerable<string> paths)
        {
            string[] input = (paths ?? Enumerable.Empty<string>()).ToArray();
            if (input.Length == 0) return;
            if (_scanning)
            {
                _pendingInputs.AddRange(input);
                _hint.Text = "已排队，等待完成当前扫描...";
                return;
            }

            _scanning = true;
            _merge.Enabled = false;
            _files.Enabled = false;
            try
            {
                var pending = new Queue<string>(input);
                while (pending.Count > 0)
                {
                    string[] batch = pending.ToArray();
                    pending.Clear();
                    _hint.Text = "正在扫描 DWG...";
                    DwgPathDiscoveryResult discovered = null;
                    try
                    {
                        discovered = await Task.Run(() => DwgPathDiscovery.Find(batch));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "扫描 DWG 失败：" + ex.Message, "Xmerge",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    if (discovered != null)
                    {
                        AddDiscoveredFiles(discovered);
                    }

                    if (_pendingInputs.Count == 0) continue;
                    foreach (string queued in _pendingInputs) pending.Enqueue(queued);
                    _pendingInputs.Clear();
                }
            }
            finally
            {
                _scanning = false;
                _merge.Enabled = true;
                _files.Enabled = true;
                _hint.Text = _files.Items.Count == 0
                    ? "将文件或文件夹拖到此处" : "已添加 " + _files.Items.Count + " 个 DWG 文件";
            }

        }

        private void AddDiscoveredFiles(DwgPathDiscoveryResult discovered)
        {
            var files = new HashSet<string>(Files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in discovered.Files) files.Add(file);
            _files.Items.Clear();
            _files.Items.AddRange(files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
            _hint.Text = _files.Items.Count == 0
                ? "将文件或文件夹拖到此处" : "已添加 " + _files.Items.Count + " 个 DWG 文件";
            if (discovered.Errors.Count > 0)
                MessageBox.Show(this, "部分目录无法读取：\n" + string.Join("\n",
                    discovered.Errors.Take(8)), "Xmerge", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
        }

        private void RemoveSelected()
        {
            while (_files.SelectedItems.Count > 0)
                _files.Items.Remove(_files.SelectedItems[0]);
            _hint.Text = _files.Items.Count == 0
                ? "将文件或文件夹拖到此处" : "已添加 " + _files.Items.Count + " 个 DWG 文件";
        }
    }
}
