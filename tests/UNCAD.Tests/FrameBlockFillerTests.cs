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
            var v = FrameBlockFiller.BuildValues(Row(), "");
            Assert.Equal("MDAPT01-泵1", v[FrameBlockFiller.TagDevice]);
            Assert.Equal("MDAPT01-POWER", v[FrameBlockFiller.TagPower]);
            Assert.Equal("ZB-YJV-3*70+1*35", v[FrameBlockFiller.TagCable]);
            Assert.Equal("", v[FrameBlockFiller.TagBridge]);
            Assert.Equal("", v[FrameBlockFiller.TagConduit]);
        }

        [Fact]
        public void BuildValues_IncludesBridgeTag_WhenBridgeInfoProvided()
        {
            var v = FrameBlockFiller.BuildValues(Row(), "桥架400*100 10格");
            Assert.Equal("桥架400*100 10格", v[FrameBlockFiller.TagBridge]);
        }

        [Fact]
        public void BuildValues_ClearsEmptyCable()
        {
            var row = Row();
            row.Cable = "  ";
            var v = FrameBlockFiller.BuildValues(row, "");
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
            var v = FrameBlockFiller.BuildValues(Row(), "", stat);

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
            var v = FrameBlockFiller.BuildValues(Row(), "", stat);

            Assert.Equal("ZB-YJV-3*70+1*35mm²: 2+3+3+4+4.4=16.4M",
                v[FrameBlockFiller.TagCable]);
        }

        [Fact]
        public void BuildValues_WritesBridgeSpecAndTotalLength()
        {
            var stat = StatCalculator.Calculate(new[]
            {
                "桥架200*100 10格",
                "桥架200*100 2格",
                "桥架400*100 4格"
            }, 250.0);
            var v = FrameBlockFiller.BuildValues(Row(), "手工桥架信息", stat);

            Assert.Equal("桥架200*100 3M; 桥架400*100 1M",
                v[FrameBlockFiller.TagBridge]);
        }
    }
}
