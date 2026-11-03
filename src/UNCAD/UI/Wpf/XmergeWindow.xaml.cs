using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using UNCAD.Core.Dwg;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// Xmerge 拖放式 DWG 选择窗（3.0 WPF 版，D-038，P2 工作台模式）。
    /// 拖入文件/文件夹（递归扫描）、添加/移除文件；"开始合并"的权威校验在
    /// 按钮里：列表为空不关窗，给出可见警告（设计原则 6）。
    /// </summary>
    public sealed partial class XmergeWindow : FluentWindow
    {
        /// <summary>用户点了"开始合并"为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        private readonly ObservableCollection<string> _files =
            new ObservableCollection<string>();
        private readonly List<string> _pendingInputs = new List<string>();
        private bool _scanning;

        public XmergeWindow(IEnumerable<string> initial = null)
        {
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            FileList.ItemsSource = _files;
            // 契约测试断言拖放支持：文件/文件夹可拖入整个窗口。
            AllowDrop = true;
            DragOver += OnDragOver;
            Drop += OnDrop;
            UpdateHint();
            if (initial != null)
                Loaded += async (sender, args) => await AddPathsAsync(initial);
        }

        /// <summary>已添加、待合并的 DWG 文件（去重 + 忽略大小写排序）。</summary>
        public IReadOnlyList<string> Files => _files.ToList();

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                await AddPathsAsync(paths);
        }

        private void OnAddFiles(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "AutoCAD 图纸 (*.dwg)|*.dwg",
                Multiselect = true,
                CheckFileExists = true,
                Title = "添加 DWG 文件"
            };
            if (dialog.ShowDialog(this) == true)
                _ = AddPathsAsync(dialog.FileNames);
        }

        private void OnAddFolder(object sender, RoutedEventArgs e)
        {
            // net48 WPF 没有原生文件夹选择框；沿用 WinForms 的 FolderBrowserDialog。
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择包含 DWG 的文件夹"
            })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    _ = AddPathsAsync(new[] { dialog.SelectedPath });
            }
        }

        private void OnRemoveSelected(object sender, RoutedEventArgs e)
        {
            List<string> selected = FileList.SelectedItems.Cast<string>().ToList();
            foreach (string file in selected) _files.Remove(file);
            UpdateHint();
        }

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            // 权威校验：空列表绝不带着"确认"语义关窗。
            if (_files.Count == 0)
            {
                WarningBar.Message = "请先添加至少一个 DWG 文件。";
                WarningBar.IsOpen = true;
                return;
            }
            WarningBar.IsOpen = false;
            Confirmed = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        /// <summary>
        /// 扫描是异步的（Task.Run 里做 IO），拖入大文件夹时窗口保持响应；
        /// 扫描期间的再次拖入排队等待，不丢文件。
        /// </summary>
        private async Task AddPathsAsync(IEnumerable<string> paths)
        {
            string[] input = (paths ?? Enumerable.Empty<string>()).ToArray();
            if (input.Length == 0) return;
            if (_scanning)
            {
                _pendingInputs.AddRange(input);
                HintText.Text = "已排队，等待完成当前扫描...";
                return;
            }

            _scanning = true;
            SetBusy(true);
            try
            {
                var pending = new Queue<string>(input);
                while (pending.Count > 0)
                {
                    string[] batch = pending.ToArray();
                    pending.Clear();
                    HintText.Text = "正在扫描 DWG...";
                    DwgPathDiscoveryResult discovered = null;
                    try
                    {
                        discovered = await Task.Run(() => DwgPathDiscovery.Find(batch));
                    }
                    catch (Exception ex)
                    {
                        ShowWarning("扫描 DWG 失败：" + ex.Message);
                    }
                    if (discovered != null) ApplyDiscovery(discovered);

                    if (_pendingInputs.Count == 0) continue;
                    foreach (string queued in _pendingInputs) pending.Enqueue(queued);
                    _pendingInputs.Clear();
                }
            }
            finally
            {
                _scanning = false;
                SetBusy(false);
                UpdateHint();
            }
        }

        /// <summary>同步扫描入口（无头测试用）：跳过 Task.Run，结果与异步路径一致。</summary>
        internal void AddPaths(IEnumerable<string> paths)
            => ApplyDiscovery(DwgPathDiscovery.Find(paths));

        private void ApplyDiscovery(DwgPathDiscoveryResult discovered)
        {
            if (discovered == null) return;
            var merged = new HashSet<string>(_files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in discovered.Files) merged.Add(file);
            List<string> sorted = merged
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            _files.Clear();
            foreach (string file in sorted) _files.Add(file);
            UpdateHint();
            if (discovered.Errors.Count > 0)
                ShowWarning("部分目录无法读取：\n" + string.Join("\n",
                    discovered.Errors.Take(8)));
        }

        private void SetBusy(bool busy)
        {
            MergeButton.IsEnabled = !busy;
            FileList.IsEnabled = !busy;
        }

        private void UpdateHint()
            => HintText.Text = _files.Count == 0
                ? "将文件或文件夹拖到此处"
                : "已添加 " + _files.Count + " 个 DWG 文件";

        private void ShowWarning(string message)
        {
            WarningBar.Message = message;
            WarningBar.IsOpen = true;
        }
    }
}
