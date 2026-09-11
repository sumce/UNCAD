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
        public void Build_ResolvesBridgeBoqModelFromCatalogFeature()
        {
            var items = Items();
            items[1].Feature = "1.名称:梯形桥架200Wx100H\n2.材质:铝合金粉体烤漆";
            var machine = new MachineRow { MachineId = "M1", CircuitName = "X" };
            var stat = StatCalculator.Calculate(new[] { "桥架200*100 10格" }, 250.0);

            TableFillPlanner.Build(machine, items, stat);

            // 图框随后据此显示 BOQ 型号而不是图上量的规格写法。
            Assert.Equal("梯形桥架200Wx100H", stat.Bridges[0].CatalogModel);
        }

        [Fact]
        public void Build_ThrowsWhenBridgeSpecIsNotInCatalog()
        {
            // D-016：未匹配的桥架没有清单编码，必须在 CAD 事务之前失败，
            // 而不是写出一行无编码的清单。
            var machine = new MachineRow { MachineId = "M1", CircuitName = "X" };
            var stat = StatCalculator.Calculate(new[] { "桥架250*80 10格" }, 250.0);

            Assert.Throws<System.IO.InvalidDataException>(
                () => TableFillPlanner.Build(machine, Items(), stat));
        }

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
        public void Build_RigidConduit32_DoesNotUse38TemplateWhenCatalogHasNo32()
        {
            var items = new List<ListItem>
            {
                Item("3.3", "镀锌穿线管", "清单中的38mm线管模板", "m", "38mm"),
                Item("3.7", "包塑金属软管", "不能误选软管", "m", "38mm")
            };
            CableStatResult stat = StatCalculator.Calculate(
                new[] { "⌀32线管 3000mm" }, 250.0);

            TableFillRow conduit = TableFillPlanner.Build(
                new MachineRow(), items, stat).Single(row =>
                    row.Category == TableFillCategory.RigidConduit);

            Assert.Equal(TableFillCategory.RigidConduit, conduit.Category);
            Assert.Equal("", conduit.Code);
            Assert.Equal("⌀32线管", conduit.Name);
            Assert.Equal("1.名称:⌀32线管", conduit.Description);
            Assert.Equal("3", conduit.Quantity);
            Assert.False(conduit.CatalogMatched);
        }

        [Fact]
        public void Build_RigidConduit32_UsesExact32TemplateOnly()
        {
            var items = new List<ListItem>
            {
                Item("3.3", "镀锌穿线管", "38mm模板", "m", "38mm"),
                Item("3.32", "电线管", "32mm精确模板", "m", "32mm")
            };
            CableStatResult stat = StatCalculator.Calculate(
                new[] { "Φ32 线管 2000MM" }, 250.0);

            TableFillRow conduit = TableFillPlanner.Build(
                new MachineRow(), items, stat).Single(row =>
                    row.Category == TableFillCategory.RigidConduit);

            Assert.Equal("3.32", conduit.Code);
            Assert.Equal("32mm精确模板", conduit.Description);
            Assert.True(conduit.CatalogMatched);
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

        [Theory]
        [InlineData("U220 1P3W 1P20A", "4.11")]
        [InlineData("N220 1P3W 1P16A", "4.12")]
        [InlineData("N220 1P3W 1P 16 A", "4.12")]
        public void Build_AddsExactSocketPanelForSupportedRating(string detail,
            string expectedCode)
        {
            var machine = new MachineRow { Next = "插座盘", Detail = detail };
            var catalog = new BoqCatalogIndex(new[]
            {
                Panel("4.11", "100A+20A*40"),
                Panel("4.12", "80A+16A*30"),
                // 4.14 also mentions 16A and must never win by a broad range match.
                Panel("4.14", "50A+16A*10"),
                Item("8.2", "插座", "10~16A插座描述", "个", "10~16A"),
                Item("8.3", "插座", "20~30A插座描述", "个", "20~30A")
            });

            List<TableFillRow> rows = TableFillPlanner.Build(
                machine, catalog, new CableStatResult(), FillPlanningOptions.Default,
                AllAutoFill());
            TableFillRow panel = Assert.Single(rows,
                row => row.Category == TableFillCategory.OutletPanel);

            Assert.Equal(expectedCode, panel.Code);
            Assert.Equal("1", panel.Quantity);
            Assert.True(panel.CatalogMatched);
            Assert.Equal(expectedCode == "4.12" ? "8.2" : "8.3",
                Assert.Single(rows, row => row.Category == TableFillCategory.Outlet).Code);
        }

        [Fact]
        public void Build_LeavesUnsupportedSocketPanelUnmatchedInsteadOfGuessing()
        {
            var machine = new MachineRow { Next = "插座盘", Detail = "N220 1P3W 1P10A" };
            var catalog = new BoqCatalogIndex(new[]
            {
                Panel("4.11", "100A+20A*40"),
                Panel("4.12", "80A+16A*30"),
                Panel("4.13", "50A+10A*10"),
                Panel("4.14", "50A+16A*10"),
                Item("8.2", "插座", "10~16A插座描述", "个", "10~16A")
            });

            TableFillRow panel = Assert.Single(TableFillPlanner.Build(
                machine, catalog, new CableStatResult(), FillPlanningOptions.Default,
                AllAutoFill()),
                row => row.Category == TableFillCategory.OutletPanel);

            Assert.Equal("", panel.Code);
            Assert.False(panel.CatalogMatched);
            Assert.DoesNotContain("4.11", panel.Code);
            Assert.DoesNotContain("4.12", panel.Code);
        }

        [Fact]
        public void Build_AddsMatchingBreakerForNextILinePanel()
        {
            var machine = new MachineRow
            {
                Next = "I-Line盘",
                Detail = "N480 3P4W 3P250A"
            };

            TableFillRow breaker = TableFillPlanner.Build(
                machine, new BoqCatalogIndex(Items()), new CableStatResult(),
                FillPlanningOptions.Default, AllAutoFill()).Single(row =>
                    row.Category == TableFillCategory.Breaker);
            Assert.Equal(TableFillCategory.Breaker, breaker.Category);
            Assert.Equal("6.5", breaker.Code);
            Assert.Equal("1", breaker.Quantity);
        }

        [Fact]
        public void Build_DefaultAutoFillKeepsBreakerAndSkipsOutletPanel()
        {
            List<ListItem> items = Items();
            items.Add(Panel("4.11", "100A+20A*40"));

            List<TableFillRow> socketRows = TableFillPlanner.Build(new MachineRow
            {
                Next = "插座盘",
                Detail = "U220 1P3W 1P20A"
            }, items, new CableStatResult());
            Assert.DoesNotContain(socketRows,
                row => row.Category == TableFillCategory.OutletPanel);
            Assert.Contains(socketRows,
                row => row.Category == TableFillCategory.Outlet);

            List<TableFillRow> breakerRows = TableFillPlanner.Build(new MachineRow
            {
                Next = "I-Line盘",
                Detail = "N480 3P4W 3P250A"
            }, items, new CableStatResult());
            Assert.Contains(breakerRows,
                row => row.Category == TableFillCategory.Breaker
                    && row.Code == "6.5");

            List<TableFillRow> allDisabledSocketRows = TableFillPlanner.Build(
                new MachineRow
                {
                    Next = "插座盘",
                    Detail = "U220 1P3W 1P20A"
                }, new BoqCatalogIndex(items), new CableStatResult(),
                FillPlanningOptions.Default,
                FillAutoFillOptions.Create(false, false, false, false, false,
                    false, false));
            Assert.Contains(allDisabledSocketRows,
                row => row.Category == TableFillCategory.Outlet);
        }

        [Fact]
        public void Build_DisabledMaterialCategoriesProduceNoAutomaticRows()
        {
            var machine = new MachineRow
            {
                Cable = "ZB-YJVR-3*2.5",
                Dia = "20"
            };
            CableStatResult stat = StatCalculator.Calculate(new[]
            {
                "2000mm",
                "桥架200*100 2500mm",
                "⌀20线管 2000mm"
            }, 250.0);

            List<TableFillRow> rows = TableFillPlanner.Build(machine,
                new BoqCatalogIndex(Items()), stat, FillPlanningOptions.Default,
                FillAutoFillOptions.Create(false, false, false, false, false,
                    false, false));

            Assert.Empty(rows);
        }

        [Fact]
        public void Build_AddsMatchingBusPlugBoxAndModelForNextBusInterface()
        {
            var machine = new MachineRow
            {
                Next = "母线插接口",
                Detail = "N480 3P4W 3P250A"
            };

            TableFillRow busPlug = TableFillPlanner.Build(
                machine, Items(), new CableStatResult()).Single(row =>
                    row.Category == TableFillCategory.BusPlugBox);
            Assert.Equal(TableFillCategory.BusPlugBox, busPlug.Category);
            Assert.Equal("5.8", busPlug.Code);
            Assert.Equal("母线插接箱", busPlug.Name);
            Assert.Contains("250A", busPlug.Description);
            Assert.Equal("1", busPlug.Quantity);
        }

        [Theory]
        [InlineData("350A")]
        [InlineData("360A")]
        public void Build_LeavesUnknownBusPlugBoxRatingForUserSelection(string rating)
        {
            var machine = new MachineRow
            {
                Next = "母线插接口",
                Detail = "N208 3P4W 3P" + rating
            };
            List<ListItem> items = Items();
            items.Add(Item("5.6", "母线插接箱",
                "SQ-D PLUG-IN 400A 母线插接开关箱", "个", "400A"));

            TableFillRow busPlug = TableFillPlanner.Build(
                machine, items, new CableStatResult()).Single(row =>
                    row.Category == TableFillCategory.BusPlugBox);

            Assert.Equal("", busPlug.Code);
            Assert.Contains(rating, busPlug.Description);
            Assert.False(busPlug.CatalogMatched);
            Assert.Equal("1", busPlug.Quantity);
        }

        [Fact]
        public void Build_DoesNotAddFlexibleConduitWithoutCableDerivedDiameter()
        {
            var machine = new MachineRow { CircuitName = "普通设备", Dia = "" };

            Assert.DoesNotContain(TableFillPlanner.Build(machine, Items(), new CableStatResult()),
                row => row.Category == TableFillCategory.FlexibleConduit);
        }

        [Fact]
        public void Build_DoesNotInferHoseDiameterFromRigidConduitStatistics()
        {
            var items = new List<ListItem>
            {
                Item("3.3", "镀锌穿线管", "刚性38模板", "m", "38mm"),
                Item("3.7", "包塑金属软管", "软管38模板", "m", "38mm")
            };
            CableStatResult stat = StatCalculator.Calculate(
                new[] { "⌀32线管 3000mm" }, 250.0);

            List<TableFillRow> rows = TableFillPlanner.Build(new MachineRow(), items, stat);

            Assert.DoesNotContain(rows, row => row.Category == TableFillCategory.FlexibleConduit);
        }

        [Fact]
        public void Build_Alias1DoesNotCrossMaterialCategories()
        {
            ListItem migratedTarget = Item("3.3", "镀锌穿线管",
                "固定清单38mm模板", "m", "38mm");
            migratedTarget.Alias1 = "32mm";
            var catalog = new BoqCatalogIndex(new[]
            {
                migratedTarget,
                Item("3.7", "包塑金属软管", "软管38mm模板", "m", "38mm")
            });

            TableFillRow flexible = TableFillPlanner.BuildFlexibleConduitRow(
                "32mm", catalog, FillPlanningOptions.Default);

            Assert.Equal("", flexible.Code);
        }

        [Fact]
        public void BuildFlexibleConduitRow_ConfiguredMetersFlowsToPlannedQuantity()
        {
            var catalog = new BoqCatalogIndex(new List<ListItem>
            {
                Item("3.6", "包塑金属软管", "25mm模板", "m", "25mm")
            });
            TableFillRow flexible = TableFillPlanner.BuildFlexibleConduitRow(
                "DN25", catalog, FillPlanningOptions.Create(2.25, false));

            Assert.Equal("3.6", flexible.Code);
            Assert.Equal("2.25", flexible.Quantity);
        }

        [Fact]
        public void Build_DoesNotUseMultipleRigidConduitDiametersForHose()
        {
            CableStatResult stat = StatCalculator.Calculate(new[]
            {
                "⌀20线管 2000mm", "⌀25线管 3000mm"
            }, 250.0);

            List<TableFillRow> rows = TableFillPlanner.Build(
                new MachineRow(), Items(), stat);

            Assert.DoesNotContain(rows, row => row.Category == TableFillCategory.FlexibleConduit);
        }

        private static ListItem Item(string code, string name, string feature, string unit, string spec)
        {
            return new ListItem
            {
                // 测试夹具显式模拟工作簿“类”列，产品代码不会再按编码推断。
                Category = TestCategory(code, name),
                Code = code,
                Name = name,
                Feature = feature,
                Unit = unit,
                Spec = spec
            };
        }

        private static ListItem Panel(string code, string spec)
        {
            return new ListItem
            {
                Category = "插座盘",
                Code = code,
                Name = "插座盘",
                Feature = "1.名称:插座盘\\P2.规格:" + spec,
                Unit = "个",
                Spec = spec
            };
        }

        private static FillAutoFillOptions AllAutoFill()
            => FillAutoFillOptions.Create(true, true, true, true, true, true, true);

        private static string TestCategory(string code, string name)
        {
            if (code.StartsWith("1.")) return "电缆";
            if (code.StartsWith("2.")) return "桥架";
            if (code.StartsWith("3.")) return name.Contains("软管") ? "软管" : "线管";
            if (code.StartsWith("5.")) return "母线插接箱";
            if (code.StartsWith("6.")) return "断路器";
            if (code.StartsWith("8.")) return "插座";
            if (code.StartsWith("4.9") || code.StartsWith("4.10")
                || code.StartsWith("4.11") || code.StartsWith("4.12")
                || code.StartsWith("4.13") || code.StartsWith("4.14")) return "插座盘";
            return "";
        }
    }
}
