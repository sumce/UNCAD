using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using Xunit;

namespace UNCAD.Tests
{
    public class FillRowDiffBuilderTests
    {
        private static TableFillRow Row(string name, string quantity, string code = "")
            => new TableFillRow
            {
                Name = name,
                Quantity = quantity,
                Code = code,
                Unit = "米"
            };

        [Fact]
        public void Build_MatchesByCodeAndFlagsQuantityChange()
        {
            var existing = new List<TableFillRow>
            {
                Row("电缆", "100", "1.1"),
                Row("桥架", "50", "2.1")
            };
            var planned = new List<TableFillRow>
            {
                Row("电缆", "120", "1.1"),
                Row("桥架", "50", "2.1")
            };

            var diffs = FillRowDiffBuilder.Build(existing, planned);

            Assert.Equal(2, diffs.Count);
            Assert.True(diffs[0].Status == FillRowDiff.StatusQuantity, "改量应排最前");
            Assert.Equal("120", diffs[0].NewQuantity);
            Assert.Equal("100", diffs[0].OldQuantity);
            Assert.True(diffs[0].Changed);
            Assert.Equal(FillRowDiff.StatusKept, diffs[1].Status);
            Assert.False(diffs[1].Changed);
        }

        [Fact]
        public void Build_ReportsAddedAndRemovedRows()
        {
            var existing = new List<TableFillRow> { Row("旧桥架", "40", "2.1") };
            var planned = new List<TableFillRow> { Row("新电缆", "80", "1.1") };

            var diffs = FillRowDiffBuilder.Build(existing, planned);

            Assert.Contains(diffs, d => d.Status == FillRowDiff.StatusAdded
                && d.Name == "新电缆");
            Assert.Contains(diffs, d => d.Status == FillRowDiff.StatusRemoved
                && d.Name == "旧桥架");
        }

        [Fact]
        public void Build_DuplicateCodeReportsUnpairedExistingRowAsRemoved()
        {
            var existing = new List<TableFillRow>
            {
                Row("Cable A", "10", "1.1"),
                Row("Cable B", "20", "1.1")
            };
            var planned = new List<TableFillRow> { Row("Cable A", "10", "1.1") };

            List<FillRowDiff> diffs = FillRowDiffBuilder.Build(existing, planned);

            Assert.Equal(2, diffs.Count);
            Assert.Equal(FillRowDiff.StatusKept, diffs[0].Status);
            Assert.Equal(FillRowDiff.StatusRemoved, diffs[1].Status);
            Assert.Equal("20", diffs[1].OldQuantity);

            Tuple<List<FillRowDiff>, List<FillRowDiff>> sides =
                FillRowDiffBuilder.BuildSides(existing, planned);
            Assert.Equal(FillRowDiff.StatusKept, sides.Item1[0].Status);
            Assert.Equal(FillRowDiff.StatusRemoved, sides.Item1[1].Status);
            Assert.Equal(FillRowDiff.StatusKept, Assert.Single(sides.Item2).Status);
        }

        [Fact]
        public void Build_DuplicateCodeReportsUnpairedPlannedRowAsAdded()
        {
            var existing = new List<TableFillRow> { Row("Cable A", "10", "1.1") };
            var planned = new List<TableFillRow>
            {
                Row("Cable A", "10", "1.1"),
                Row("Cable B", "20", "1.1")
            };

            List<FillRowDiff> diffs = FillRowDiffBuilder.Build(existing, planned);

            Assert.Equal(2, diffs.Count);
            Assert.Equal(FillRowDiff.StatusKept, diffs[0].Status);
            Assert.Equal(FillRowDiff.StatusAdded, diffs[1].Status);
            Assert.Equal("20", diffs[1].NewQuantity);

            Tuple<List<FillRowDiff>, List<FillRowDiff>> sides =
                FillRowDiffBuilder.BuildSides(existing, planned);
            Assert.Equal(FillRowDiff.StatusKept, Assert.Single(sides.Item1).Status);
            Assert.Equal(FillRowDiff.StatusKept, sides.Item2[0].Status);
            Assert.Equal(FillRowDiff.StatusAdded, sides.Item2[1].Status);
        }

        [Fact]
        public void Build_CodeLessDuplicateNamesMatchByOccurrence()
        {
            var existing = new List<TableFillRow>
            {
                Row("Custom", "10"),
                Row("Custom", "20")
            };
            var planned = new List<TableFillRow>
            {
                Row("custom", "10"),
                Row("CUSTOM", "25")
            };

            List<FillRowDiff> diffs = FillRowDiffBuilder.Build(existing, planned);

            Assert.Equal(FillRowDiff.StatusKept, diffs[0].Status);
            Assert.Equal(FillRowDiff.StatusQuantity, diffs[1].Status);
            Assert.Equal("20", diffs[1].OldQuantity);
            Assert.Equal("25", diffs[1].NewQuantity);
        }

        [Fact]
        public void BuildSides_RightSideMarksMatchedQuantityChange()
        {
            var existing = new List<TableFillRow> { Row("Cable", "10", "1.1") };
            var planned = new List<TableFillRow> { Row("Cable", "12", "1.1") };

            Tuple<List<FillRowDiff>, List<FillRowDiff>> sides =
                FillRowDiffBuilder.BuildSides(existing, planned);

            FillRowDiff right = Assert.Single(sides.Item2);
            Assert.Equal(FillRowDiff.StatusQuantity, right.Status);
            Assert.True(right.Changed);
            Assert.Equal("10", right.OldQuantity);
            Assert.Equal("12", right.NewQuantity);
        }

        [Fact]
        public void ReadRows_SkipsHeaderAndOrdinalRowsKeepContent()
        {
            var source = new SubmissionSourceData();
            source.AddTableRow("NO.", "设备名称", "规格", "单位", "数量", "编号");
            source.AddTableRow("1", "电缆 WDZ-YJY-4x35", "", "米", "100", "1.1");
            source.AddTableRow("2", "桥架200*100", "", "米", "50", "");

            var rows = FillRowDiffBuilder.ReadRows(source);

            Assert.Equal(2, rows.Count);
            Assert.Equal("电缆 WDZ-YJY-4x35", rows[0].Name);
            Assert.Equal("1.1", rows[0].Code);
            Assert.Equal("50", rows[1].Quantity);
        }

        [Fact]
        public void Update_RecordsLastModifiedUser()
        {
            FrameInfoJsonRecord updated = FrameInfoJsonRecordUpdater.Update(
                null, new MachineRow { MachineId = "M01" }, null, "U1U",
                new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc),
                "zhangsan");
            Assert.Equal("zhangsan", updated.LastModifiedUser);
        }

        [Theory]
        [InlineData("1*16", true)]
        [InlineData("1x16", true)]
        [InlineData("1*16mm2", true)]
        [InlineData("1*16mm²", true)]
        [InlineData(" 1 × 16 ", true)]
        [InlineData("3*120+1*70", false)]
        [InlineData("1*16+1*16", false)]
        [InlineData("4*35", false)]
        [InlineData("", false)]
        public void IsGroundingCableModel_MatchesOnlyBare1x16(string model, bool expected)
        {
            Assert.Equal(expected, TableFillPlanner.IsGroundingCableModel(model));
        }

        [Fact]
        public void Catalog_ExposesGroundingRow85()
        {
            var catalog = new BoqCatalogIndex(UNCAD.Core.Excel.ListItemReader.ReadEmbedded());
            var grounding = catalog.FindByCode("8.5");
            Assert.NotNull(grounding);
            Assert.Equal("设备接地独立连接", grounding.Name);
            Assert.NotEqual("电缆", grounding.Category);
        }
    }
}
