using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UNCAD.Core.Excel;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 固定清单电缆替代型号选择窗（3.0 WPF 版，D-038，P2 工作台模式）。
    /// 单关键词搜索（编码/规格/名称/特征任一命中），双击或“使用所选型号”确认。
    /// </summary>
    public sealed partial class CableCatalogSelectionWindow : FluentWindow
    {
        /// <summary>用户点了“使用所选型号”为 true；关闭/“保持原型号”/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        public ListItem SelectedItem => CableGrid.SelectedItem is CableRow row
            ? row.Item : null;

        private readonly List<ListItem> _candidates;
        private readonly ObservableCollection<CableRow> _rows =
            new ObservableCollection<CableRow>();

        public CableCatalogSelectionWindow(IEnumerable<ListItem> candidates,
            string missingModel)
        {
            _candidates = (candidates ?? Enumerable.Empty<ListItem>())
                .Where(item => item != null)
                .OrderBy(item => item.Code ?? "", StringComparer.Ordinal)
                .ToList();
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;

            Title = "选择替代电缆型号 · " + Branding.Nameplate;
            HeaderBar.Title = Title;
            CurrentModelText.Text = "当前型号: " + (string.IsNullOrWhiteSpace(missingModel)
                ? "未填写" : missingModel.Trim());
            CableGrid.ItemsSource = _rows;
            Populate("");
            Loaded += (sender, args) => SearchBox.Focus();
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
            => Populate(SearchBox.Text);

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateConfirmState();

        private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
            => Confirm();

        private void OnConfirm(object sender, RoutedEventArgs e) => Confirm();

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void Populate(string query)
        {
            string keyword = (query ?? "").Trim();
            _rows.Clear();
            foreach (ListItem candidate in _candidates.Where(item => Matches(item, keyword)))
                _rows.Add(new CableRow(candidate));
            if (_rows.Count > 0) CableGrid.SelectedIndex = 0;
            ResultCountText.Text = "共 " + _rows.Count + " 项";
            UpdateConfirmState();
        }

        internal static bool Matches(ListItem item, string keyword)
        {
            if (keyword.Length == 0) return true;
            return Contains(item.Code, keyword) || Contains(item.Spec, keyword)
                || Contains(item.Name, keyword) || Contains(item.Feature, keyword);
        }

        private static bool Contains(string value, string keyword)
            => (value ?? "").IndexOf(keyword,
                StringComparison.OrdinalIgnoreCase) >= 0;

        private void UpdateConfirmState()
            => ConfirmButton.IsEnabled = SelectedItem != null;

        private void Confirm()
        {
            if (SelectedItem == null) return;
            Confirmed = true;
            Close();
        }

        /// <summary>表格行显示模型。</summary>
        public sealed class CableRow
        {
            public CableRow(ListItem item)
            {
                Item = item;
                Code = item.Code ?? "";
                Spec = item.Spec ?? "";
                Name = item.Name ?? "";
                Feature = item.Feature ?? "";
            }

            public ListItem Item { get; }
            public string Code { get; }
            public string Spec { get; }
            public string Name { get; }
            public string Feature { get; }
        }
    }
}
