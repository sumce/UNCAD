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
        public void ManualItem_CanBeAddedSelectedAndRemovedWithoutDeletingPlannedRows()
        {
            FillReviewData data = FillReviewData.Create(new MachineRow(), Rows());
            FillReviewItem manual = data.AddCatalogItem(new ListItem
            {
                Code = " 8.9 ",
                Name = " 插座 ",
                Feature = " 五孔 ",
                Unit = " 个 "
            }, " 2 ");

            Assert.Equal(TableFillCategory.Manual, manual.Category);
            Assert.Equal("手动添加", FillReviewData.CategoryName(manual.Category));
            Assert.True(manual.CatalogMatched);
            Assert.True(manual.Included);
            Assert.Equal("插座", manual.Name);
            Assert.Equal("2", manual.Quantity);
            Assert.Equal("8.9", manual.Code);
            Assert.Equal(manual.Name, data.SelectedRows().Last().Name);
            Assert.False(data.RemoveManualItem(data.Items[0]));
            Assert.True(data.RemoveManualItem(manual));
            Assert.DoesNotContain(manual, data.Items);
        }

        [Fact]
        public void CatalogIdentity_IsRequiredForManualAdditionAndReplacement()
        {
            FillReviewData data = FillReviewData.Create(new MachineRow(), new[]
            {
                new TableFillRow
                {
                    Category = TableFillCategory.Bridge, Name = "未匹配桥架",
                    Quantity = "4.5", CatalogMatched = false
                }
            });
            var replacement = new ListItem
            {
                Category = "桥架",
                Code = "2.6",
                Name = "桥架",
                Feature = "200*100固定清单特征",
                Unit = "m",
                Alias = "200*100"
            };

            Assert.Throws<System.ArgumentNullException>(() =>
                data.AddCatalogItem(null, "1"));
            Assert.Throws<System.ArgumentException>(() =>
                data.AddCatalogItem(new ListItem { Name = "无编码" }, "1"));

            FillReviewItem item = Assert.Single(data.Items);
            data.ReplaceWithCatalogItem(item, replacement);

            Assert.True(item.CatalogMatched);
            Assert.True(item.Included);
            Assert.Equal("2.6", item.Code);
            Assert.Equal("桥架", item.Name);
            Assert.Equal("200*100固定清单特征", item.Description);
            Assert.Equal("m", item.Unit);
            Assert.Equal("4.5", item.Quantity);
        }

        [Fact]
        public void CableEditors_UpdateModelDescriptionAndLength()
        {
            FillReviewData data = FillReviewData.Create(new MachineRow { Cable = "OLD" }, Rows());
            data.SetCableModel("ZB-YJVR-5*6");
            data.SetCableMeters("18.5");

            Assert.Equal("OLD", data.Machine.Cable);
            Assert.Equal("OLD", data.OriginalCableModel);
            Assert.Equal("ZB-YJVR-5*6", data.BoqCableModel);
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

        [Fact]
        public void Create_UnmatchedConduitModelsRequireExplicitPerModelSelection()
        {
            FillReviewData data = FillReviewData.Create(new MachineRow(), new[]
            {
                new TableFillRow
                {
                    Category = TableFillCategory.RigidConduit, Name = "⌀32线管",
                    Description = "1.名称:⌀32线管", CatalogMatched = false
                },
                new TableFillRow
                {
                    Category = TableFillCategory.FlexibleConduit, Name = "包塑金属软管",
                    Description = "1.名称:包塑金属软管\\P2.规格:32mm",
                    CatalogMatched = false
                },
                new TableFillRow
                {
                    Category = TableFillCategory.RigidConduit, Name = "25mm线管",
                    Code = "3.2", CatalogMatched = true
                }
            });

            FillReviewItem rigid32 = data.Items[0];
            FillReviewItem flexible32 = data.Items[1];
            Assert.False(rigid32.Included);
            Assert.False(flexible32.Included);
            Assert.True(rigid32.RequiresCatalogConfirmation);
            Assert.True(flexible32.RequiresCatalogConfirmation);
            Assert.True(data.Items[2].Included);

            rigid32.Included = true;
            Assert.Equal(new[] { "25mm线管" },
                data.SelectedRows().Select(row => row.Name));
        }

        [Fact]
        public void Create_RejectsUnmatchedConduitsDespiteLegacyConfiguration()
        {
            FillReviewData data = FillReviewData.Create(new MachineRow(), new[]
            {
                new TableFillRow
                {
                    Category = TableFillCategory.RigidConduit,
                    Name = "⌀32线管", CatalogMatched = false
                }
            }, FillPlanningOptions.Create(1.5, true));

            Assert.False(Assert.Single(data.Items).Included);
            Assert.Empty(data.SelectedRows());
        }

        [Fact]
        public void FlexibleConduitDiameterEditor_RematchesCatalogAndPreservesQuantity()
        {
            FillReviewData data = FillReviewData.Create(new MachineRow { Dia = "38" },
                new[]
                {
                    new TableFillRow
                    {
                        Category = TableFillCategory.FlexibleConduit,
                        Name = "旧软管", Description = "旧模板", Unit = "M",
                        Quantity = "2.25", Code = "3.7"
                    }
                });
            var catalog = new List<ListItem>
            {
                new ListItem
                {
                    Code = "3.6", Name = "25mm包塑金属软管",
                    Feature = "Excel中的25mm软管模板", Unit = "m", Spec = "25mm"
                }
            };

            FillReviewItem flexible = data.SetFlexibleConduitDiameter("25", catalog);

            Assert.Equal("25", data.Machine.Dia);
            Assert.Equal("3.6", flexible.Code);
            Assert.Equal("25mm包塑金属软管", flexible.Name);
            Assert.Equal("Excel中的25mm软管模板", flexible.Description);
            Assert.Equal("M", flexible.Unit);
            Assert.Equal("2.25", flexible.Quantity);
            Assert.True(flexible.CatalogMatched);
            Assert.True(flexible.Included);
        }

        [Fact]
        public void SelectedRows_ReordersContinuouslyAfterDeleteExcludeAndAdd()
        {
            FillReviewData data = FillReviewData.Create(new MachineRow(), Rows());
            data.RemoveItem(data.Items[0]);
            data.Items[0].Included = false;
            data.AddCatalogItem(new ListItem
            {
                Code = "8.9",
                Name = "插座",
                Feature = "五孔",
                Unit = "个"
            }, "2");

            List<TableFillRow> selected = data.SelectedRows();

            Assert.Equal(2, selected.Count);
            Assert.Equal(TableFillCategory.Outlet, selected[0].Category);
            Assert.Equal(TableFillCategory.Manual, selected[1].Category);
            Assert.Equal(new[] { 1, 2 }, selected.ConvertAll(row => row.SortOrder));
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
            new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "5", Code = "1.1", CatalogMatched = true },
            new TableFillRow { Category = TableFillCategory.Bridge, Name = "桥架", Quantity = "2", Code = "2.1", CatalogMatched = true },
            new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座", Quantity = "1", Code = "8.1", CatalogMatched = true }
        };
    }
}
