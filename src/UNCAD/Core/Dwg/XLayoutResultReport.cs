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
        /// <summary>
        /// D-057：条目命中的图框参与了排版，但该回路在台账里有同名多条
        /// （上游编号不同），本次只排了其中一部分。
        /// </summary>
        Partial,
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
        /// <summary>条目带的上游编号（可空）。</summary>
        public string UpstreamId { get; set; } = "";
        public XLayoutFilterOutcome Outcome { get; set; }
        public string FrameHandles { get; set; } = "";
        public string Note { get; set; } = "";

        public string OutcomeText => Outcome == XLayoutFilterOutcome.Done ? "Done"
            : Outcome == XLayoutFilterOutcome.Partial ? "不全"
            : Outcome == XLayoutFilterOutcome.NotArranged ? "未参与排版"
            : "找不到";
    }

    /// <summary>结果导出「排版图框」的一行：本次参与排版的图框逐框一行。</summary>
    public sealed class XLayoutResultFrameRow
    {
        public string MachineId { get; set; } = "";
        public string CircuitName { get; set; } = "";
        /// <summary>D-057：图框记录的上游编号（同机台同名回路靠它认领）。</summary>
        public string UpstreamId { get; set; } = "";
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
        /// 时不出清单 sheet。workbookRows（D-057，可空）= 台账行：回路级条目
        /// 在台账里有同名多条时，只排了一部分记「不全」而不是 Done。
        /// </summary>
        public static XLayoutResultReport Build(XLayoutFilterList filter,
            IReadOnlyList<XLayoutFrameItem> allItems,
            IReadOnlyList<XLayoutFrameItem> arrangedItems,
            IReadOnlyDictionary<string, XLayoutMachineSummary> arrangedSummaries,
            IEnumerable<UNCAD.Core.Excel.MachineRow> workbookRows = null)
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
                    // D-057：回路级条目遇上台账里的同名回路（上游不同）——
                    // 只排了一部分不能记 Done。
                    if (!match.Entry.IsMachineLevel
                        && outcome == XLayoutFilterOutcome.Done)
                    {
                        string partial = SameNamePartialNote(match.Entry, arranged,
                            workbookRows);
                        if (partial.Length > 0)
                        {
                            outcome = XLayoutFilterOutcome.Partial;
                            note = partial;
                        }
                    }
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
                        UpstreamId = match.Entry.UpstreamId,
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
                    UpstreamId = item.UpstreamId ?? "",
                    FrameHandle = item.Handle ?? "",
                    Note = summary != null && summary.StatusKnown
                        && summary.DetailText.Length > 0
                        ? summary.DetailText : ""
                });
            }
            return report;
        }

        /// <summary>
        /// 台账里该机台该回路名有多条（上游不同）而本次只排了其中一部分时，
        /// 返回「不全」说明；齐全、无台账或回路名唯一时返回空串。
        /// </summary>
        private static string SameNamePartialNote(XLayoutFilterEntry entry,
            List<XLayoutFrameItem> arranged, IEnumerable<UNCAD.Core.Excel.MachineRow> workbookRows)
        {
            if (workbookRows == null) return "";
            int expected = 0;
            foreach (UNCAD.Core.Excel.MachineRow row in workbookRows)
            {
                if (IdentityTextNormalizer.Equals(row.MachineId, entry.MachineId)
                    && IdentityTextNormalizer.Equals(row.CircuitName, entry.CircuitName))
                    expected++;
            }
            if (expected <= 1) return "";
            int arrangedCircuits = arranged
                .Select(item => item.CircuitKey.Length > 0
                    ? "k:" + item.CircuitKey
                    : "n:" + IdentityTextNormalizer.Key(item.DeviceName) + "|"
                        + UNCAD.Core.Fill.CircuitIdentityKey.UpstreamKey(item.UpstreamId))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return arrangedCircuits >= expected ? ""
                : "同名回路 " + expected + " 条（上游编号不同），本次排版 "
                    + arrangedCircuits + " 条";
        }
    }
}
