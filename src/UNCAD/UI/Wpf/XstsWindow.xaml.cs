using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using UNCAD.Core.Stat;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace UNCAD.UI
{
    /// <summary>
    /// XSTS 机台回路统计的只读汇报窗（3.0 WPF 版，D-038，P2 工作站模式）。
    /// 顶部：机台/图框总数 + 覆盖率 + 机台状态徽章行（完整/缺回路/多出/无基准/
    /// 同名回路待确认）+ 下一步指引；回路统计 / 图框明细 / 异常图框 三个密排
    /// 页签（行高 26）；底部详情条随选中行切换（D-057）——机台行列出已选/
    /// 缺少/多出/待确认的全部回路名，图框行列出该框清单明细，异常行给全文。
    /// 关闭 / Esc / Enter 都只是关窗，不做任何写入。
    /// </summary>
    public sealed partial class XstsWindow : FluentWindow
    {
        public string Heading => "机台回路统计 · " + _report.Machines.Count + " 个机台 / "
            + _report.FrameCount + " 个图框";

        /// <summary>下一步指引：统计是工序核对，缺什么、下一步干什么要说清。</summary>
        public string NextStep => !_report.ExpectedDataAvailable
            ? "下一步：先在 U1SET 刷新机台数据（" + _report.ExpectedDataDetail + "），再重跑 XSTS。"
            : _report.MissingCircuitCount > 0
                ? "下一步：把缺少的回路补画进图纸后重跑 XSTS 核对；全部机台齐全后执行 U1U 生成清单。"
                : _report.UnresolvedCircuitCount > 0
                    ? "下一步：有待确认的同名回路图框（未记录上游编号）——对其执行 U1F 或 U1U 指认后重跑 XSTS。"
                    : _report.Issues.Count > 0 || _report.UnexpectedCircuitCount > 0
                        || _report.HasUnknownMachineBaseline
                        ? "下一步：先核对图框明细和异常图框中的问题，再重跑 XSTS。"
                        : "下一步：回路齐全，执行 U1U 生成清单。";

        /// <summary>覆盖率摘要：数据可用时给出 缺少/多出/待确认/异常 的确切数字。</summary>
        public string Coverage => _report.ExpectedDataAvailable && !_report.HasUnknownMachineBaseline
            ? "有效回路 " + _report.SelectedCircuitCount + " / 应有 "
                + _report.ExpectedCircuitCount + " · 缺少 " + _report.MissingCircuitCount
                + " · 多出 " + _report.UnexpectedCircuitCount
                + (_report.UnresolvedCircuitCount > 0
                    ? " · 同名回路待确认 " + _report.UnresolvedCircuitCount : "")
                + (_report.DuplicateFrameCount > 0
                    ? " · 重复图框 " + _report.DuplicateFrameCount : "")
                + " · 异常 " + _report.Issues.Count
            : "有效回路 " + _report.SelectedCircuitCount
                + " · 应有/缺少/多出 "
                + (_report.ExpectedDataAvailable ? "部分无法判断" : "无法判断")
                + " · " + _report.ExpectedDataDetail
                + " · 异常 " + _report.Issues.Count;

        public string Footer => string.IsNullOrWhiteSpace(_outputPath)
            ? "未导出 Excel" : "Excel: " + _outputPath;

        /// <summary>详情条当前显示的行（测试/外部断言用）。</summary>
        internal IReadOnlyList<KeyValuePair<string, string>> DetailRows => _detailRows;
        internal string DetailTitleText => DetailTitle.Text;

        private readonly XstsReport _report;
        private readonly string _outputPath;
        private List<KeyValuePair<string, string>> _detailRows =
            new List<KeyValuePair<string, string>>();

        public XstsWindow(XstsReport report, string outputPath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            _report = report;
            _outputPath = outputPath ?? "";
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            MachineGrid.ItemsSource = report.Machines;
            for (int index = 0; index < report.MaterialColumns.Count; index++)
            {
                string path = "Cells[" + index + "]";
                var textStyle = new Style(typeof(TextBlock));
                textStyle.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
                textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,
                    TextTrimming.CharacterEllipsis));
                textStyle.Setters.Add(new Setter(ToolTipProperty,
                    new Binding(path + ".DetailText")));
                textStyle.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 60000));
                FrameGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = report.MaterialColumns[index].ModelHeader,
                    Binding = new Binding(path + ".Model"),
                    ElementStyle = textStyle,
                    Width = Math.Max(210, report.MaterialColumns[index].ModelHeader.Length * 13 + 20)
                });
                FrameGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = report.MaterialColumns[index].QuantityHeader,
                    Binding = new Binding(path + ".Quantity"),
                    ElementStyle = textStyle,
                    Width = Math.Max(100, report.MaterialColumns[index].QuantityHeader.Length * 13 + 20)
                });
            }
            FrameGrid.ItemsSource = report.Frames;
            FrameTab.Header = "图框明细 (" + report.FrameCount + " 框)";
            IssueGrid.ItemsSource = report.Issues;
            IssueTab.Header = "异常图框 (" + report.Issues.Count + ")";
            BuildBadges();
            // 首屏就有内容：默认选中第一台机台，详情条不用再点一下才出现。
            if (report.Machines.Count > 0) MachineGrid.SelectedIndex = 0;
            Loaded += (sender, args) => CloseButton.Focus();
        }

        /// <summary>机台状态徽章：全局分布一眼可见（口径与命令行摘要一致）。</summary>
        internal void BuildBadges()
        {
            var badges = new List<StatusBadge>
            {
                new StatusBadge("完整 " + _report.CompleteMachineCount + " 台", "success"),
                new StatusBadge("缺回路 " + _report.MachinesWithMissingCount + " 台",
                    _report.MachinesWithMissingCount > 0 ? "critical" : "neutral"),
                new StatusBadge("多出 " + _report.MachinesWithExtraOnlyCount + " 台",
                    _report.MachinesWithExtraOnlyCount > 0 ? "caution" : "neutral"),
                new StatusBadge("待确认 " + _report.MachinesWithUnresolvedCount + " 台",
                    _report.MachinesWithUnresolvedCount > 0 ? "caution" : "neutral"),
                new StatusBadge("无基准 " + _report.UnknownMachineCount + " 台",
                    _report.UnknownMachineCount > 0 ? "caution" : "neutral")
            };
            StatusBadgeBar.Build(BadgeBar, badges);
        }

        // ---------- 详情条（D-057） ----------

        private void OnMachineSelected(object sender, SelectionChangedEventArgs e)
        {
            if (MachineGrid.SelectedItem is XstsMachineSummary machine)
                ShowMachineDetail(machine);
        }

        private void OnFrameSelected(object sender, SelectionChangedEventArgs e)
        {
            if (FrameGrid.SelectedItem is XstsFrameDetail frame) ShowFrameDetail(frame);
        }

        private void OnIssueSelected(object sender, SelectionChangedEventArgs e)
        {
            if (IssueGrid.SelectedItem is XstsFrameIssue issue) ShowIssueDetail(issue);
        }

        /// <summary>机台行：已选/缺少/多出/待确认的全部回路名（格子里的只是截断预览）。</summary>
        internal void ShowMachineDetail(XstsMachineSummary machine)
        {
            string title = "机台 " + machine.MachineId
                + (machine.Batch.Length > 0 ? " · 批次 " + machine.Batch : "")
                + (machine.Seq.Length > 0 ? " · 序号 " + machine.Seq : "")
                + " · 已选 " + machine.SelectedCircuitCount
                + " / 应有 " + machine.ExpectedCircuitText
                + (machine.DuplicateFrameCount > 0 ? " · 重复图框 " + machine.DuplicateFrameCount : "")
                + (machine.UnresolvedCircuitCount > 0 ? " · 待确认 " + machine.UnresolvedCircuitCount : "");
            bool known = machine.ExpectedDataAvailable;
            ShowDetail(title,
                Row("已选回路", machine.SelectedCircuits),
                Row("缺少回路", known ? machine.MissingCircuits : null),
                Row("多出回路", known ? machine.UnexpectedCircuits : null),
                Row("待确认", machine.UnresolvedCircuits));
        }

        /// <summary>图框行：身份 + 清单逐项（序号/名称/型号/数量/编码）。</summary>
        internal void ShowFrameDetail(XstsFrameDetail frame)
        {
            var lines = new List<string>();
            foreach (XstsFrameItem item in frame.Items)
                lines.Add(item.Number + "  " + item.Name + " ｜ " + item.Model
                    + " ｜ " + item.Quantity + (item.Unit.Length > 0 ? " " + item.Unit : "")
                    + (item.Code.Length > 0 ? " ｜ " + item.Code : ""));
            string circuit = frame.CircuitName
                + (frame.UpstreamId.Length > 0 ? "（上游 " + frame.UpstreamId + "）" : "");
            ShowDetail("图框 " + (frame.FrameHandle.Length > 0 ? frame.FrameHandle : "—")
                    + " · " + frame.MachineId + " / " + circuit
                    + (frame.MachineId.Length == 0 && frame.CircuitName.Length == 0
                        ? "（空图框或身份读取失败，见异常图框页）" : ""),
                Row("清单 " + frame.Items.Count + " 项", lines));
        }

        /// <summary>异常行：问题全文（格子里是截断的单行）。</summary>
        internal void ShowIssueDetail(XstsFrameIssue issue)
        {
            ShowDetail("异常 · 图框 " + issue.FrameIdText
                    + (issue.MachineId.Length > 0 ? " · " + issue.MachineId : "")
                    + (issue.CircuitName.Length > 0 ? " / " + issue.CircuitName : "")
                    + (issue.UpstreamId.Length > 0 ? "（上游 " + issue.UpstreamId + "）" : ""),
                Row("问题", new[] { issue.Description }));
        }

        private static KeyValuePair<string, string> Row(string label,
            IReadOnlyList<string> values)
        {
            if (values == null) return new KeyValuePair<string, string>(label, "无法判断（机台数据未刷新或无基准）");
            return new KeyValuePair<string, string>(label,
                values.Count == 0 ? "无" : string.Join("、", values));
        }

        private void ShowDetail(string title,
            params KeyValuePair<string, string>[] rows)
        {
            _detailRows = new List<KeyValuePair<string, string>>(rows);
            DetailTitle.Text = title;
            DetailBody.Children.Clear();
            Brush secondary = BrushOf("TextFillColorSecondaryBrush", Brushes.DimGray);
            Brush primary = BrushOf("TextFillColorPrimaryBrush", Brushes.Black);
            foreach (KeyValuePair<string, string> row in rows)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                line.Children.Add(new TextBlock
                {
                    Text = row.Key,
                    Width = 78,
                    FontSize = 12,
                    Foreground = secondary,
                    VerticalAlignment = VerticalAlignment.Top
                });
                line.Children.Add(new TextBlock
                {
                    Text = row.Value,
                    FontSize = 12,
                    MaxWidth = 1040,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = primary
                });
                DetailBody.Children.Add(line);
            }
            DetailStrip.Visibility = Visibility.Visible;
            if (!_detailCollapsed) DetailScroll.Visibility = Visibility.Visible;
            DetailToggle.Content = _detailCollapsed ? "展开" : "收起";
            DetailToggle.IsChecked = !_detailCollapsed;
        }

        /// <summary>取 Fluent 主题画刷；主题字典缺席时回退系统色，不让汇报窗因
        /// 资源名缺失而打不开（展示件不抛）。</summary>
        private Brush BrushOf(string key, Brush fallback)
        {
            try { return (Brush)FindResource(key) ?? fallback; }
            catch { return fallback; }
        }

        private bool _detailCollapsed;

        private void OnDetailToggle(object sender, RoutedEventArgs e)
        {
            _detailCollapsed = !_detailCollapsed;
            DetailScroll.Visibility = _detailCollapsed
                ? Visibility.Collapsed : Visibility.Visible;
            DetailToggle.Content = _detailCollapsed ? "展开" : "收起";
            DetailToggle.IsChecked = !_detailCollapsed;
        }

        private void OnClose(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
