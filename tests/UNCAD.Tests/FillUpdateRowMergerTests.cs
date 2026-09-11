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
        public void MergeRows_MeasuredConduitUsesTheFreshPlannedQuantity()
        {
            var statistics = new CableStatResult
            {
                CableState = MeasurementState.Unknown,
                BridgeState = MeasurementState.ConfirmedEmpty,
                ConduitState = MeasurementState.Measured
            };

            List<TableFillRow> result = FillUpdateRowMerger.MergeRows(
                new List<TableFillRow> { Conduit("5") },
                new[] { Conduit("2") }, statistics);

            Assert.Equal("5", Assert.Single(result).Quantity);
        }

        [Fact]
        public void MergeRows_UnknownConduitPreservesTheExistingQuantity()
        {
            var statistics = new CableStatResult
            {
                CableState = MeasurementState.ConfirmedEmpty,
                BridgeState = MeasurementState.ConfirmedEmpty,
                ConduitState = MeasurementState.Unknown
            };

            List<TableFillRow> result = FillUpdateRowMerger.MergeRows(
                new List<TableFillRow> { Conduit("") },
                new[] { Conduit("2") }, statistics);

            Assert.Equal("2", Assert.Single(result).Quantity);
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

        [Fact]
        public void MergeRows_NameOnlyMatchKeepsQuantityButNotTheCatalogCode()
        {
            // 本次一格电缆长度都没测到、框选范围又不完整 → CableState = Unknown，
            // 于是走进“保留旧行”分支。此时规划行的电缆型号在清单里查不到，没有编码，
            // 名称回退成通用的“电缆”；旧行也叫“电缆”但带着 1.10 的编码。
            //
            // 只凭“电缆”这两个字根本证明不了两行是同一根电缆，所以旧编码不能借过来——
            // 借了就等于给 3*185+1*95 盖上 3*10 的编码，而两道写出闸都会放行。
            var statistics = new CableStatResult
            {
                CableState = MeasurementState.Unknown,
                BridgeState = MeasurementState.ConfirmedEmpty,
                ConduitState = MeasurementState.ConfirmedEmpty
            };
            var planned = new List<TableFillRow>
            {
                new TableFillRow
                {
                    Category = TableFillCategory.Cable,
                    SortOrder = 100,
                    Name = "电缆",
                    Code = "",
                    Quantity = "",
                    CatalogMatched = false
                }
            };
            var existing = new[]
            {
                new TableFillRow
                {
                    Category = TableFillCategory.Cable,
                    SortOrder = 1,
                    Name = "电缆",
                    Code = "1.10",
                    Quantity = "12.5",
                    CatalogMatched = true
                }
            };

            TableFillRow row = Assert.Single(
                FillUpdateRowMerger.MergeRows(planned, existing, statistics));

            Assert.Equal("12.5", row.Quantity);  // 没测到就沿用旧数量
            Assert.Equal("", row.Code);          // 身份不跨行传染
            Assert.False(row.CatalogMatched);
        }

        [Fact]
        public void MergeRows_SameCodeKeepsQuantityAndIdentity()
        {
            // 旧行与新行编码一致时，才是同一项材料，数量和身份都该保留。
            var statistics = new CableStatResult
            {
                CableState = MeasurementState.Unknown,
                BridgeState = MeasurementState.ConfirmedEmpty,
                ConduitState = MeasurementState.ConfirmedEmpty
            };

            List<TableFillRow> result = FillUpdateRowMerger.MergeRows(
                new List<TableFillRow> { Cable("") }, new[] { Cable("12.5") }, statistics);

            TableFillRow row = Assert.Single(result);
            Assert.Equal("12.5", row.Quantity);
            Assert.Equal("1.1", row.Code);
            Assert.True(row.CatalogMatched);
        }

        [Theory]
        [InlineData("电缆桥架200*100", "")]
        [InlineData("电缆桥架200*100", "2.1")]
        public void ExistingCategory_ClassifiesCableTrayAsBridge(string name, string code)
            => Assert.Equal(TableFillCategory.Bridge,
                FillUpdateRowMerger.ExistingCategory(name, code));

        [Fact]
        public void ExistingCategory_CleansFormattedCatalogCode()
            => Assert.Equal(TableFillCategory.Cable,
                FillUpdateRowMerger.ExistingCategory("电缆",
                    @"{\fSimSun|b0|i0;1.1}"));

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

        private static TableFillRow Conduit(string quantity) => new TableFillRow
        {
            Category = TableFillCategory.RigidConduit,
            SortOrder = 300,
            Name = "镀锌穿线管",
            Code = "3.1",
            Quantity = quantity,
            CatalogMatched = true
        };
    }
}
