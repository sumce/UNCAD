using System.Collections.Generic;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public class FrameBlockFillerTests
    {
        private static MachineRow Row() => new MachineRow
        {
            MachineId = "MDAPT01",
            CircuitName = "泵1",
            Cable = "ZB-YJV-3*70+1*35",
            Detail = "N208 3P4W 3P50A",
            Dia = "51"
        };

        [Fact]
        public void DeviceText_IsMachineDashCircuit()
        {
            Assert.Equal("MDAPT01-泵1", FrameBlockFiller.DeviceText(Row()));
        }

        [Fact]
        public void DeviceText_DoesNotCreateDanglingSeparator()
        {
            var row = Row();
            row.MachineId = "";
            Assert.Equal("泵1", FrameBlockFiller.DeviceText(row));
            row.CircuitName = "";
            Assert.Equal("", FrameBlockFiller.DeviceText(row));
        }

        [Theory]
        [InlineData("CABLE_INFO")]
        [InlineData("cable_info")]
        [InlineData("Conduit_Info")]
        public void KnownAttributeTags_AreCaseInsensitive(string tag)
        {
            Assert.True(FrameBlockFiller.IsKnownTag(tag));
        }

        [Fact]
        public void PowerText_IsMachineIdDashPower()
        {
            Assert.Equal("MDAPT01-POWER", FrameBlockFiller.PowerText(Row()));
        }

        [Fact]
        public void PowerText_IsEmpty_WhenMachineIdMissing()
        {
            var row = Row();
            row.MachineId = "";
            Assert.Equal("", FrameBlockFiller.PowerText(row));
        }

        [Fact]
        public void BuildValues_MapsExcelTags_WithoutStatistics()
        {
            var v = FrameBlockFiller.BuildValues(Row());
            Assert.Equal("MDAPT01-泵1", v[FrameBlockFiller.TagDevice]);
            Assert.Equal("MDAPT01-POWER", v[FrameBlockFiller.TagPower]);
            Assert.Equal("电缆ZB-YJV-3*70+1*35", v[FrameBlockFiller.TagCable]);
            Assert.Equal("", v[FrameBlockFiller.TagConduit]);
        }

        [Fact]
        public void BuildValues_NoBridgeTag_AfterXframeV2Removal()
        {
            // BRIDGE_FRAME_INFO 已随 xframe_v2 移除（D-035/D-033 之后的图框
            // 不携带该属性）；桥架数据只进 BOQ 表与 DATA 工作表。
            Assert.False(FrameBlockFiller.IsKnownTag("BRIDGE_FRAME_INFO"));
        }

        [Fact]
        public void BuildValues_ClearsEmptyCable()
        {
            var row = Row();
            row.Cable = "  ";
            var v = FrameBlockFiller.BuildValues(row);
            Assert.Equal("", v[FrameBlockFiller.TagCable]);
        }

        [Fact]
        public void BuildValues_WritesConduitTotals_WithChineseComma()
        {
            var stat = StatCalculator.Calculate(new[]
            {
                "⌀20线管 2000mm",
                "⌀20线管 2500mm",
                "⌀25线管 3000mm"
            }, 250.0);
            var v = FrameBlockFiller.BuildValues(Row(), stat);

            Assert.Equal("⌀20线管 4.5M，⌀25线管 3M",
                v[FrameBlockFiller.TagConduit]);
        }

        [Fact]
        public void BuildValues_AppendsCableLengthFormula()
        {
            var stat = StatCalculator.Calculate(new[]
            {
                "2000mm", "3000mm", "3000mm", "4000mm", "4400mm"
            }, 250.0);
            var v = FrameBlockFiller.BuildValues(Row(), stat);

            Assert.Equal("电缆ZB-YJV-3*70+1*35mm²: 2000+3000+3000+4000+4400mm",
                v[FrameBlockFiller.TagCable]);
        }

        [Fact]
        public void BuildValues_AppendsReserveLabelsInMillimeters()
        {
            var stat = StatCalculator.Calculate(new[]
            {
                "2000mm", "3000mm"
            }, 250.0);
            CableReservePolicy.Apply(stat, "I-Line盘", 500d);
            var v = FrameBlockFiller.BuildValues(Row(), stat);

            Assert.Equal(
                "电缆ZB-YJV-3*70+1*35mm²: 2000+3000+3400（盘内）+500（设备内）"
                + "+3000（预留）=11900mm",
                v[FrameBlockFiller.TagCable]);
        }

        [Fact]
        public void BuildValues_SingleMeasuredCableUsesMillimeters()
        {
            var stat = StatCalculator.Calculate(new[] { "2000mm" }, 250.0);
            CableReservePolicy.Apply(stat, "插座盘", 500d);
            var v = FrameBlockFiller.BuildValues(Row(), stat);

            Assert.Equal(
                "电缆ZB-YJV-3*70+1*35mm²: 2000+1000（盘内）+500（设备内）"
                + "+3000（预留）=6500mm",
                v[FrameBlockFiller.TagCable]);
        }

        [Fact]
        public void BuildValues_OmitsDeviceReserveWhenNotReserved()
        {
            var stat = StatCalculator.Calculate(new[] { "2000mm" }, 250.0);
            CableReservePolicy.Apply(stat, "I-Line盘", 0d);
            var v = FrameBlockFiller.BuildValues(Row(), stat);

            Assert.Equal(
                "电缆ZB-YJV-3*70+1*35mm²: 2000+3400（盘内）+3000（预留）=8400mm",
                v[FrameBlockFiller.TagCable]);
        }

        [Theory]
        // 工作簿只给芯数规格，图框负责补出工程前缀。
        [InlineData("3*70+1*35", "ZB-YJV-3*70+1*35")]
        [InlineData("3*2.5", "ZB-YJVR-3*2.5")]
        [InlineData("1*16", "1*16")]                      // 接地线不补前缀
        public void BuildValues_PrependsCableTypePrefix(string workbookCable, string expected)
        {
            var row = Row();
            row.Cable = workbookCable;
            var stat = StatCalculator.Calculate(new[] { "2000mm" }, 250.0);
            var v = FrameBlockFiller.BuildValues(row, stat);

            Assert.Equal("电缆" + expected + "mm²: 2000mm", v[FrameBlockFiller.TagCable]);
        }

        [Fact]
        public void BuildValues_PrependsCableTypePrefixWithoutStatistics()
        {
            // 没有统计文字时图框只写型号，同样要带前缀，保持前后一致。
            var row = Row();
            row.Cable = "3*2.5";
            var v = FrameBlockFiller.BuildValues(row, new CableStatResult());

            Assert.Equal("电缆ZB-YJVR-3*2.5", v[FrameBlockFiller.TagCable]);
        }




        [Fact]
        public void BuildValues_UpdateModeOmitsStatisticsTagsWithoutFreshMeasurements()
        {
            var empty = new CableStatResult();

            Dictionary<string, string> values = FrameBlockFiller.BuildValues(
                Row(), empty, true);

            Assert.False(values.ContainsKey(FrameBlockFiller.TagCable));
            Assert.False(values.ContainsKey(FrameBlockFiller.TagConduit));
            Assert.Equal("MDAPT01-泵1", values[FrameBlockFiller.TagDevice]);
        }
    }
}
