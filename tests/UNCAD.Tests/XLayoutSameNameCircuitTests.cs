using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Dwg;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Geometry;
using UNCAD.Core.Text;
using UNCAD.Features.XLayout;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-057：XLAYOUT 对「一个机台两条同名回路（上游编号不同）」的口径。
    /// 3.5.0 之前按回路名分组：合法的同名回路被当成“重复”画黄框，应有个数
    /// 塌缩，按上游排版时两条挤进同一行，行序共用第一条的。
    /// </summary>
    public sealed class XLayoutSameNameCircuitTests
    {
        private const string Machine = "MEDPC01";
        private const string Circuit = "AC Rack";
        private const string UpA = "3N-11B1A-PP-02";
        private const string UpB = "3N-31D2A-PP-03";

        private static string Key(string circuit, string upstream)
            => CircuitIdentityKey.Of(new MachineRow
                { MachineId = Machine, CircuitName = circuit, Fr = upstream });

        private static MachineRow Row(string circuit, string upstream)
            => new MachineRow
            {
                MachineId = Machine, CircuitName = circuit, Fr = upstream,
                Region = "AMHS", Batch = "B1", Seq = "7"
            };

        private static XLayoutFrameItem Item(string circuit, string upstream, int x,
            string handle, bool keyed = true)
            => new XLayoutFrameItem(Machine, circuit,
                new FrameRectangle(handle, x, 0, x + 800, 400), handle,
                upstream ?? "", keyed && upstream != null ? Key(circuit, upstream) : "");

        private static XLayoutArrangeContext Context(params MachineRow[] rows)
        {
            var context = new XLayoutArrangeContext();
            int order = 0;
            foreach (MachineRow row in rows) context.AddRow(row, order++);
            return context;
        }

        private static string[] RowLabels(IReadOnlyList<XLayoutPlacement> placements)
            => placements.GroupBy(placement => placement.RowIndex)
                .OrderBy(group => group.Key)
                .Select(group => group.First().RowLabel).ToArray();

        [Fact]
        public void SameNameCircuits_EachFollowTheirOwnExcelRowOrder()
        {
            // Excel 行序：AC Rack（上游 A）在前、AC Rack（上游 B）在后；图上位置相反。
            var context = Context(Row(Circuit, UpA), Row(Circuit, UpB), Row("LP", "3N-LP-01"));
            var items = new[]
            {
                Item(Circuit, UpB, 2000, "h2"),
                Item(Circuit, UpA, 0, "h1")
            };

            IReadOnlyList<XLayoutPlacement> placements = XLayoutLayout.Arrange(items,
                context, XLayoutArrangeMode.MachineReadiness);

            // 旧口径两条共用第一条的行序，退回图上位置 → B 在前。
            Assert.Equal(new[] { "h1", "h2" }, placements
                .Select(placement => placement.Item.Handle).ToArray());
        }

        [Fact]
        public void UpstreamMode_SameNameCircuitsLandInTheirOwnUpstreamRows()
        {
            var context = Context(Row(Circuit, UpA), Row(Circuit, UpB));
            var items = new[]
            {
                Item(Circuit, UpA, 0, "h1"),
                Item(Circuit, UpB, 2000, "h2")
            };

            IReadOnlyList<XLayoutPlacement> placements = XLayoutLayout.Arrange(items,
                context, XLayoutArrangeMode.Upstream);

            // 旧口径两条都掉进“最后一条的上游”那一行。
            Assert.Equal(new[] { UpA, UpB }, RowLabels(placements));
            Assert.NotEqual(placements[0].RowIndex, placements[1].RowIndex);
        }

        [Fact]
        public void UpstreamMode_KeylessSameNameFramesGoToThePendingRowNotUnassigned()
        {
            var context = Context(Row(Circuit, UpA), Row(Circuit, UpB));
            var items = new[]
            {
                Item(Circuit, null, 0, "h1", keyed: false),
                Item(Circuit, null, 2000, "h2", keyed: false),
                Item("LP", null, 4000, "h3", keyed: false)
            };

            IReadOnlyList<XLayoutPlacement> placements = XLayoutLayout.Arrange(items,
                context, XLayoutArrangeMode.Upstream);

            // LP 名字唯一：照旧按名取不到上游 → “未配上游”；同名两条进“待确认”行排最后。
            Assert.Equal(new[] { XLayoutLayout.UnassignedUpstreamLabel,
                    XLayoutLayout.AmbiguousUpstreamLabel }, RowLabels(placements));
            Assert.Equal(2, placements.Count(placement =>
                placement.RowLabel == XLayoutLayout.AmbiguousUpstreamLabel));
        }

        [Fact]
        public void UpstreamMode_UpstreamAnnotationsDoNotSplitOneFeederIntoTwoRows()
        {
            var context = Context(
                new MachineRow { MachineId = "M1", CircuitName = "Pump", Fr = "3U-UPS10D1-PP-03 36(43/AC)" },
                new MachineRow { MachineId = "M2", CircuitName = "Fan", Fr = "3U-UPS10D1-PP-03" });
            var items = new[]
            {
                new XLayoutFrameItem("M1", "Pump", new FrameRectangle("h1", 0, 0, 800, 400), "h1",
                    "3U-UPS10D1-PP-03 36(43/AC)", Key2("M1", "Pump", "3U-UPS10D1-PP-03 36(43/AC)")),
                new XLayoutFrameItem("M2", "Fan", new FrameRectangle("h2", 2000, 0, 2800, 400), "h2",
                    "3U-UPS10D1-PP-03", Key2("M2", "Fan", "3U-UPS10D1-PP-03"))
            };

            IReadOnlyList<XLayoutPlacement> placements = XLayoutLayout.Arrange(items,
                context, XLayoutArrangeMode.Upstream);

            // 同一上游的两种写法归同一行；行首标签用去掉标注的编号。
            Assert.Equal(new[] { "3U-UPS10D1-PP-03" }, RowLabels(placements));
        }

        private static string Key2(string machine, string circuit, string upstream)
            => CircuitIdentityKey.Of(new MachineRow
                { MachineId = machine, CircuitName = circuit, Fr = upstream });

        // ---- 机台统计：统一配对内核 ----

        private static XLayoutWorkbookContext Workbook(params MachineRow[] rows)
        {
            var byMachine = new Dictionary<string, List<MachineRow>>(
                IdentityTextNormalizer.Comparer);
            foreach (MachineRow row in rows)
            {
                string key = IdentityTextNormalizer.Key(row.MachineId);
                if (!byMachine.TryGetValue(key, out List<MachineRow> bucket))
                    byMachine[key] = bucket = new List<MachineRow>();
                bucket.Add(row);
            }
            return new XLayoutWorkbookContext(true, byMachine, rows.ToList(),
                new Dictionary<string, string>());
        }

        [Fact]
        public void SameNameBothDrawn_IsCompleteNotDuplicateAndGetsNoWarningMarker()
        {
            var workbook = Workbook(Row(Circuit, UpA), Row(Circuit, UpB));
            var warning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, XLayoutMachineSummary> summaries =
                XLayoutCircuitStatus.Evaluate(
                    new[] { Item(Circuit, UpA, 0, "h1"), Item(Circuit, UpB, 2000, "h2") },
                    workbook, warning);

            XLayoutMachineSummary summary = Assert.Single(summaries.Values);
            Assert.True(summary.IsComplete);
            Assert.Equal(2, summary.CircuitCount);
            Assert.Equal(2, summary.ExpectedCircuitCount);
            Assert.Equal(0, summary.DuplicateCircuitCount);
            Assert.Equal(0, summary.MissingCircuitCount);
            Assert.Equal(0, summary.UnresolvedCircuitCount);
            Assert.Equal(XLayoutLabelStatus.Complete, summary.LabelStatus);
            Assert.Equal("完整", summary.StatusText);
            Assert.Empty(warning);
            Assert.Equal(new[] { UpA, UpB }, summary.Frames.Select(frame => frame.UpstreamId));
            Assert.All(summary.Frames, frame => Assert.Equal("已核实", frame.State));
        }

        [Fact]
        public void SameNameOnlyOneDrawn_ReportsTheOtherWithItsUpstream()
        {
            var workbook = Workbook(Row(Circuit, UpA), Row(Circuit, UpB), Row("LP", "3N-LP-01"));

            Dictionary<string, XLayoutMachineSummary> summaries = XLayoutCircuitStatus.Evaluate(
                new[] { Item(Circuit, UpB, 0, "h2") }, workbook, new HashSet<string>());

            XLayoutMachineSummary summary = Assert.Single(summaries.Values);
            Assert.Equal(3, summary.ExpectedCircuitCount);
            // 缺两条：另一条同名回路（带上游编号点名）+ LP。
            Assert.Equal(2, summary.MissingCircuitCount);
            Assert.Contains("AC Rack（上游 3N-11B1A-PP-02）", summary.MissingCircuitNames);
            Assert.Contains("LP", summary.MissingCircuitNames);
            Assert.Equal(XLayoutLabelStatus.Missing, summary.LabelStatus);
        }

        [Fact]
        public void KeylessSameNameFrame_IsPendingWithWarningMarkerNotRed()
        {
            var workbook = Workbook(Row(Circuit, UpA), Row(Circuit, UpB));
            var warning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, XLayoutMachineSummary> summaries = XLayoutCircuitStatus.Evaluate(
                new[] { Item(Circuit, null, 0, "h1", keyed: false) }, workbook, warning);

            XLayoutMachineSummary summary = Assert.Single(summaries.Values);
            Assert.Equal(1, summary.UnresolvedCircuitCount);
            Assert.Equal(1, summary.MissingCircuitCount);
            // 图框已经画了、缺的是上游编号：黄色 + 指引做 U1F/U1U，不是红色补画。
            Assert.Equal(XLayoutLabelStatus.Warning, summary.LabelStatus);
            Assert.Equal("待确认 1 · 缺少回路 1", summary.StatusText);
            Assert.Contains("h1", warning);
            string detail = Assert.Single(summary.UnresolvedCircuitNames);
            Assert.Contains("2 条同名回路", detail, StringComparison.Ordinal);
            Assert.Contains("h1", detail, StringComparison.Ordinal);
            Assert.Equal("待确认", Assert.Single(summary.Frames).State);
        }

        [Fact]
        public void SameCircuitDrawnTwice_OnlyTheSurplusFrameGetsTheWarningMarker()
        {
            var workbook = Workbook(Row(Circuit, UpA), Row(Circuit, UpB));
            var warning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, XLayoutMachineSummary> summaries = XLayoutCircuitStatus.Evaluate(
                new[] { Item(Circuit, UpA, 0, "h1"), Item(Circuit, UpA, 2000, "h2") },
                workbook, warning);

            XLayoutMachineSummary summary = Assert.Single(summaries.Values);
            Assert.Equal(1, summary.DuplicateCircuitCount);
            // 旧口径把同名/同回路的图框整组画黄框；现在只框多出来的那张，
            // 统计里点名它与哪一张重复。
            Assert.Equal("h2", Assert.Single(warning));
            Assert.Contains(summary.DuplicateCircuitNames,
                name => name.Contains("h2") && name.Contains("h1"));
            // 重复的那张不顶替另一条同名回路。
            Assert.Equal(1, summary.MissingCircuitCount);
        }

        [Fact]
        public void WithoutSnapshot_SameNameDifferentUpstreamIsNotADuplicate()
        {
            var unavailable = new XLayoutWorkbookContext(false,
                new Dictionary<string, List<MachineRow>>(), new List<MachineRow>(),
                new Dictionary<string, string>());
            var warning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, XLayoutMachineSummary> summaries = XLayoutCircuitStatus.Evaluate(
                new[] { Item(Circuit, UpA, 0, "h1"), Item(Circuit, UpB, 2000, "h2") },
                unavailable, warning);

            XLayoutMachineSummary summary = Assert.Single(summaries.Values);
            Assert.False(summary.StatusKnown);
            Assert.Equal(0, summary.DuplicateCircuitCount);
            Assert.Empty(warning);
            Assert.Equal("无基准", summary.Frames[0].State);
        }

        // ---- 行首标签颜色 ----

        private static XLayoutMachineSummary Summary(string machine,
            XLayoutLabelStatus status)
        {
            var summary = new XLayoutMachineSummary(machine) { StatusKnown = true };
            switch (status)
            {
                case XLayoutLabelStatus.Complete:
                    summary.IsComplete = true;
                    break;
                case XLayoutLabelStatus.Missing:
                    summary.MissingCircuitCount = 1;
                    break;
                default:
                    summary.ExtraCircuitCount = 1;
                    break;
            }
            return summary;
        }

        [Fact]
        public void RowLabelColor_TakesTheWorstMachineOfTheRow()
        {
            // 上游模式一行跨多台机台：最左是完整的，右边那台缺回路 → 行标签必须红。
            var summaries = new Dictionary<string, XLayoutMachineSummary>(
                IdentityTextNormalizer.Comparer)
            {
                ["MA"] = Summary("MA", XLayoutLabelStatus.Complete),
                ["MB"] = Summary("MB", XLayoutLabelStatus.Missing),
                ["MC"] = Summary("MC", XLayoutLabelStatus.Warning)
            };
            XLayoutPlacement Placement(string machine) => placementsOf(machine);

            XLayoutPlacement placementsOf(string machine)
            {
                var item = new XLayoutFrameItem(machine, "C",
                    new FrameRectangle(machine, 0, 0, 800, 400), machine);
                return XLayoutLayout.Arrange(new[] { item }, XLayoutArrangeContext.Empty,
                    XLayoutArrangeMode.MachineReadiness)[0];
            }

            Assert.Equal(1, XLayoutFeature.RowLabelColor(summaries,
                new[] { Placement("MA"), Placement("MB") }));   // 红：右边那台缺回路
            Assert.Equal(2, XLayoutFeature.RowLabelColor(summaries,
                new[] { Placement("MC"), Placement("MA") }));   // 黄：有警告机台
            Assert.Equal(3, XLayoutFeature.RowLabelColor(summaries,
                new[] { Placement("MA"), Placement("MA") }));   // 绿：整行完整
        }

        // ---- 清单导入 / 结果导出 ----

        [Fact]
        public void FilterEntryWithUpstream_MatchesOnlyThatSameNameFrame()
        {
            var entry = new XLayoutFilterEntry(Machine, Circuit, UpB);
            Assert.True(XLayoutFilterList.Matches(entry, Item(Circuit, UpB, 0, "h2")));
            Assert.False(XLayoutFilterList.Matches(entry, Item(Circuit, UpA, 0, "h1")));
            Assert.Equal("MEDPC01-AC Rack（上游 3N-31D2A-PP-03）", entry.DisplayText);

            // 不带上游的条目：同名图框全部命中（齐全性由结果报表判）。
            var plain = new XLayoutFilterEntry(Machine, Circuit);
            Assert.True(XLayoutFilterList.Matches(plain, Item(Circuit, UpA, 0, "h1")));
            Assert.True(XLayoutFilterList.Matches(plain, Item(Circuit, UpB, 0, "h2")));
        }

        [Fact]
        public void ResultReport_SameNameCircuitPartiallyArranged_IsPartialNotDone()
        {
            var filter = new XLayoutFilterList(
                new[] { new XLayoutFilterEntry(Machine, Circuit) }, "list.xlsx");
            var all = new List<XLayoutFrameItem>
            {
                Item(Circuit, UpA, 0, "h1"), Item(Circuit, UpB, 2000, "h2")
            };

            XLayoutResultReport partial = XLayoutResultReport.Build(filter, all,
                new List<XLayoutFrameItem> { all[1] },
                new Dictionary<string, XLayoutMachineSummary>(),
                new[] { Row(Circuit, UpA), Row(Circuit, UpB) });
            Assert.Equal(XLayoutFilterOutcome.Partial, Assert.Single(partial.FilterRows).Outcome);
            Assert.Equal("不全", partial.FilterRows[0].OutcomeText);
            Assert.Contains("同名回路 2 条", partial.FilterRows[0].Note, StringComparison.Ordinal);

            XLayoutResultReport complete = XLayoutResultReport.Build(filter, all, all,
                new Dictionary<string, XLayoutMachineSummary>(),
                new[] { Row(Circuit, UpA), Row(Circuit, UpB) });
            Assert.Equal(XLayoutFilterOutcome.Done, complete.FilterRows[0].Outcome);
            Assert.Equal(new[] { UpA, UpB }, complete.FrameRows.Select(row => row.UpstreamId));
        }

        [Fact]
        public void FilterListReader_OptionalUpstreamColumnIsReadAndDeduplicatedWithIt()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad_filter_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                var workbook = new XSSFWorkbook();
                NPOI.SS.UserModel.ISheet sheet = workbook.CreateSheet("清单");
                NPOI.SS.UserModel.IRow header = sheet.CreateRow(0);
                header.CreateCell(0).SetCellValue("机台ID");
                header.CreateCell(1).SetCellValue("回路名称");
                header.CreateCell(2).SetCellValue("U_上游编号");
                string[][] lines =
                {
                    new[] { Machine, Circuit, UpA },
                    new[] { Machine, Circuit, UpB },
                    new[] { Machine, Circuit, UpA },   // 重复条目
                    new[] { Machine, "LP", "" }
                };
                for (int index = 0; index < lines.Length; index++)
                {
                    NPOI.SS.UserModel.IRow row = sheet.CreateRow(index + 1);
                    for (int column = 0; column < 3; column++)
                        row.CreateCell(column).SetCellValue(lines[index][column]);
                }
                using (var stream = File.Create(path)) workbook.Write(stream);
                workbook.Close();

                Assert.True(XLayoutFilterListReader.TryRead(path,
                    out XLayoutFilterList list, out string error), error);
                // 两条同名回路各算一条；重复的去掉。
                Assert.Equal(3, list.Entries.Count);
                Assert.Equal(new[] { UpA, UpB, "" }, list.Entries.Select(entry => entry.UpstreamId));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
