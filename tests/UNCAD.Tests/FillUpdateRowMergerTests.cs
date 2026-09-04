using System.Collections.Generic;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Features.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FillUpdateRowMergerTests
    {
        [Fact]
        public void MergeRows_UnknownMeasurementPreservesExistingQuantity()
        {
            var planned = new List<TableFillRow> { Cable("") };
            var statistics = new CableStatResult
            {
                CableState = MeasurementState.Unknown,
                BridgeState = MeasurementState.ConfirmedEmpty,
                ConduitState = MeasurementState.ConfirmedEmpty
            };

            List<TableFillRow> result = FillUpdateRowMerger.MergeRows(planned,
                new[] { Cable("12.5") }, statistics);

            Assert.Equal("12.5", Assert.Single(result).Quantity);
        }

        [Fact]
        public void MergeRows_ConfirmedEmptyDoesNotRestoreDeletedCategory()
        {
            var statistics = new CableStatResult
            {
                CableState = MeasurementState.ConfirmedEmpty,
                BridgeState = MeasurementState.ConfirmedEmpty,
                ConduitState = MeasurementState.ConfirmedEmpty
            };

            List<TableFillRow> result = FillUpdateRowMerger.MergeRows(
                new List<TableFillRow>(), new[] { Cable("12.5"), Bridge("4") }, statistics);

            Assert.Empty(result);
        }

        [Fact]
        public void MergeRows_DisplayOrdinalIsNotCatalogCode()
        {
            var statistics = new CableStatResult
            {
                CableState = MeasurementState.Unknown,
                BridgeState = MeasurementState.ConfirmedEmpty,
                ConduitState = MeasurementState.ConfirmedEmpty
            };
            List<TableFillRow> result = FillUpdateRowMerger.MergeRows(
                new List<TableFillRow>(), new[]
                {
                    new TableFillRow
                    {
                        Category = TableFillCategory.Cable,
                        Name = "\u7535\u7f06",
                        Code = "",
                        Quantity = "2",
                        SortOrder = 1
                    }
                }, statistics);

            TableFillRow row = Assert.Single(result);
            Assert.Equal("", row.Code);
            Assert.False(row.CatalogMatched);
        }

        [Fact]
        public void MergeRows_UnknownMeasurementPreservesDuplicateExistingRows()
        {
            var statistics = new CableStatResult
            {
                CableState = MeasurementState.Unknown,
                BridgeState = MeasurementState.ConfirmedEmpty,
                ConduitState = MeasurementState.ConfirmedEmpty
            };

            List<TableFillRow> result = FillUpdateRowMerger.MergeRows(
                new List<TableFillRow> { Cable("") },
                new[] { Cable("10"), Cable("20") }, statistics);

            Assert.Equal(2, result.Count);
            Assert.Equal("10", result[0].Quantity);
            Assert.Equal("20", result[1].Quantity);
        }

        [Theory]
        [InlineData("电缆桥架200*100", "")]
        [InlineData("电缆桥架200*100", "2.1")]
        public void ExistingCategory_ClassifiesCableTrayAsBridge(string name, string code)
            => Assert.Equal(TableFillCategory.Bridge,
                FillUpdateRowMerger.ExistingCategory(name, code));

        private static TableFillRow Cable(string quantity) => new TableFillRow
        {
            Category = TableFillCategory.Cable,
            SortOrder = 100,
            Name = "电缆",
            Code = "1.1",
            Quantity = quantity,
            CatalogMatched = true
        };

        private static TableFillRow Bridge(string quantity) => new TableFillRow
        {
            Category = TableFillCategory.Bridge,
            SortOrder = 200,
            Name = "桥架200*100",
            Code = "2.1",
            Quantity = quantity,
            CatalogMatched = true
        };
    }
}
