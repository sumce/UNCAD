using System.Collections.Generic;
using System.Linq;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>XLAYOUT 机台标签三色状态（2026-11-02 现场口径）：
    /// 绿=完整无异常；黄=完整但有重复或未知；红=缺回路。</summary>
    public enum XLayoutLabelStatus
    {
        Complete,
        Warning,
        Missing
    }

    /// <summary>XLAYOUT 按机台汇总的排版结果（一行一个机台）。</summary>
    public sealed class XLayoutMachineSummary
    {
        public XLayoutMachineSummary(string machineId)
        {
            MachineId = machineId ?? "";
        }

        public string MachineId { get; }
        public int CircuitCount { get; set; }
        public int ExpectedCircuitCount { get; set; }
        public int DuplicateCircuitCount { get; set; }
        /// <summary>应有回路里本次没见到的数量（红“缺回路”的唯一判据）。</summary>
        public int MissingCircuitCount { get; set; }
        /// <summary>图纸上有、Excel 基准里没有的回路数量（黄，不算缺）。</summary>
        public int ExtraCircuitCount { get; set; }
        public bool StatusKnown { get; set; }
        public bool IsComplete { get; set; }
        public List<string> MissingCircuitNames { get; } = new List<string>();
        public List<string> ExtraCircuitNames { get; } = new List<string>();

        public string StatusText => !StatusKnown ? "未判断"
            : MissingCircuitCount > 0 ? "缺少回路 " + MissingCircuitCount
            : ExtraCircuitCount > 0 ? "多出回路 " + ExtraCircuitCount
            : DuplicateCircuitCount > 0 ? "重复 " + DuplicateCircuitCount
            : IsComplete ? "完整" : "缺少回路";

        /// <summary>详情（统计窗“详情”列与日志共用）：缺少/多出的回路名各最多
        /// 列 5 个，超出折叠为“等 N 个”。</summary>
        public string DetailText
        {
            get
            {
                if (!StatusKnown) return "无 Excel 基准";
                var parts = new List<string>();
                if (MissingCircuitCount > 0)
                    parts.Add("缺 " + Brief(MissingCircuitNames, MissingCircuitCount));
                if (ExtraCircuitCount > 0)
                    parts.Add("多 " + Brief(ExtraCircuitNames, ExtraCircuitCount));
                if (DuplicateCircuitCount > 0)
                    parts.Add("重复 " + DuplicateCircuitCount + " 处");
                return string.Join("；", parts);
            }
        }

        private static string Brief(List<string> names, int total)
        {
            const int shown = 5;
            List<string> pick = names.Take(shown).ToList();
            string text = string.Join("、", pick);
            return total > pick.Count ? text + " 等 " + total + " 个" : text;
        }

        /// <summary>标签颜色判定（纯逻辑，可单测）：红只表示“应有回路缺失”
        /// （MissingCircuitCount &gt; 0）；未知、重复、多出都是警告黄；其余完整绿。
        /// 旧实现先判 IsComplete（其中已含“无重复”条件），重复机台会掉进红色
        /// “缺回路”，与 XSTS 按去重集合比对的结论相反（2026-11-03 修复）。</summary>
        public XLayoutLabelStatus LabelStatus
        {
            get
            {
                if (!StatusKnown) return XLayoutLabelStatus.Warning;
                if (IsComplete) return XLayoutLabelStatus.Complete;
                return MissingCircuitCount > 0
                    || (ExtraCircuitCount == 0 && DuplicateCircuitCount == 0)
                        ? XLayoutLabelStatus.Missing
                        : XLayoutLabelStatus.Warning;
            }
        }
    }

    /// <summary>
    /// XLAYOUT 排版完成后的机台完整性统计窗（3.0 WPF 版，D-038，P2 模式）。
    /// 只读汇报，关闭/Esc/Enter 都只是关窗。
    /// </summary>
    public sealed partial class XLayoutSummaryWindow : FluentWindow
    {
        internal string Heading => "排版完成 · " + _frameCount + " 个图框 / "
            + _rowCount + " 个机台";

        /// <summary>下一步指引：红/黄机台按详情列处理后重跑，回路画全再 U1U 出清单。</summary>
        internal string NextStep
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

        internal string Footer => "机台标签颜色与本统计只反映本次实际参与重排的回路。";

        private readonly int _frameCount;
        private readonly int _rowCount;
        private readonly int _completeMachines;
        private readonly int _warningMachines;
        private readonly int _missingMachines;
        private readonly int _unknownMachines;

        public XLayoutSummaryWindow(IReadOnlyList<XLayoutMachineSummary> summaries,
            int frameCount)
        {
            _frameCount = frameCount;
            _rowCount = summaries?.Count ?? 0;
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
    }
}
