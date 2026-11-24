using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UNCAD.Core.Dwg;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// XLAYOUT 排版完成后的机台完整性统计窗（3.0 WPF 版，D-038，P2 模式）。
    /// 上半：机台密排表（图框/应有/缺/多/重复/待确认/状态/详情）；下半
    /// （D-057）：选中机台的**全量**回路名单（缺少/多出/待确认/重复图框）
    /// 与本次参与排版的图框明细——双击图框行或点「定位图框」把视图平移到
    /// 那张图框。只读汇报：定位只改视图，不改图形；关闭/Esc/Enter 只是关窗。
    /// </summary>
    public sealed partial class XLayoutSummaryWindow : FluentWindow
    {
        public string Heading => "排版完成 · " + _frameCount + " 个图框 / "
            + _rowCount + " 个机台";

        /// <summary>下一步指引：红=补画缺的回路；黄=处理重复/多出，或给同名
        /// 回路的图框做 U1F/U1U 指认；回路画全再 U1U 出清单。</summary>
        public string NextStep
        {
            get
            {
                if (_missingMachines > 0)
                    return "下一步：红色机台补画缺的回路后重跑 XLAYOUT；回路画全后执行 U1U 生成清单。";
                if (_unresolvedMachines > 0)
                    return "下一步：「待确认」机台的同名回路图框未记录上游编号——对其执行 U1F 或 U1U 指认后重跑 XLAYOUT。";
                if (_warningMachines > 0)
                    return "下一步：黄色机台按详情清理重复/多出图框；回路画全后执行 U1U 生成清单。";
                return "下一步：全部机台完整，执行 U1U 生成清单。";
            }
        }

        public string Footer => "机台标签颜色与本统计只反映本次实际参与重排的回路。"
            + (_skippedInPlace > 0 ? " 原位跳过 " + _skippedInPlace + " 个图框。" : "");

        /// <summary>
        /// 导出回调（Features 注入，D-055）：写出 Excel 并返回文件路径；
        /// 抛异常=失败（异常消息进提示文本）。UI 只消费结果，不落盘。
        /// </summary>
        public Func<string> ExportRequested { get; set; }

        /// <summary>
        /// 定位回调（Features 注入，D-057）：参数为图框句柄，返回是否成功。
        /// 只平移视图，不改图形。
        /// </summary>
        public Func<string, bool> LocateRequested { get; set; }

        private readonly int _frameCount;
        private readonly int _rowCount;
        private readonly int _completeMachines;
        private readonly int _warningMachines;
        private readonly int _missingMachines;
        private readonly int _unknownMachines;
        private readonly int _unresolvedMachines;
        private readonly int _skippedInPlace;

        /// <summary>详情条当前显示的行（测试/外部断言用）。</summary>
        internal IReadOnlyList<KeyValuePair<string, string>> DetailRows => _detailRows;
        internal string DetailTitleText => DetailTitle.Text;

        private List<KeyValuePair<string, string>> _detailRows =
            new List<KeyValuePair<string, string>>();

        public XLayoutSummaryWindow(IReadOnlyList<XLayoutMachineSummary> summaries,
            int frameCount, int skippedInPlace = 0)
        {
            _frameCount = frameCount;
            _rowCount = summaries?.Count ?? 0;
            _skippedInPlace = Math.Max(0, skippedInPlace);
            foreach (XLayoutMachineSummary summary in summaries
            ?? (IReadOnlyList<XLayoutMachineSummary>)new List<XLayoutMachineSummary>())
            {
                switch (summary.LabelStatus)
                {
                    case XLayoutLabelStatus.Complete: _completeMachines++; break;
                    case XLayoutLabelStatus.Missing: _missingMachines++; break;
                    default: _warningMachines++; break;
                }
                if (!summary.StatusKnown) _unknownMachines++;
                if (summary.StatusKnown && summary.UnresolvedCircuitCount > 0)
                    _unresolvedMachines++;
            }
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            SummaryGrid.ItemsSource = summaries ?? new List<XLayoutMachineSummary>();
            // 开窗即选中首台机台：详情不用再点一下才出现。
            if (SummaryGrid.Items.Count > 0) SummaryGrid.SelectedIndex = 0;
            StatusBadgeBar.Build(BadgeBar, new[]
            {
                new StatusBadge("完整 " + _completeMachines + " 台", "success"),
                new StatusBadge("警告 " + _warningMachines + " 台",
                    _warningMachines > 0 ? "caution" : "neutral"),
                new StatusBadge("缺回路 " + _missingMachines + " 台",
                    _missingMachines > 0 ? "critical" : "neutral"),
                new StatusBadge("待确认 " + _unresolvedMachines + " 台",
                    _unresolvedMachines > 0 ? "caution" : "neutral"),
                new StatusBadge("未判断 " + _unknownMachines + " 台",
                    _unknownMachines > 0 ? "caution" : "neutral")
            });
        }

        private void OnSummarySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SummaryGrid.SelectedItem is XLayoutMachineSummary summary)
                ShowMachineDetail(summary);
        }

        /// <summary>选中机台：全量回路名单 + 图框明细。</summary>
        internal void ShowMachineDetail(XLayoutMachineSummary summary)
        {
            string title = "机台 " + summary.MachineId + " · 本次排版 " + summary.CircuitCount
                + " 个图框"
                + (summary.StatusKnown ? " / 应有 " + summary.ExpectedCircuitCount + " 条回路" : "")
                + " · " + summary.StatusText;
            var rows = new List<KeyValuePair<string, string>>();
            if (!summary.StatusKnown)
                rows.Add(Row("基准", new[]
                    { "无 Excel 基准，未判断回路是否齐全（U1SET 刷新机台数据后重跑）" }));
            else
            {
                rows.Add(Row("缺少回路", summary.MissingCircuitNames));
                rows.Add(Row("多出回路", summary.ExtraCircuitNames));
            }
            rows.Add(Row("待确认", summary.UnresolvedCircuitNames));
            rows.Add(Row("重复图框", summary.DuplicateCircuitNames));
            _detailRows = rows;
            DetailTitle.Text = title;
            DetailBody.Children.Clear();
            Brush secondary = BrushOf("TextFillColorSecondaryBrush", Brushes.DimGray);
            Brush primary = BrushOf("TextFillColorPrimaryBrush", Brushes.Black);
            foreach (KeyValuePair<string, string> row in rows)
            {
                bool quiet = row.Value == "无";
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                line.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = row.Key,
                    Width = 66,
                    FontSize = 12,
                    Foreground = secondary,
                    VerticalAlignment = VerticalAlignment.Top
                });
                line.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = row.Value,
                    FontSize = 12,
                    MaxWidth = 360,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = quiet ? secondary : primary
                });
                DetailBody.Children.Add(line);
            }
            FrameGrid.ItemsSource = summary.Frames;
            LocateButton.IsEnabled = false;
        }

        private static KeyValuePair<string, string> Row(string label,
            IReadOnlyList<string> values)
            => new KeyValuePair<string, string>(label,
                values == null || values.Count == 0 ? "无" : string.Join("；", values));

        private Brush BrushOf(string key, Brush fallback)
        {
            try { return (Brush)FindResource(key) ?? fallback; }
            catch { return fallback; }
        }

        // ---------- 定位图框（D-057） ----------

        private void OnFrameSelectionChanged(object sender, SelectionChangedEventArgs e)
            => LocateButton.IsEnabled = FrameGrid.SelectedItem is XLayoutFrameRow
                && LocateRequested != null;

        private void OnFrameDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FrameGrid.SelectedItem is XLayoutFrameRow) LocateSelectedFrame();
        }

        private void OnLocate(object sender, RoutedEventArgs e) => LocateSelectedFrame();

        /// <summary>把视图平移到图框明细里选中的那张图框（Features 注入回调）。</summary>
        internal void LocateSelectedFrame()
        {
            if (!(FrameGrid.SelectedItem is XLayoutFrameRow row)) return;
            Func<string, bool> locate = LocateRequested;
            if (locate == null)
            {
                ExportPathText.Text = "当前环境无法定位图框。";
                return;
            }
            bool found;
            try { found = locate(row.Handle); }
            catch (System.Exception ex) { found = false; Log.Warn("[XLAYOUT] 定位图框失败：" + ex.Message); }
            ExportPathText.Text = found
                ? "已定位到图框 " + row.Handle + "（" + row.CircuitName + "）。"
                : "定位失败：图框 " + row.Handle + " 不在当前图形里。";
        }

        private void OnClose(object sender, System.Windows.RoutedEventArgs e) => Close();

        private void OnExportExcel(object sender, System.Windows.RoutedEventArgs e)
        {
            Func<string> export = ExportRequested;
            if (export == null)
            {
                ExportPathText.Text = "导出不可用。";
                return;
            }
            ExportButton.IsEnabled = false;
            try
            {
                string path = export();
                ExportPathText.Text = "已导出：" + path;
            }
            catch (System.Exception ex)
            {
                ExportButton.IsEnabled = true;
                ExportPathText.Text = "导出失败：" + ex.Message;
            }
        }
    }
}
