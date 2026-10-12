using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// Downstream 块（精确名 Downstream，三态与 upstream 相同）的识别与清单规则。
    /// 用户确认的规则：只有 socket box 产生物料；不加 8.x 插座行；永不加断路器。
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
        [InlineData("socket box", true, true)]
        [InlineData("Socket  Box", true, true)]
        [InlineData("I-line_Panel", false, true)]
        [InlineData("busbar connector", false, true)]
        [InlineData("设备", false, false)]
        [InlineData("", false, false)]
        public void TryClassifyDownstreamState_KnowsTheThreeUpstreamStates(
            string state, bool expectedSocketBox, bool expectedKnown)
        {
            bool known = DynamicBlockStatePolicy.TryClassifyDownstreamState(state,
                out bool socketBox);
            Assert.Equal(expectedKnown, known);
            Assert.Equal(expectedSocketBox, socketBox);
        }

        [Fact]
        public void ApplySocketBoxPanel_AddsExactlyOneOutletPanelRow()
        {
            List<TableFillRow> result = DownstreamStatePolicy.ApplySocketBoxPanel(BaseRows(),
                Machine(), Catalog(), AllAutoFill(), true);

            TableFillRow panel = Assert.Single(result,
                row => row.Category == TableFillCategory.OutletPanel);
            Assert.Equal("4.11", panel.Code);
            Assert.Equal("插座盘", panel.Name);
            Assert.Equal(Enumerable.Range(1, result.Count),
                result.Select(row => row.SortOrder));
        }

        [Fact]
        public void ApplySocketBoxPanel_NeverAddsABreaker()
        {
            // 断路器只由上游 U_上游类型=I-Line盘 产生；Downstream 即使状态异常也不许补断路器。
            List<TableFillRow> result = DownstreamStatePolicy.ApplySocketBoxPanel(BaseRows(),
                Machine(), Catalog(), AllAutoFill(), true);

            Assert.DoesNotContain(result, row => row.Category == TableFillCategory.Breaker);
            Assert.DoesNotContain(result, row => row.Category == TableFillCategory.Outlet
                && row.Code != "8.2");
        }

        [Fact]
        public void ApplySocketBoxPanel_DoesNothingWhenTheStateIsNotSocketBox()
        {
            List<TableFillRow> result = DownstreamStatePolicy.ApplySocketBoxPanel(BaseRows(),
                Machine(), Catalog(), AllAutoFill(), false);

            Assert.DoesNotContain(result, row => row.Category == TableFillCategory.OutletPanel);
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void ApplySocketBoxPanel_RespectsTheOutletPanelSwitch()
        {
            // 默认设置里"插座盘"是关闭的：关掉时必须一行都不加。
            var outletPanelOff = FillAutoFillOptions.Create(true, true, true, true, true,
                true, false, true);

            List<TableFillRow> result = DownstreamStatePolicy.ApplySocketBoxPanel(BaseRows(),
                Machine(), Catalog(), outletPanelOff, true);

            Assert.DoesNotContain(result, row => row.Category == TableFillCategory.OutletPanel);
        }

        private static List<TableFillRow> BaseRows() => new List<TableFillRow>
        {
            new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", SortOrder = 100 },
            new TableFillRow { Category = TableFillCategory.BusPlugBox, Name = "母线插接箱",
                Code = "5.1", SortOrder = 500 },
            new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座",
                Code = "8.2", SortOrder = 800 }
        };

        private static MachineRow Machine() => new MachineRow { Detail = "U220 1P3W 1P20A" };

        private static BoqCatalogIndex Catalog() => new BoqCatalogIndex(new List<ListItem>
        {
            new ListItem
            {
                Category = "插座盘",
                Code = "4.11",
                Name = "插座盘",
                Feature = "1.名称:插座盘\\P2.规格:100A+20A*40",
                Unit = "个",
                Spec = "100A+20A*40"
            }
        });

        private static FillAutoFillOptions AllAutoFill()
            => FillAutoFillOptions.Create(true, true, true, true, true, true, true, true);
    }
}
