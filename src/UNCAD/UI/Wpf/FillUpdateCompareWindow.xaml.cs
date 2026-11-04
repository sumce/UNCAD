using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UNCAD.Core.Fill;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>One frame's comparison payload for FillUpdateCompareWindow.</summary>
    public sealed class FillUpdateCompareItem
    {
        public string MachineId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string FrameHandle { get; set; } = "";
        public string LastUpdatedText { get; set; } = "";
        public string LastUpdatedUser { get; set; } = "";
        public bool HasHistory { get; set; }
        public int AddedCount { get; set; }
        public int RemovedCount { get; set; }
        public int QuantityChangedCount { get; set; }
        /// <summary>左侧:上次清单(表格现有行,移除行标红)。</summary>
        public List<FillRowDiff> PreviousRows { get; set; } = new List<FillRowDiff>();
        /// <summary>右侧:本次将写入的清单(新增/改量着色)。</summary>
        public List<FillRowDiff> PlannedRows { get; set; } = new List<FillRowDiff>();
        /// <summary>电缆长度卡片（实测/盘内箱内/设备内/业主分解）；null = 本次无
        /// 预留数据，卡片隐藏（2026-11-04 用户要求）。</summary>
        public CableLengthCard CableCard { get; set; }

        /// <summary>内容是否真的变了：行新增/移除/改量任一非零。2026-11-04 起
        /// 弹窗门控用它而不是 HasHistory——内容没变不打扰用户预览。</summary>
        public bool HasChanges
            => AddedCount > 0 || RemovedCount > 0 || QuantityChangedCount > 0;
    }

    /// <summary>电缆长度变化卡片：本次构成分解 + 上次总长对比。</summary>
    public sealed class CableLengthCard
    {
        /// <summary>上次总长（来自 frameinfo_json 的 TotalCableM；未知为空）。</summary>
        public string PreviousTotal { get; set; } = "";
        /// <summary>本次总长（预留同步后的最终值，与实际写入一致）。</summary>
        public string NewTotal { get; set; } = "";
        /// <summary>差值文案：" +3 m" / "−2 m" / "无变化"；上次值未知时为空。</summary>
        public string Delta { get; set; } = "";
        public bool DeltaChanged { get; set; }
        /// <summary>构成行：实测 / 盘内（箱内）预留 / 设备内预留 / 业主预留 / 总长。</summary>
        public List<CableComponentRow> Components { get; }
            = new List<CableComponentRow>();
    }

    /// <summary>电缆卡片的一行构成（Label + 数值；总长行加粗）。</summary>
    public sealed class CableComponentRow
    {
        public CableComponentRow(string label, string value, bool isTotal)
        {
            Label = label ?? "";
            Value = value ?? "";
            IsTotal = isTotal;
        }

        public string Label { get; }
        public string Value { get; }
        public bool IsTotal { get; }
    }

    /// <summary>
    /// U1U 更新对比窗（3.0 WPF 版，D-038，P2 工作台模式）：左右两个清单并排——
    /// 左边是上次清单（表格现有内容），右边是本次将写入的清单；列与清单表格一致
    /// （序号/项目名称/单位/数量/项次编码），变化行着色。仅展示有上次更新信息
    /// 的图框；多框时左侧列表选择。
    /// </summary>
    public sealed partial class FillUpdateCompareWindow : FluentWindow
    {
        /// <summary>用户点了“继续更新”为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        private readonly List<FillUpdateCompareItem> _items;
        private readonly ObservableCollection<CompareRow> _previousRows =
            new ObservableCollection<CompareRow>();
        private readonly ObservableCollection<CompareRow> _plannedRows =
            new ObservableCollection<CompareRow>();

        public FillUpdateCompareWindow(List<FillUpdateCompareItem> items)
        {
            // 2026-11-04：弹不弹、弹哪些由调用方门控（HasChanges——内容有变化
            // 才弹），窗口只负责渲染传入清单，不再按 HasHistory 二次过滤。
            _items = (items ?? new List<FillUpdateCompareItem>()).ToList();
            bool multiple = _items.Count > 1;
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            if (!multiple)
            {
                Width = 920;
                FrameListPanel.Visibility = Visibility.Collapsed;
                FrameListColumn.Width = new GridLength(0);
            }
            else
            {
                Width = 1180;
            }

            FrameListHeader.Text = "内容有变化的图框（" + _items.Count + " 个）";
            foreach (FillUpdateCompareItem item in _items)
            {
                int changed = item.AddedCount + item.RemovedCount
                    + item.QuantityChangedCount;
                FrameList.Items.Add(item.MachineId + " · " + item.DeviceName
                    + (changed > 0 ? "  ·  " + changed + " 项变化" : ""));
            }
            if (_items.Count > 0) FrameList.SelectedIndex = 0;
            PreviousGrid.ItemsSource = _previousRows;
            PlannedGrid.ItemsSource = _plannedRows;
            ShowSelected();
        }

        private void OnFrameSelected(object sender, SelectionChangedEventArgs e)
            => ShowSelected();

        private void OnContinue(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private FillUpdateCompareItem Selected
            => FrameList.SelectedIndex >= 0 && FrameList.SelectedIndex < _items.Count
                ? _items[FrameList.SelectedIndex] : null;

        private void ShowSelected()
        {
            _previousRows.Clear();
            _plannedRows.Clear();
            FillUpdateCompareItem item = Selected;
            if (item == null)
            {
                TitleText.Text = "";
                HistoryText.Text = "";
                SummaryText.Text = "";
                CableCardPanel.Visibility = Visibility.Collapsed;
                return;
            }
            TitleText.Text = item.MachineId + " · " + item.DeviceName
                + (item.FrameHandle.Length > 0 ? "  （图框 " + item.FrameHandle + "）" : "");
            HistoryText.Text = "上次更新时间: "
                + (string.IsNullOrWhiteSpace(item.LastUpdatedText) ? "未知" : item.LastUpdatedText)
                + "    上次更新用户: "
                + (string.IsNullOrWhiteSpace(item.LastUpdatedUser) ? "未知" : item.LastUpdatedUser);
            int changed = item.AddedCount + item.RemovedCount + item.QuantityChangedCount;
            SummaryText.Text = changed == 0
                ? "✓ 本次更新与上次完全一致，清单无变化"
                : "新增 " + item.AddedCount + " / 移除 " + item.RemovedCount
                    + " / 改量 " + item.QuantityChangedCount;
            SummaryText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
                changed == 0
                    ? "SystemFillColorSuccessBrush" : "TextFillColorPrimaryBrush");
            ShowCableCard(item.CableCard);

            foreach (FillRowDiff row in item.PreviousRows)
                _previousRows.Add(new CompareRow(row, row.OldQuantity,
                    row.Status == FillRowDiff.StatusRemoved ? "removed" : "normal"));
            foreach (FillRowDiff row in item.PlannedRows)
                _plannedRows.Add(new CompareRow(row, row.NewQuantity,
                    row.Status == FillRowDiff.StatusAdded ? "added"
                        : row.Status == FillRowDiff.StatusQuantity ? "quantity" : "normal"));
        }

        /// <summary>电缆长度卡片：无数据隐藏；有数据展示总长对比与构成分解。</summary>
        private void ShowCableCard(CableLengthCard card)
        {
            if (card == null || card.Components.Count == 0)
            {
                CableCardPanel.Visibility = Visibility.Collapsed;
                return;
            }
            CableCardPanel.Visibility = Visibility.Visible;
            CableDeltaText.Text = card.Delta;
            CableDeltaText.SetResourceReference(
                System.Windows.Controls.TextBlock.ForegroundProperty,
                card.DeltaChanged
                    ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush");
            CableDeltaText.Visibility = card.Delta.Length == 0
                ? Visibility.Collapsed : Visibility.Visible;
            CableTotalText.Text = card.PreviousTotal.Length == 0
                ? "本次总长 " + card.NewTotal
                : "上次 " + card.PreviousTotal + "   →   本次 " + card.NewTotal;
            CableComponentsText.Text = string.Join("　·　",
                card.Components.Where(component => !component.IsTotal)
                    .Select(component => component.Label + " " + component.Value))
                + "　=　总长 " + card.NewTotal;
        }

        /// <summary>对比表行模型：Role 决定着色（removed/added/quantity/normal）。</summary>
        public sealed class CompareRow
        {
            public CompareRow(FillRowDiff diff, string quantity, string role)
            {
                Ordinal = diff.Order > 0
                    ? diff.Order.ToString(CultureInfo.InvariantCulture) : "—";
                Name = string.IsNullOrWhiteSpace(diff.Name) ? "—" : diff.Name;
                Unit = string.IsNullOrWhiteSpace(diff.Unit) ? "—" : diff.Unit;
                Quantity = string.IsNullOrWhiteSpace(quantity) ? "—" : quantity;
                Code = string.IsNullOrWhiteSpace(diff.Code) ? "—" : diff.Code;
                Role = role;
            }

            public string Ordinal { get; }
            public string Name { get; }
            public string Unit { get; }
            public string Quantity { get; }
            public string Code { get; }
            public string Role { get; }
        }
    }
}
