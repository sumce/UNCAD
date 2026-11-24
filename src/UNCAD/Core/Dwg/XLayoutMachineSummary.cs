using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Fill;

namespace UNCAD.Core.Dwg
{
    /// <summary>XLAYOUT 机台标签三色状态（2026-11-02 现场口径）：
    /// 绿=完整无异常；黄=完整但有重复、待确认或未知；红=缺回路。</summary>
    public enum XLayoutLabelStatus
    {
        Complete,
        Warning,
        Missing
    }

    /// <summary>
    /// 本次参与排版的一张图框（统计窗“图框明细”用，D-057）。State 取值：
    /// 已核实 / 重复 / 待确认 / 多出 / 无基准。
    /// </summary>
    public sealed class XLayoutFrameRow
    {
        public string Handle { get; set; } = "";
        public string CircuitName { get; set; } = "";
        public string UpstreamId { get; set; } = "";
        public string State { get; set; } = "";
    }

    /// <summary>XLAYOUT 按机台汇总的排版结果（一行一个机台）。
    /// 2026-11-04 从 UI/Wpf/XLayoutSummaryWindow.xaml.cs 下沉到 Core/Dwg：
    /// 标签颜色判定是 Features 绘图要消费的业务规则，不得驻留窗体层。</summary>
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
        /// <summary>
        /// D-057：机台内有同名回路，而图框没有可用的回路身份键（未记录上游
        /// 编号），无法判定对应哪一条。黄——先做 U1F/U1U 指认，不是补画。
        /// </summary>
        public int UnresolvedCircuitCount { get; set; }
        public bool StatusKnown { get; set; }
        public bool IsComplete { get; set; }
        public List<string> MissingCircuitNames { get; } = new List<string>();
        public List<string> ExtraCircuitNames { get; } = new List<string>();
        public List<string> UnresolvedCircuitNames { get; } = new List<string>();
        /// <summary>重复图框说明（回路名 + 本框/被重复框的句柄）。</summary>
        public List<string> DuplicateCircuitNames { get; } = new List<string>();
        /// <summary>本次参与排版的图框明细。</summary>
        public List<XLayoutFrameRow> Frames { get; } = new List<XLayoutFrameRow>();

        /// <summary>本次参与排版的图框里带上游编号的张数（详情条提示用）。</summary>
        public int FramesWithUpstream => Frames.Count(frame => frame.UpstreamId.Length > 0);

        public string StatusText
        {
            get
            {
                if (!StatusKnown) return "未判断";
                var parts = new List<string>();
                if (UnresolvedCircuitCount > 0) parts.Add("待确认 " + UnresolvedCircuitCount);
                if (MissingCircuitCount > 0) parts.Add("缺少回路 " + MissingCircuitCount);
                if (ExtraCircuitCount > 0) parts.Add("多出回路 " + ExtraCircuitCount);
                if (DuplicateCircuitCount > 0) parts.Add("重复 " + DuplicateCircuitCount);
                if (parts.Count > 0) return string.Join(" · ", parts);
                return IsComplete ? "完整" : "缺少回路";
            }
        }

        /// <summary>详情（统计窗“详情”列与日志共用）：缺少/多出/待确认/重复各最多
        /// 列 5 条，超出折叠为“等 N 个”；全量名单见对应 *Names 列表。</summary>
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
                if (UnresolvedCircuitCount > 0)
                    parts.Add("待确认 " + Brief(UnresolvedCircuitNames, UnresolvedCircuitCount));
                if (DuplicateCircuitCount > 0)
                    // 重复图框说明可能没逐条给出（手工汇总）：那时只报处数。
                    parts.Add(DuplicateCircuitNames.Count == 0
                        ? "重复 " + DuplicateCircuitCount + " 处"
                        : "重复 " + Brief(DuplicateCircuitNames, DuplicateCircuitCount));
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
        /// “缺回路”，与 XSTS 按去重集合比对的结论相反（2026-11-03 修复）。
        /// D-057：同名回路待确认一律黄——图框已经画了，缺的是上游编号，
        /// 指引用户去做 U1F/U1U 指认而不是补画。</summary>
        public XLayoutLabelStatus LabelStatus
        {
            get
            {
                if (!StatusKnown || UnresolvedCircuitCount > 0) return XLayoutLabelStatus.Warning;
                if (IsComplete) return XLayoutLabelStatus.Complete;
                return MissingCircuitCount > 0
                    || (ExtraCircuitCount == 0 && DuplicateCircuitCount == 0)
                        ? XLayoutLabelStatus.Missing
                        : XLayoutLabelStatus.Warning;
            }
        }

        /// <summary>
        /// D-057：用统一配对结果（<see cref="CircuitMatcher"/>）填充机台统计。
        /// frames = 本次参与排版的图框（与配对时的 drawn 一一对应）。
        /// </summary>
        public void Apply(CircuitMatchResult match, IEnumerable<XLayoutFrameRow> frames)
        {
            if (match == null) return;
            Frames.Clear();
            MissingCircuitNames.Clear();
            ExtraCircuitNames.Clear();
            UnresolvedCircuitNames.Clear();
            DuplicateCircuitNames.Clear();
            foreach (XLayoutFrameRow frame in frames ?? Enumerable.Empty<XLayoutFrameRow>())
                Frames.Add(frame);
            CircuitCount = Frames.Count;

            var states = new Dictionary<CircuitSelection, string>();
            var duplicateOf = new Dictionary<CircuitSelection, CircuitSelection>();
            foreach (CircuitDuplicate duplicate in match.Duplicates)
            {
                states[duplicate.Drawn] = "重复";
                if (duplicate.Original != null) duplicateOf[duplicate.Drawn] = duplicate.Original;
            }
            foreach (CircuitSelection item in match.Extra) states[item] = "多出";
            foreach (CircuitSelection item in match.Unresolved) states[item] = "待确认";
            foreach (CircuitPair pair in match.Pairs) states[pair.Drawn] = "已核实";
            var drawnByHandle = new Dictionary<string, CircuitSelection>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (CircuitSelection item in match.Selected)
                if (!string.IsNullOrEmpty(item.Handle)) drawnByHandle[item.Handle] = item;
            foreach (XLayoutFrameRow frame in Frames)
            {
                if (drawnByHandle.TryGetValue(frame.Handle ?? "", out CircuitSelection drawn)
                    && states.TryGetValue(drawn, out string state))
                    frame.State = state;
                else frame.State = match.HasBaseline ? "已核实" : "无基准";
            }

            DuplicateCircuitCount = match.Duplicates.Count;
            foreach (CircuitDuplicate duplicate in match.Duplicates)
            {
                CircuitSelection drawn = duplicate.Drawn;
                string text = "图框 " + (drawn.Handle ?? "") + " "
                    + CircuitMatchText.Circuit(drawn, match.ExpectedNameCounts
                        .TryGetValue(CircuitMatchResult.NameKey(drawn.CircuitName), out int names)
                        && names > 1);
                if (!match.HasBaseline && !duplicate.Certain)
                    text += "（与图框 " + (duplicate.Original?.Handle ?? "—")
                        + " 同名，无基准，无法判断是否重复）";
                else if (duplicate.Original == null)
                    text += "（同名回路已全部上图，本图框多出一张）";
                else text += "（与图框 " + duplicate.Original.Handle + " 重复）";
                DuplicateCircuitNames.Add(text);
            }
            if (!match.HasBaseline)
            {
                StatusKnown = false;
                ExpectedCircuitCount = 0;
                UnresolvedCircuitCount = match.Duplicates.Count(
                    duplicate => !duplicate.Certain);
                UnresolvedCircuitNames.AddRange(CircuitMatchText.Unresolved(match));
                IsComplete = false;
                return;
            }
            StatusKnown = true;
            ExpectedCircuitCount = match.Expected.Count;
            MissingCircuitNames.AddRange(CircuitMatchText.Missing(match));
            MissingCircuitCount = CircuitMatchText.MissingCount(match);
            ExtraCircuitNames.AddRange(CircuitMatchText.Extra(match));
            ExtraCircuitCount = match.Extra.Count;
            UnresolvedCircuitNames.AddRange(CircuitMatchText.Unresolved(match));
            UnresolvedCircuitCount = match.Unresolved.Count;
            IsComplete = MissingCircuitCount == 0 && ExtraCircuitCount == 0
                && DuplicateCircuitCount == 0 && UnresolvedCircuitCount == 0;
        }

        /// <summary>
        /// D-057：按机台就绪等级（D-045）：0 完整 / 1 重复、多出或待确认 /
        /// 2 缺回路 / 3 无基准。
        /// </summary>
        public int ReadinessRank => !StatusKnown ? 3
            : IsComplete ? 0
            : MissingCircuitCount > 0 ? 2
            : 1;
    }
}
