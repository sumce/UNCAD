using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class UpdateOutletPolicyTests
    {
        [Fact]
        public void PreserveExisting_DoesNotRegenerateDeletedOutlet()
        {
            List<TableFillRow> result = UpdateOutletPolicy.PreserveExisting(
                PlannedWithOutlet("机台Excel插座"), new TableFillRow[0]);

            Assert.DoesNotContain(result, UpdateOutletPolicy.IsOutlet);
            Assert.Equal(new[] { "电缆", "软管" }, result.Select(row => row.Name));
        }

        [Fact]
        public void PreserveExisting_KeepsCadOutletValuesInsteadOfPlannedValues()
        {
            var existing = new TableFillRow
            {
                Category = TableFillCategory.Manual,
                Name = "用户保留插座",
                Description = "CAD中的原描述",
                Unit = "套",
                Quantity = "2",
                // 8.2/8.3 才是插座编码；有编码时由编码说了算（见 IsOutlet）。
                Code = "8.2",
                CatalogMatched = false
            };

            List<TableFillRow> result = UpdateOutletPolicy.PreserveExisting(
                PlannedWithOutlet("机台Excel插座"), new[] { existing });

            TableFillRow outlet = Assert.Single(result, UpdateOutletPolicy.IsOutlet);
            Assert.Equal("用户保留插座", outlet.Name);
            Assert.Equal("CAD中的原描述", outlet.Description);
            Assert.Equal("套", outlet.Unit);
            Assert.Equal("2", outlet.Quantity);
            Assert.Equal("8.2", outlet.Code);
            Assert.True(outlet.CatalogMatched);
        }

        [Theory]
        [InlineData("8.11", "插座漏电相序检测仪", false)]  // 名字带“插座”，但不是插座
        [InlineData("8.9", "绝缘测试仪（数字式摇表）", false)]
        [InlineData("8.2", "插座", true)]
        [InlineData("8.3", "插座", true)]
        [InlineData("", "插座", true)]                     // 老图行，无编码，靠名字
        [InlineData("", "电缆", false)]
        public void IsOutlet_LetsTheCatalogCodeDecideWhenTheRowHasOne(
            string code, string name, bool expected)
        {
            var row = new TableFillRow
            {
                Category = TableFillCategory.Manual,
                Name = name,
                Code = code
            };

            Assert.Equal(expected, UpdateOutletPolicy.IsOutlet(row));
        }

        [Fact]
        public void PreserveExisting_KeepsACatalogRowNamedLikeASocket()
        {
            // 用户在清单确认里加的 8.11「插座漏电相序检测仪」不能被当成插座删掉。
            var added = new TableFillRow
            {
                Category = TableFillCategory.Manual,
                Name = "插座漏电相序检测仪",
                Code = "8.11",
                Quantity = "1",
                CatalogMatched = true
            };

            List<TableFillRow> result = UpdateOutletPolicy.PreserveExisting(
                new[] { added }, new TableFillRow[0]);

            Assert.Equal("8.11", Assert.Single(result).Code);
        }

        [Fact]
        public void PreserveExistingOrAdd_KeepsExistingQuantityAndMaterial()
        {
            var existing = new TableFillRow
            {
                Category = TableFillCategory.Manual,
                Name = "Existing outlet",
                Description = "User description",
                Unit = "pcs",
                Quantity = "7",
                Code = "8.2",
                CatalogMatched = false
            };
            var generated = new TableFillRow
            {
                Category = TableFillCategory.Outlet,
                Name = "Generated outlet",
                Quantity = "1",
                Code = "8.3",
                SortOrder = 800,
                CatalogMatched = true
            };

            List<TableFillRow> result = UpdateOutletPolicy.PreserveExistingOrAdd(
                PlannedWithOutlet("planned"), true, generated, new[] { existing });

            TableFillRow outlet = Assert.Single(result, UpdateOutletPolicy.IsOutlet);
            Assert.Equal("Existing outlet", outlet.Name);
            Assert.Equal("7", outlet.Quantity);
            Assert.Equal("8.2", outlet.Code);
            Assert.True(outlet.CatalogMatched);
        }

        [Fact]
        public void PreserveExistingOrAdd_CreatesOneWhenSocketStateHasNoExistingRow()
        {
            var generated = new TableFillRow
            {
                Category = TableFillCategory.Outlet,
                Name = "Generated outlet",
                Quantity = "",
                Code = "8.3",
                SortOrder = 800,
                CatalogMatched = true
            };

            List<TableFillRow> result = UpdateOutletPolicy.PreserveExistingOrAdd(
                PlannedWithOutlet("planned"), true, generated, new TableFillRow[0]);

            TableFillRow outlet = Assert.Single(result, UpdateOutletPolicy.IsOutlet);
            Assert.Equal("1", outlet.Quantity);
            Assert.Equal("8.3", outlet.Code);
        }

        [Fact]
        public void PreserveExistingOrAdd_RemovesOutletWhenDeviceStateIsEquipment()
        {
            var existing = new TableFillRow
            {
                Category = TableFillCategory.Manual,
                Name = "Existing outlet",
                Quantity = "4",
                Code = "8.2"
            };
            var panel = new TableFillRow
            {
                Category = TableFillCategory.OutletPanel,
                Name = "Socket panel",
                Quantity = "1",
                Code = "4.11"
            };

            List<TableFillRow> result = UpdateOutletPolicy.PreserveExistingOrAdd(
                new[] { panel, existing }, false, null, new[] { existing });

            Assert.DoesNotContain(result, UpdateOutletPolicy.IsOutlet);
            Assert.Contains(result, UpdateOutletPolicy.IsOutletPanel);
        }

        [Fact]
        public void PreserveExistingOrAdd_PreservesBlankExistingQuantityInsteadOfInventingCount()
        {
            var existing = new TableFillRow
            {
                Category = TableFillCategory.Manual,
                Name = "Existing outlet",
                Quantity = "",
                Code = "8.2"
            };

            List<TableFillRow> result = UpdateOutletPolicy.PreserveExistingOrAdd(
                PlannedWithOutlet("planned"), true,
                new TableFillRow { Category = TableFillCategory.Outlet, Quantity = "1", Code = "8.3" },
                new[] { existing });

            Assert.Equal("", Assert.Single(result, UpdateOutletPolicy.IsOutlet).Quantity);
        }

        private static List<TableFillRow> PlannedWithOutlet(string outletName)
            => new List<TableFillRow>
            {
                new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆" },
                new TableFillRow { Category = TableFillCategory.Outlet, Name = outletName, Code = "8.1" },
                new TableFillRow { Category = TableFillCategory.FlexibleConduit, Name = "软管" }
            };
    }
}
