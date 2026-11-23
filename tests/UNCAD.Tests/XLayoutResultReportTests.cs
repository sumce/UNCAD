using System;
using System.Collections.Generic;
using UNCAD.Core.Dwg;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XLayoutResultReportTests
    {
        private static XLayoutFrameItem Item(string machine, string circuit,
            string handle)
            => new XLayoutFrameItem(machine, circuit,
                new Core.Geometry.FrameRectangle(handle, 0, 0, 100, 100), handle);

        [Fact]
        public void Build_WithFilterList_ClassifiesDoneNotArrangedAndNotFound()
        {
            var filter = new XLayoutFilterList(new[]
            {
                new XLayoutFilterEntry("M1", "A"),
                new XLayoutFilterEntry("M1", "B"),   // 命中但未参与排版
                new XLayoutFilterEntry("M9", "Ghost") // 图上没有
            }, "list.csv");
            var all = new List<XLayoutFrameItem>
            {
                Item("M1", "A", "h1"),
                Item("M1", "B", "h2"),
                Item("M2", "X", "h3")
            };
            var arranged = new List<XLayoutFrameItem> { all[0] };

            XLayoutResultReport report = XLayoutResultReport.Build(
                filter, all, arranged,
                new Dictionary<string, XLayoutMachineSummary>());

            Assert.True(report.HasFilterList);
            Assert.Equal(3, report.FilterRows.Count);
            Assert.Equal(XLayoutFilterOutcome.Done, report.FilterRows[0].Outcome);
            Assert.Equal("h1", report.FilterRows[0].FrameHandles);
            Assert.Equal(XLayoutFilterOutcome.NotArranged,
                report.FilterRows[1].Outcome);
            Assert.Equal(XLayoutFilterOutcome.NotFound,
                report.FilterRows[2].Outcome);
            Assert.Equal("找不到", report.FilterRows[2].OutcomeText);
            Assert.Equal("Done", report.FilterRows[0].OutcomeText);
            // 排版图框 sheet 始终在：只含本次参与的图框。
            Assert.Single(report.FrameRows);
            Assert.Equal("h1", report.FrameRows[0].FrameHandle);
        }

        [Fact]
        public void Build_MachineLevelEntry_NoteCarriesArrangedCount()
        {
            var filter = new XLayoutFilterList(
                new[] { new XLayoutFilterEntry("M1", "") }, "list.csv");
            var all = new List<XLayoutFrameItem>
            {
                Item("M1", "A", "h1"),
                Item("M1", "B", "h2")
            };

            XLayoutResultReport report = XLayoutResultReport.Build(
                filter, all, all,
                new Dictionary<string, XLayoutMachineSummary>());

            Assert.Single(report.FilterRows);
            Assert.Equal(XLayoutFilterOutcome.Done, report.FilterRows[0].Outcome);
            Assert.Equal("（整台）", report.FilterRows[0].CircuitName);
            Assert.Contains("2 个回路参与排版", report.FilterRows[0].Note);
            Assert.Equal("h1、h2", report.FilterRows[0].FrameHandles);
        }

        [Fact]
        public void Build_WithoutFilterList_OnlyFrameRows()
        {
            XLayoutResultReport report = XLayoutResultReport.Build(
                null,
                new List<XLayoutFrameItem> { Item("M1", "A", "h1") },
                new List<XLayoutFrameItem> { Item("M1", "A", "h1") },
                new Dictionary<string, XLayoutMachineSummary>());

            Assert.False(report.HasFilterList);
            Assert.Empty(report.FilterRows);
            Assert.Single(report.FrameRows);
            Assert.Equal("Done", XLayoutResultReport.DoneText);
        }

        [Fact]
        public void Build_MachineSummaryDetailFeedsNotes()
        {
            var summary = new XLayoutMachineSummary("M1")
            {
                StatusKnown = true,
                MissingCircuitCount = 1
            };
            summary.MissingCircuitNames.Add("C");

            XLayoutResultReport report = XLayoutResultReport.Build(
                new XLayoutFilterList(new[] { new XLayoutFilterEntry("M1", "A") },
                    "list.csv"),
                new List<XLayoutFrameItem> { Item("M1", "A", "h1") },
                new List<XLayoutFrameItem> { Item("M1", "A", "h1") },
                new Dictionary<string, XLayoutMachineSummary> { ["M1"] = summary });

            Assert.Contains("缺 C", report.FilterRows[0].Note);
            Assert.Contains("缺 C", report.FrameRows[0].Note);
        }
    }
}
