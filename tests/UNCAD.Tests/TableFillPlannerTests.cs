using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public class TableFillPlannerTests
    {
        private static List<ListItem> Items() => new List<ListItem>
        {
            Item("1.1", "多芯电缆 XLPE", "电缆第一行\n电缆第二行", "m", "3*2.5"),
            Item("2.2", "梯式桥架", "桥架描述", "m", "200*100"),
            Item("3.1", "热镀锌穿线管", "线管描述", "m", "20mm"),
            Item("3.8", "包塑金属软管(波纹管)", "软管描述", "m", "20mm"),
            Item("5.8", "母线插接箱", "SQ-D PLUG-IN 250A 母线插接开关箱", "个", "250A"),
            Item("6.5", "断路器", "断路器 3P 225A~250A(I-LINE)", "个", "3P225~250A"),
            Item("8.2", "插座", "10~16A插座描述", "个", "10~16A"),
            Item("8.3", "插座", "20~30A插座描述", "个", "20~30A")
        };

        [Fact]
        public void Build_OrdersOnlyActualItems_AndCarriesAllColumns()
        {
            var machine = new MachineRow
            {
                MachineId = "M1",
                CircuitName = "RCM 插座*2",
                Cable = "ZB-YJVR-3*2.5",
                Detail = "U220 1P3W 1P20A",
                Dia = "20",
                Next = "插座盘"
            };
            var stat = StatCalculator.Calculate(new[]
            {
                "2000mm", "2500mm",
                "桥架200*100 10格",
                "⌀20线管 2000mm", "⌀20线管 2500mm"
            }, 250.0);

            List<TableFillRow> rows = TableFillPlanner.Build(machine, Items(), stat);

            Assert.Equal(new[]
            {
                TableFillCategory.Cable, TableFillCategory.Bridge,
                TableFillCategory.RigidConduit, TableFillCategory.FlexibleConduit,
                TableFillCategory.Outlet
            }, rows.Select(r => r.Category));
            Assert.Equal(new[] { "1.1", "2.2", "3.1", "3.8", "8.3" },
                rows.Select(r => r.Code));
            Assert.Equal("4.5", rows[0].Quantity);
            Assert.Equal("2.5", rows[1].Quantity);
            Assert.Equal("4.5", rows[2].Quantity);
            Assert.Equal("1.5", rows[3].Quantity);
            Assert.Equal("1", rows[4].Quantity);
            Assert.Equal("M", rows[0].Unit);
            Assert.Contains("\\P", rows[0].Description);
        }

        [Fact]
        public void Build_RigidConduit32_Uses38TemplateWhenCatalogHasNo32()
        {
            var items = new List<ListItem>
            {
                Item("3.3", "镀锌穿线管", "清单中的38mm线管模板", "m", "38mm"),
                Item("3.7", "包塑金属软管", "不能误选软管", "m", "38mm")
            };
            CableStatResult stat = StatCalculator.Calculate(
                new[] { "⌀32线管 3000mm" }, 250.0);

            TableFillRow conduit = Assert.Single(TableFillPlanner.Build(
                new MachineRow(), items, stat));

            Assert.Equal(TableFillCategory.RigidConduit, conduit.Category);
            Assert.Equal("3.3", conduit.Code);
            Assert.Equal("镀锌穿线管", conduit.Name);
            Assert.Equal("清单中的38mm线管模板", conduit.Description);
            Assert.Equal("3", conduit.Quantity);
        }

        [Fact]
        public void Build_RigidConduit32_PrefersExact32TemplateOver38Compatibility()
        {
            var items = new List<ListItem>
            {
                Item("3.3", "镀锌穿线管", "38mm模板", "m", "38mm"),
                Item("3.32", "电线管", "32mm精确模板", "m", "32mm")
            };
            CableStatResult stat = StatCalculator.Calculate(
                new[] { "Φ32 线管 2000MM" }, 250.0);

            TableFillRow conduit = Assert.Single(TableFillPlanner.Build(
                new MachineRow(), items, stat));

            Assert.Equal("3.32", conduit.Code);
            Assert.Equal("32mm精确模板", conduit.Description);
        }

        [Theory]
        [InlineData("-", "电脑插座", "U220 1P3W 1P20A", null)]
        [InlineData("插座盘", "普通设备", "N220 1P3W 1P16A", "8.2")]
        [InlineData("插座盘", "普通设备", "U220 1P3W 1P20A", "8.3")]
        public void Build_AddsOutletOnlyForNextOutletPanel_AndMatchesCurrent(
            string next, string circuit, string detail, string expectedCode)
        {
            var machine = new MachineRow
            {
                Next = next,
                CircuitName = circuit,
                Detail = detail
            };
            List<TableFillRow> rows = TableFillPlanner.Build(
                machine, Items(), new CableStatResult());
            TableFillRow outlet = rows.SingleOrDefault(r => r.Category == TableFillCategory.Outlet);

            Assert.Equal(expectedCode, outlet?.Code);
            if (outlet != null) Assert.Equal("1", outlet.Quantity);
        }

        [Fact]
        public void Build_AddsMatchingBreakerForNextILinePanel()
        {
            var machine = new MachineRow
            {
                Next = "I-Line盘",
                Detail = "N480 3P4W 3P250A"
            };

            TableFillRow breaker = Assert.Single(TableFillPlanner.Build(
                machine, Items(), new CableStatResult()));
            Assert.Equal(TableFillCategory.Breaker, breaker.Category);
            Assert.Equal("6.5", breaker.Code);
            Assert.Equal("1", breaker.Quantity);
        }

        [Fact]
        public void Build_AddsMatchingBusPlugBoxAndModelForNextBusInterface()
        {
            var machine = new MachineRow
            {
                Next = "母线插接口",
                Detail = "N480 3P4W 3P250A"
            };

            TableFillRow busPlug = Assert.Single(TableFillPlanner.Build(
                machine, Items(), new CableStatResult()));
            Assert.Equal(TableFillCategory.BusPlugBox, busPlug.Category);
            Assert.Equal("5.8", busPlug.Code);
            Assert.Equal("母线插接箱", busPlug.Name);
            Assert.Contains("250A", busPlug.Description);
            Assert.Equal("1", busPlug.Quantity);
        }

        [Fact]
        public void Build_DoesNotReserveRowsForAbsentCategories()
        {
            var machine = new MachineRow { CircuitName = "普通设备" };
            Assert.Empty(TableFillPlanner.Build(machine, Items(), new CableStatResult()));
        }

        private static ListItem Item(string code, string name, string feature, string unit, string spec)
        {
            return new ListItem
            {
                Code = code,
                Name = name,
                Feature = feature,
                Unit = unit,
                Spec = spec
            };
        }
    }
}
