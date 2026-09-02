using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace UNCAD.UI
{
    /// <summary>Drag-and-drop file picker used by Xmerge.</summary>
    public sealed class XmergeForm : Form
    {
        private readonly ListBox _files;
        private readonly Label _hint;

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
            Button merge = UiTheme.PrimaryButton("开始合并", DialogResult.OK);
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Controls.Add(cancel);
            commands.Controls.Add(merge);
            commands.Controls.Add(remove);
            commands.Controls.Add(folder);
            commands.Controls.Add(add);
            Controls.Add(_files);
            Controls.Add(_hint);
            Controls.Add(commands);
            Controls.Add(header);
            AcceptButton = merge;
            CancelButton = cancel;
            merge.Click += (sender, args) =>
            {
                if (Files.Count == 0)
                {
                    MessageBox.Show(this, "请先添加至少一个 DWG 文件。", "Xmerge",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                }
            };
            AddPaths(initial);
        }

        public IReadOnlyList<string> Files => _files.Items.Cast<string>().ToList();

        private void OnDragEnter(object sender, DragEventArgs args)
        {
            args.Effect = args.Data != null && args.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs args)
        {
            if (args.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
        }

        private void AddFilesWithDialog()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "AutoCAD 图纸 (*.dwg)|*.dwg",
                Multiselect = true,
                CheckFileExists = true,
                Title = "添加 DWG 文件"
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(dialog.FileNames);
            }
        }

        private void AddFolderWithDialog()
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择包含 DWG 的文件夹" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(new[] { dialog.SelectedPath });
            }
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            var files = new HashSet<string>(Files, StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths ?? Enumerable.Empty<string>())
            {
                if (Directory.Exists(path))
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*.dwg",
                        SearchOption.AllDirectories)) files.Add(Path.GetFullPath(file));
                }
                else if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".dwg",
                    StringComparison.OrdinalIgnoreCase)) files.Add(Path.GetFullPath(path));
            }
            _files.Items.Clear();
            _files.Items.AddRange(files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
            _hint.Text = _files.Items.Count == 0
                ? "将文件或文件夹拖到此处" : "已添加 " + _files.Items.Count + " 个 DWG 文件";
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
