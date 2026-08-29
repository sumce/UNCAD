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

        private static List<TableFillRow> PlannedWithOutlet(string outletName)
            => new List<TableFillRow>
            {
                new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆" },
                new TableFillRow { Category = TableFillCategory.Outlet, Name = outletName, Code = "8.1" },
                new TableFillRow { Category = TableFillCategory.FlexibleConduit, Name = "软管" }
            };
    }
}
