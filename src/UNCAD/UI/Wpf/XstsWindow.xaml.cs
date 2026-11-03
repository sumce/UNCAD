using System;
using UNCAD.Core.Stat;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// XSTS 机台回路统计的只读汇报窗（3.0 WPF 版，D-038，P2 工作台模式）。
    /// 顶部：机台/图框总数 + 覆盖率 + 机台状态徽章行（完整/缺回路/多出/无基准）
    /// + 下一步指引；中间两个表格页（回路统计 / 异常图框）；页脚 Excel 路径。
    /// 关闭 / Esc / Enter 都只是关窗，不做任何写入。
    /// </summary>
    public sealed partial class XstsWindow : FluentWindow
    {
        internal string Heading => "机台回路统计 · " + _report.Machines.Count + " 个机台 / "
            + _report.FrameCount + " 个图框";

        /// <summary>下一步指引：统计是工序核对，缺什么、下一步干什么要说清。</summary>
        internal string NextStep => _report.ExpectedDataAvailable
            ? (_report.MissingCircuitCount > 0
                ? "下一步：把缺少的回路补画进图纸后重跑 XSTS 核对；全部机台齐全后执行 U1U 生成清单。"
                : "下一步：回路齐全，执行 U1U 生成清单。")
            : "下一步：先在 U1SET 刷新机台数据（" + _report.ExpectedDataDetail + "），再重跑 XSTS。";

        /// <summary>覆盖率摘要：数据可用时给出 缺少/多出/异常 的确切数字。</summary>
        internal string Coverage => _report.ExpectedDataAvailable && !_report.HasUnknownMachineBaseline
            ? "有效回路 " + _report.SelectedCircuitCount + " / 应有 "
                + _report.ExpectedCircuitCount + " · 缺少 " + _report.MissingCircuitCount
                + " · 多出 " + _report.UnexpectedCircuitCount
                + " · 异常 " + _report.Issues.Count
            : "有效回路 " + _report.SelectedCircuitCount
                + " · 应有/缺少/多出 "
                + (_report.ExpectedDataAvailable ? "部分无法判断" : "无法判断")
                + " · " + _report.ExpectedDataDetail
                + " · 异常 " + _report.Issues.Count;

        internal string Footer => string.IsNullOrWhiteSpace(_outputPath)
            ? "未导出 Excel" : "Excel: " + _outputPath;

        private readonly XstsReport _report;
        private readonly string _outputPath;

        public XstsWindow(XstsReport report, string outputPath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            _report = report;
            _outputPath = outputPath ?? "";
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            MachineGrid.ItemsSource = report.Machines;
            IssueGrid.ItemsSource = report.Issues;
            IssueTab.Header = "异常图框 (" + report.Issues.Count + ")";
            BuildBadges();
            Loaded += (sender, args) => CloseButton.Focus();
        }

        /// <summary>机台状态徽章：全局分布一眼可见（口径与命令行摘要一致）。</summary>
        internal void BuildBadges()
        {
            StatusBadgeBar.Build(BadgeBar, new[]
            {
                new StatusBadge("完整 " + _report.CompleteMachineCount + " 台", "success"),
                new StatusBadge("缺回路 " + _report.MachinesWithMissingCount + " 台",
                    _report.MachinesWithMissingCount > 0 ? "critical" : "neutral"),
                new StatusBadge("多出 " + _report.MachinesWithExtraOnlyCount + " 台",
                    _report.MachinesWithExtraOnlyCount > 0 ? "caution" : "neutral"),
                new StatusBadge("无基准 " + _report.UnknownMachineCount + " 台",
                    _report.UnknownMachineCount > 0 ? "caution" : "neutral")
            });
        }

        private void OnClose(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
