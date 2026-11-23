using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Text;

namespace UNCAD.Core.Dwg
{
    /// <summary>清单条目在本次 XLAYOUT 里的结果状态（D-055）。</summary>
    public enum XLayoutFilterOutcome
    {
        /// <summary>条目命中图框且本次已参与排版。</summary>
        Done,
        /// <summary>条目命中的图框存在，但本次未勾选参与排版（清单预勾选后被手动改掉）。</summary>
        NotArranged,
        /// <summary>图纸上找不到该机台/回路。</summary>
        NotFound
    }

    /// <summary>结果导出「以清单为纲」的一行（D-055）。</summary>
    public sealed class XLayoutResultListRow
    {
        public string MachineId { get; set; } = "";
        public string CircuitName { get; set; } = "";
        public XLayoutFilterOutcome Outcome { get; set; }
        public string FrameHandles { get; set; } = "";
        public string Note { get; set; } = "";

        public string OutcomeText => Outcome == XLayoutFilterOutcome.Done ? "Done"
            : Outcome == XLayoutFilterOutcome.NotArranged ? "未参与排版"
            : "找不到";
    }

    /// <summary>结果导出「排版图框」的一行：本次参与排版的图框逐框一行。</summary>
    public sealed class XLayoutResultFrameRow
    {
        public string MachineId { get; set; } = "";
        public string CircuitName { get; set; } = "";
        public string FrameHandle { get; set; } = "";
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// XLAYOUT 结果报告的纯数据模型（D-055）：清单为纲（导入清单时逐条
    /// Done/未参与排版/找不到）+ 本次参与排版的图框明细。未导入清单时只有
    /// 图框明细（全部参与图框为纲）。构建是纯逻辑，可单测。
    /// </summary>
    public sealed class XLayoutResultReport
    {
        public static readonly string DoneText = "Done";
        public static readonly string NotFoundText = "找不到";

        public XLayoutResultReport()
        {
            FilterRows = new List<XLayoutResultListRow>();
            FrameRows = new List<XLayoutResultFrameRow>();
        }

        public bool HasFilterList { get; set; }
        public string FilterSourcePath { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
        public List<XLayoutResultListRow> FilterRows { get; }
        public List<XLayoutResultFrameRow> FrameRows { get; }

        /// <summary>
        /// 构建报告。allItems = 本次识别到的全部图框（判定「找不到」的对照全集）；
        /// arrangedItems = 实际参与排版的图框；arrangedSummaries = 按机台汇总
        /// （备注取 DetailText，缺回路/重复等信息随行导出）。filter 为 null/空
        /// 时不出清单 sheet。
        /// </summary>
        public static XLayoutResultReport Build(XLayoutFilterList filter,
            IReadOnlyList<XLayoutFrameItem> allItems,
            IReadOnlyList<XLayoutFrameItem> arrangedItems,
            IReadOnlyDictionary<string, XLayoutMachineSummary> arrangedSummaries)
        {
            allItems = allItems ?? Array.Empty<XLayoutFrameItem>();
            arrangedItems = arrangedItems ?? Array.Empty<XLayoutFrameItem>();
            var arrangedHandles = new HashSet<string>(
                arrangedItems.Select(item => item.Handle ?? ""),
                StringComparer.OrdinalIgnoreCase);
            var summaries = new Dictionary<string, XLayoutMachineSummary>(
                IdentityTextNormalizer.Comparer);
            foreach (KeyValuePair<string, XLayoutMachineSummary> pair
                in arrangedSummaries ?? new Dictionary<string, XLayoutMachineSummary>())
                summaries[IdentityTextNormalizer.Key(pair.Key)] = pair.Value;

            var report = new XLayoutResultReport
            {
                GeneratedAt = DateTime.Now,
                HasFilterList = filter != null && !filter.IsEmpty,
                FilterSourcePath = filter?.SourcePath ?? ""
            };

            if (report.HasFilterList)
            {
                foreach (XLayoutFilterMatch match in filter.MatchAll(allItems))
                {
                    List<XLayoutFrameItem> arranged = match.Items
                        .Where(item => arrangedHandles.Contains(item.Handle ?? ""))
                        .ToList();
                    XLayoutFilterOutcome outcome = match.Found
                        ? (arranged.Count > 0
                            ? XLayoutFilterOutcome.Done
                            : XLayoutFilterOutcome.NotArranged)
                        : XLayoutFilterOutcome.NotFound;
                    string note = "";
                    if (match.Entry.IsMachineLevel && arranged.Count > 0)
                        note = arranged.Count + " 个回路参与排版";
                    summaries.TryGetValue(match.Entry.MachineKey,
                        out XLayoutMachineSummary summary);
                    if (summary != null && summary.StatusKnown
                        && summary.DetailText.Length > 0)
                        note = note.Length == 0
                            ? summary.DetailText
                            : note + "；" + summary.DetailText;
                    report.FilterRows.Add(new XLayoutResultListRow
                    {
                        MachineId = match.Entry.MachineId,
                        CircuitName = match.Entry.IsMachineLevel
                            ? "（整台）" : match.Entry.CircuitName,
                        Outcome = outcome,
                        FrameHandles = string.Join("、", arranged
                            .Select(item => item.Handle ?? "")
                            .Where(handle => handle.Length > 0)),
                        Note = note
                    });
                }
            }

            foreach (XLayoutFrameItem item in arrangedItems)
            {
                summaries.TryGetValue(IdentityTextNormalizer.Key(item.MachineId),
                    out XLayoutMachineSummary summary);
                report.FrameRows.Add(new XLayoutResultFrameRow
                {
                    MachineId = item.MachineId ?? "",
                    CircuitName = item.DeviceName ?? "",
                    FrameHandle = item.Handle ?? "",
                    Note = summary != null && summary.StatusKnown
                        && summary.DetailText.Length > 0
                        ? summary.DetailText : ""
                });
            }
            return report;
        }
    }
}
