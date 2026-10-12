using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// Downstream 块（精确名 Downstream，三态与 upstream 相同）的识别与清单规则。
    ///
    /// 用户确认的规则：socket box → 额外一行 4.x 插座箱/盘（默认 20A）；
    /// I-line_Panel → 额外一行电盘（默认 1000A）；busbar connector 只上色；
    /// 不加 8.x 插座行；**永不加断路器**；两个额定电流都来自 U1SET，不从 U_配电信息 取。
    /// </summary>
    public sealed class DownstreamStatePolicyTests
    {
        [Theory]
        [InlineData("Downstream", true)]
        [InlineData("downstream", true)]
        [InlineData("Downstream$0$", true)]
        [InlineData("Downstream$0$$12$", true)]
        [InlineData("Downstream_GW", false)]      // 只认精确名，不做族匹配
        [InlineData("upstream", false)]
        [InlineData("DS", false)]
        [InlineData("", false)]
        public void IsDownstreamBlock_MatchesTheExactNameOnly(string name, bool expected)
            => Assert.Equal(expected, DynamicBlockStatePolicy.IsDownstreamBlock(name));

        [Theory]
        [InlineData("socket box", DownstreamState.SocketBox, true)]
        [InlineData("Socket  Box", DownstreamState.SocketBox, true)]
        [InlineData("I-line_Panel", DownstreamState.ILinePanel, true)]
        [InlineData("busbar connector", DownstreamState.BusbarConnector, true)]
        [InlineData("设备", DownstreamState.Unknown, false)]
        [InlineData("", DownstreamState.Unknown, false)]
        public void TryClassifyDownstreamState_KnowsTheThreeUpstreamStates(
            string state, DownstreamState expected, bool expectedKnown)
        {
            bool known = DynamicBlockStatePolicy.TryClassifyDownstreamState(state,
                out DownstreamState actual);
            Assert.Equal(expectedKnown, known);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SocketBox_AddsExactlyOneOutletPanelRow()
        {
            List<TableFillRow> result = DownstreamStatePolicy.Apply(BaseRows(), Catalog(),
                AllAutoFill(), DownstreamState.SocketBox, 20.0, 1000.0);

            TableFillRow panel = Assert.Single(result,
                row => row.Category == TableFillCategory.OutletPanel);
            Assert.Equal("4.11", panel.Code);
            Assert.True(panel.CatalogMatched);
            Assert.Equal(Enumerable.Range(1, result.Count),
                result.Select(row => row.SortOrder));
        }

        [Fact]
        public void ILinePanel_AddsExactlyOneElectricalPanelUsingTheConfiguredRating()
        {
            // 用户规则：Downstream=I-line_Panel → 自动加一个电盘（默认 1000A）。
            List<TableFillRow> result = DownstreamStatePolicy.Apply(BaseRows(), Catalog(),
                AllAutoFill(), DownstreamState.ILinePanel, 20.0, 1000.0);

            TableFillRow panel = Assert.Single(result,
                row => row.Category == TableFillCategory.Panel);
            Assert.Equal("电盘", panel.Name);
            // 夹具清单里没有 1000A 电盘 → 未匹配行，但额定电流必须按设置写进去。
            Assert.False(panel.CatalogMatched);
            Assert.Contains("1000A", panel.Description);
        }

        [Fact]
        public void ILinePanel_NeverAddsABreaker()
        {
            // 断路器只由上游 U_上游类型=I-Line盘 产生；Downstream 的 I-line 态不带断路器。
            List<TableFillRow> result = DownstreamStatePolicy.Apply(BaseRows(), Catalog(),
                AllAutoFill(), DownstreamState.ILinePanel, 20.0, 1000.0);

            Assert.DoesNotContain(result, row => row.Category == TableFillCategory.Breaker);
            Assert.DoesNotContain(result, row => row.Category == TableFillCategory.OutletPanel);
        }

        [Fact]
        public void SocketBox_NeverAddsABreaker()
        {
            List<TableFillRow> result = DownstreamStatePolicy.Apply(BaseRows(), Catalog(),
                AllAutoFill(), DownstreamState.SocketBox, 20.0, 1000.0);

            Assert.DoesNotContain(result, row => row.Category == TableFillCategory.Breaker);
            Assert.DoesNotContain(result, row => row.Category == TableFillCategory.Panel);
        }

        [Fact]
        public void BusbarConnector_AndUnknown_AddNoMaterialRow()
        {
            foreach (DownstreamState state in new[]
                { DownstreamState.BusbarConnector, DownstreamState.Unknown })
            {
                List<TableFillRow> result = DownstreamStatePolicy.Apply(BaseRows(), Catalog(),
                    AllAutoFill(), state, 20.0, 1000.0);
                Assert.Equal(3, result.Count);
                Assert.DoesNotContain(result, row => row.Category == TableFillCategory.Panel);
                Assert.DoesNotContain(result, row =>
                    row.Category == TableFillCategory.OutletPanel);
            }
        }

        [Fact]
        public void SocketBox_RespectsTheOutletPanelSwitch()
        {
            // 默认设置里"插座盘"是关闭的：关掉时必须一行都不加。
            List<TableFillRow> result = DownstreamStatePolicy.Apply(BaseRows(), Catalog(),
                FillAutoFillOptions.Create(true, true, true, true, true, true, false, true),
                DownstreamState.SocketBox, 20.0, 1000.0);

            Assert.DoesNotContain(result, row => row.Category == TableFillCategory.OutletPanel);
        }

        [Fact]
        public void ILinePanel_RespectsThePanelSwitch()
        {
            List<TableFillRow> result = DownstreamStatePolicy.Apply(BaseRows(), Catalog(),
                FillAutoFillOptions.Create(true, true, false, true, true, true, true, true),
                DownstreamState.ILinePanel, 20.0, 1000.0);

            Assert.DoesNotContain(result, row => row.Category == TableFillCategory.Panel);
        }

        [Fact]
        public void SocketBox_Default20AmpsMatchesTheRealEmbeddedCatalog()
        {
            // 现场问题：以前拿 U_配电信息 的电流（例如 100A）去匹配 4.x，匹配不到，
            // 整行变成 BOQ-CATALOG-MISSING。默认 20A 必须能在固定清单里匹配到。
            var catalog = new BoqCatalogIndex(ListItemReader.ReadEmbedded());

            TableFillRow matched = Assert.Single(DownstreamStatePolicy.Apply(BaseRows(),
                    catalog, AllAutoFill(), DownstreamState.SocketBox,
                    FillRuntimeOptions.DefaultDownstreamSocketAmps,
                    FillRuntimeOptions.DefaultDownstreamPanelAmps),
                row => row.Category == TableFillCategory.OutletPanel);
            Assert.True(matched.CatalogMatched, "默认 20A 必须能在固定清单里匹配到插座盘");
            Assert.False(string.IsNullOrEmpty(matched.Code));

            TableFillRow unmatched = Assert.Single(DownstreamStatePolicy.Apply(BaseRows(),
                    catalog, AllAutoFill(), DownstreamState.SocketBox, 999.0,
                    FillRuntimeOptions.DefaultDownstreamPanelAmps),
                row => row.Category == TableFillCategory.OutletPanel);
            Assert.False(unmatched.CatalogMatched);
            Assert.Equal("", unmatched.Code);
        }

        private static List<TableFillRow> BaseRows() => new List<TableFillRow>
        {
            new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", SortOrder = 100 },
            new TableFillRow { Category = TableFillCategory.BusPlugBox, Name = "母线插接箱",
                Code = "5.1", SortOrder = 500 },
            new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座",
                Code = "8.2", SortOrder = 800 }
        };

        private static BoqCatalogIndex Catalog() => new BoqCatalogIndex(new List<ListItem>
        {
            new ListItem
            {
                Category = "插座盘", Code = "4.11", Name = "插座盘",
                Feature = "1.名称:插座盘\\P2.规格:100A+20A*40", Unit = "个",
                Spec = "100A+20A*40"
            },
            new ListItem
            {
                // 电盘（I-LINE）条目：范围 60~100A，用来验证 1000A 的配置值落到未匹配行。
                Category = "电盘", Code = "6.7", Name = "电盘",
                Feature = "1.名称:电盘PANEL-100A(I-LINE)", Unit = "个",
                Spec = "3P 60A~100A(I-LINE)"
            }
        });

        private static FillAutoFillOptions AllAutoFill()
            => FillAutoFillOptions.Create(true, true, true, true, true, true, true, true);
    }
}
