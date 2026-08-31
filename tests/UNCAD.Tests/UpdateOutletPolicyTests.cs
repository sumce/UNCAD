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
                Code = "8.9",
                CatalogMatched = false
            };

            List<TableFillRow> result = UpdateOutletPolicy.PreserveExisting(
                PlannedWithOutlet("机台Excel插座"), new[] { existing });

            TableFillRow outlet = Assert.Single(result, UpdateOutletPolicy.IsOutlet);
            Assert.Equal("用户保留插座", outlet.Name);
            Assert.Equal("CAD中的原描述", outlet.Description);
            Assert.Equal("套", outlet.Unit);
            Assert.Equal("2", outlet.Quantity);
            Assert.Equal("8.9", outlet.Code);
            Assert.True(outlet.CatalogMatched);
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
                Code = "8.9",
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
            Assert.Equal("8.9", outlet.Code);
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
