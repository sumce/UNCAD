using System.Collections.Generic;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public class TableGenerationModuleTests
    {
        [Fact]
        public void Plan_ReturnsNamedBoqContractAndDefaultCableQuantity()
        {
            var machine = new MachineRow
            {
                MachineId = "M01",
                CircuitName = "设备A",
                Cable = "ZB-YJVR-3*2.5",
                Dia = "25"
            };
            var catalog = new BoqCatalogIndex(new List<ListItem>
            {
                Item("1.1", "电缆", "ZB-YJVR-3*2.5"),
                Item("3.5", "包塑金属软管", "25mm")
            });
            var statistics = new CableStatResult { CableSum = 12.5 };

            TableGenerationOutput output = TableGenerationModule.Plan(
                new TableGenerationRequest(machine, catalog, statistics,
                    FillPlanningOptions.Create(2.25, false)));

            Assert.Equal("BOQ-TABLE/清单表格", TableGenerationModule.Descriptor.Label);
            Assert.Equal("12.5", output.DefaultCableMeters);
            Assert.Equal(2, output.RowCount);
            FillReviewData review = output.CreateReview(machine,
                FillPlanningOptions.Create(2.25, false));
            Assert.Equal("12.5", review.CableMeters);
            Assert.Contains(review.Items, row => row.Quantity == "2.25");
        }

        [Fact]
        public void Plan_ClonesInputAndReturnsDefensiveRowCopies()
        {
            var machine = new MachineRow { Cable = "3*2.5" };
            var statistics = new CableStatResult { CableSum = 1 };
            var request = new TableGenerationRequest(machine,
                new BoqCatalogIndex(new[] { Item("1.1", "电缆", "3*2.5") }),
                statistics, FillPlanningOptions.Default);
            machine.Cable = "5*6";
            statistics.CableSum = 9;
            TableGenerationOutput output = TableGenerationModule.Plan(request);
            List<TableFillRow> first = output.CopyDefaultRows();
            first[0].Name = "被调用方修改";

            Assert.NotEqual("被调用方修改", output.CopyDefaultRows()[0].Name);
            Assert.Contains("3*2.5", output.CopyDefaultRows()[0].Description);
            Assert.Equal("1", output.DefaultCableMeters);
        }

        private static ListItem Item(string code, string name, string spec)
            => new ListItem
            {
                Category = name.Contains("软管") ? "软管" : "电缆",
                Code = code,
                Name = name,
                Feature = name + spec,
                Unit = "m",
                Spec = spec
            };
    }
}
