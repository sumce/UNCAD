using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class FillReviewDataTests
    {
        [Fact]
        public void Create_DefaultsEveryMatchedItemToIncluded_AndClonesMachine()
        {
            var source = new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "OLD" };
            FillReviewData data = FillReviewData.Create(source, Rows());

            Assert.All(data.Items, item => Assert.True(item.Included));
            Assert.Equal(3, data.SelectedRows().Count);
            data.Machine.CircuitName = "已修改";
            Assert.Equal("设备A", source.CircuitName);
        }

        [Fact]
        public void SelectedRows_ExcludesUncheckedOutlet_AndPreservesOrder()
        {
            FillReviewData data = FillReviewData.Create(new MachineRow(), Rows());
            data.Items.Single(item => item.Category == TableFillCategory.Outlet).Included = false;

            Assert.Equal(new[] { TableFillCategory.Cable, TableFillCategory.Bridge },
                data.SelectedRows().Select(row => row.Category));
        }

        [Fact]
        public void CableEditors_UpdateModelDescriptionAndLength()
        {
            FillReviewData data = FillReviewData.Create(new MachineRow { Cable = "OLD" }, Rows());
            data.SetCableModel("ZB-YJVR-5*6");
            data.SetCableMeters("18.5");

            Assert.Equal("ZB-YJVR-5*6", data.Machine.Cable);
            Assert.Contains("ZB-YJVR-5*6", data.CableItem().Description);
            Assert.Equal("18.5", data.CableItem().Quantity);
        }

        [Fact]
        public void CableMeters_PersistsWithoutMatchedCableCatalogRow()
        {
            FillReviewData data = FillReviewData.Create(new MachineRow { Cable = "C1" },
                new[] { new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座" } });
            data.SetCableMeters("9.5");
            Assert.Equal("9.5", data.CableMeters);
            Assert.Null(data.CableItem());
        }

        [Theory]
        [InlineData(20, 11, 11)]
        [InlineData(7, 11, 7)]
        [InlineData(20, 0, 1)]
        public void ClearPolicy_BoundsConfiguredRange(int available, int configured, int expected)
        {
            Assert.Equal(expected, TableClearPolicy.ResolveRows(available, configured));
            Assert.True(TableClearPolicy.CanFit(expected, expected));
            Assert.False(TableClearPolicy.CanFit(expected + 1, expected));
        }

        private static List<TableFillRow> Rows() => new List<TableFillRow>
        {
            new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "5", Code = "1.1" },
            new TableFillRow { Category = TableFillCategory.Bridge, Name = "桥架", Quantity = "2", Code = "2.1" },
            new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座", Quantity = "1", Code = "8.1" }
        };
    }
}
