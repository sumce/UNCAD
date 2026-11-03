using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using UNCAD.Core.Excel;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>One unresolved material in a multi-frame U1U update, with category-limited candidates.</summary>
    internal sealed class BatchCatalogRequest
    {
        public BatchCatalogRequest(string key, string machineId, string deviceName,
            string category, string originalModel, IEnumerable<ListItem> candidates)
        {
            Key = (key ?? "").Trim();
            MachineId = (machineId ?? "").Trim();
            DeviceName = (deviceName ?? "").Trim();
            Category = (category ?? "").Trim();
            OriginalModel = (originalModel ?? "").Trim();
            Candidates = (candidates ?? Enumerable.Empty<ListItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code)
                    && !string.IsNullOrWhiteSpace(item.Name)
                    && string.Equals(item.Category?.Trim(), Category, StringComparison.Ordinal))
                .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public string Key { get; }
        public string MachineId { get; }
        public string DeviceName { get; }
        public string Category { get; }
        public string OriginalModel { get; }
        public IReadOnlyList<ListItem> Candidates { get; }
    }

    /// <summary>
    /// 批量 U1U 未匹配规格选择窗（3.0 WPF 版，D-038，P2 工作台模式）：
    /// 每个未匹配材料一行，替代规格下拉按类别限定；全部选择后才能确认。
    /// </summary>
    internal sealed partial class BatchCatalogSelectionWindow : FluentWindow
    {
        /// <summary>用户点了“确认全部选择”为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        /// <summary>确认后的 key → 所选固定清单项。</summary>
        public IReadOnlyDictionary<string, ListItem> Selections { get; private set; }

        private readonly IReadOnlyList<BatchCatalogRequest> _requests;
        private readonly ObservableCollection<BatchCatalogRow> _rows =
            new ObservableCollection<BatchCatalogRow>();

        public BatchCatalogSelectionWindow(IEnumerable<BatchCatalogRequest> requests)
        {
            _requests = (requests ?? Enumerable.Empty<BatchCatalogRequest>()).ToList();
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;

            NoticeText.Text = "未匹配项目 " + _requests.Count + " 项，替代规格待确认。";
            CatalogGrid.ItemsSource = _rows;
            foreach (BatchCatalogRequest request in _requests)
            {
                var row = new BatchCatalogRow(request);
                row.PropertyChanged += (sender, args) => UpdateConfirmation();
                _rows.Add(row);
            }
            UpdateConfirmation();
        }

        private void UpdateConfirmation()
            => ConfirmButton.IsEnabled = _requests.Count > 0
                && _rows.Count == _requests.Count
                && _rows.All(row => row.SelectedItem != null);

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            // 权威校验：任一行缺选择都不得带着 Confirmed 关窗。
            var result = new Dictionary<string, ListItem>(StringComparer.OrdinalIgnoreCase);
            foreach (BatchCatalogRow row in _rows)
            {
                ListItem selected = row.SelectedItem;
                if (selected == null) return;
                result[row.Request.Key] = selected;
            }
            Selections = result;
            Confirmed = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        /// <summary>行模型：替代规格下拉的选中编码即时写回。</summary>
        internal sealed class BatchCatalogRow : INotifyPropertyChanged
        {
            public BatchCatalogRow(BatchCatalogRequest request)
            {
                Request = request;
                Frame = request.MachineId + " / " + request.DeviceName;
                Category = request.Category;
                Original = request.OriginalModel.Length == 0
                    ? "未读取到" : request.OriginalModel;
                Candidates = request.Candidates;
            }

            public BatchCatalogRequest Request { get; }
            public string Frame { get; }
            public string Category { get; }
            public string Original { get; }
            public IReadOnlyList<ListItem> Candidates { get; }

            private string _selectedCode;
            public string SelectedCode
            {
                get { return _selectedCode; }
                set
                {
                    if (string.Equals(_selectedCode, value, StringComparison.Ordinal)) return;
                    _selectedCode = value;
                    OnPropertyChanged();
                }
            }

            public ListItem SelectedItem => string.IsNullOrWhiteSpace(SelectedCode)
                ? null
                : Candidates.FirstOrDefault(item => string.Equals(item.Code, SelectedCode,
                    StringComparison.OrdinalIgnoreCase));

            public event PropertyChangedEventHandler PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
