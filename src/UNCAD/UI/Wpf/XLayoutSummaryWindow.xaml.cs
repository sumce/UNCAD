using System;
using System.Collections.Generic;
using UNCAD.Core.Dwg;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// XLAYOUT 排版完成后的机台完整性统计窗（3.0 WPF 版，D-038，P2 模式）。
    /// 只读汇报，关闭/Esc/Enter 都只是关窗。
    /// </summary>
    public sealed partial class XLayoutSummaryWindow : FluentWindow
    {
        public string Heading => "排版完成 · " + _frameCount + " 个图框 / "
            + _rowCount + " 个机台";

        /// <summary>下一步指引：红/黄机台按详情列处理后重跑，回路画全再 U1U 出清单。</summary>
        public string NextStep
        {
            get
            {
                if (_missingMachines > 0)
                    return "下一步：红色机台补画缺的回路后重跑 XLAYOUT；回路画全后执行 U1U 生成清单。";
                if (_warningMachines > 0)
                    return "下一步：黄色机台按详情列清理重复/多出图框；回路画全后执行 U1U 生成清单。";
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

        private readonly int _frameCount;
        private readonly int _rowCount;
        private readonly int _completeMachines;
        private readonly int _warningMachines;
        private readonly int _missingMachines;
        private readonly int _unknownMachines;
        private readonly int _skippedInPlace;

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
            }
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            SummaryGrid.ItemsSource = summaries ?? new List<XLayoutMachineSummary>();
            StatusBadgeBar.Build(BadgeBar, new[]
            {
                new StatusBadge("完整 " + _completeMachines + " 台", "success"),
                new StatusBadge("警告 " + _warningMachines + " 台",
                    _warningMachines > 0 ? "caution" : "neutral"),
                new StatusBadge("缺回路 " + _missingMachines + " 台",
                    _missingMachines > 0 ? "critical" : "neutral"),
                new StatusBadge("未判断 " + _unknownMachines + " 台",
                    _unknownMachines > 0 ? "caution" : "neutral")
            });
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
