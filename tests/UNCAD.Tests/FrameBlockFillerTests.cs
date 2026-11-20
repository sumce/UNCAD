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
        [InlineData("info2")]
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
            Assert.Equal("ZB-YJV-3*70+1*35", v[FrameBlockFiller.TagCable]);
            Assert.Equal("", v[FrameBlockFiller.TagConduit]);
        }

        [Fact]
        public void BuildValues_NoBridgeTag_AfterXframeV2Removal()
        {
            // Bridge summaries are now carried by INFO2; the legacy tag stays retired.
            Assert.False(FrameBlockFiller.IsKnownTag("BRIDGE_FRAME_INFO"));
        }

        [Fact]
        public void BuildValues_Info2ShowsOnlyBridgeAndRigidConduitInMillimetres()
        {
            CableStatResult stat = StatCalculator.Calculate(new[]
            {
                "桥架200*100 10格",
                "桥架200*100 20格",
                "⌀20线管 2000mm",
                "⌀20线管 2500mm"
            }, 250);
            stat.Bridges[0].CatalogModel = "梯形桥架200Wx100H";
            stat.Conduits[0].CatalogModel =
                "镀锌穿线管EMT PIPE 20mm(3/4\")";

            Dictionary<string, string> values = FrameBlockFiller.BuildValues(Row(), stat);

            Assert.Equal(
                "梯形桥架200Wx100H 2500 +5000 = 7500mm；"
                + "镀锌穿线管20mm 2000 +2500 = 4500mm",
                values[FrameBlockFiller.TagInfo2]);
        }

        [Fact]
        public void BuildValues_Info2UsesShortConduitModelAndMillimetreLength()
        {
            CableStatResult stat = StatCalculator.Calculate(
                new[] { "⌀38线管 4000mm" }, 250);
            stat.Conduits[0].CatalogModel =
                "镀锌穿线管EMT PIPE 38mm(1-1/2\")";

            Dictionary<string, string> values = FrameBlockFiller.BuildValues(Row(), stat);

            Assert.Equal("镀锌穿线管38mm 4000mm",
                values[FrameBlockFiller.TagInfo2]);
        }

        [Fact]
        public void BuildValues_Info2ShowsSingleSegmentsInMillimetresWithoutRedundantTotal()
        {
            CableStatResult stat = StatCalculator.Calculate(new[]
            {
                "桥架200*100 16格",
                "⌀20线管 4000mm"
            }, 250);
            stat.Bridges[0].CatalogModel = "梯形桥架200Wx100H";
            stat.Conduits[0].CatalogModel =
                "镀锌穿线管EMT PIPE 20mm(3/4\")";

            Dictionary<string, string> values = FrameBlockFiller.BuildValues(
                Row(), stat);

            Assert.Equal(
                "梯形桥架200Wx100H 4000mm；镀锌穿线管20mm 4000mm",
                values[FrameBlockFiller.TagInfo2]);
        }

        [Fact]
        public void BuildValues_Info2OmitsItemsWithoutCatalogModels()
        {
            CableStatResult stat = StatCalculator.Calculate(new[]
            {
                "桥架200*100 10格",
                "⌀20线管 2000mm"
            }, 250);

            Dictionary<string, string> values = FrameBlockFiller.BuildValues(
                Row(), stat);

            Assert.Equal("", values[FrameBlockFiller.TagInfo2]);
        }

        [Fact]
        public void BuildValues_Info2PreservesOnlyUnknownCategoryDuringUpdate()
        {
            const string bridgeModel = "梯形桥架200Wx100H";
            const string conduitModel = "镀锌穿线管EMT PIPE 20mm(3/4\")";
            const string hoseModel = "20mm(3/4\") 包塑金属软管(波纹管)附镀锌接头";
            const string oldBridge = bridgeModel + ": 1000 = 1000mm";
            const string oldConduit = conduitModel + ": 2000 = 2000mm";
            const string oldHose = hoseModel + ": 3000 = 3000mm";

            CableStatResult stat = StatCalculator.Calculate(
                new[] { "⌀20线管 3500mm" }, 250);
            stat.Conduits[0].CatalogModel =
                "镀锌穿线管EMT PIPE 20mm(3/4\")";

            Dictionary<string, string> values = FrameBlockFiller.BuildValues(
                Row(), stat, false, false, true, false,
                oldBridge + "；" + oldConduit + "；" + oldHose);

            Assert.Equal(oldBridge + "；镀锌穿线管20mm 3500mm",
                values[FrameBlockFiller.TagInfo2]);
        }

        [Fact]
        public void BuildValues_Info2PreservesNewConduitFormatWhenMeasurementIsUnknown()
        {
            const string oldConduit = "镀锌穿线管38mm 2000 +2500 = 4500mm";
            CableStatResult stat = StatCalculator.Calculate(
                new[] { "⌀38线管 3500mm" }, 250);
            stat.Conduits[0].CatalogModel =
                "镀锌穿线管EMT PIPE 38mm(1-1/2\")";

            Dictionary<string, string> values = FrameBlockFiller.BuildValues(
                Row(), stat, false, false, false, true, oldConduit);

            Assert.Equal(oldConduit, values[FrameBlockFiller.TagInfo2]);
        }

        [Fact]
        public void BuildValues_Info2PreservesLegacyBridgeAndConduitButClearsHose()
        {
            const string oldBridge = "梯形桥架200Wx100H: 1000 = 1000mm";
            const string oldConduit =
                "镀锌穿线管EMT PIPE 38mm(1-1/2\"): 2000 +2500 = 4500mm";
            const string oldHose =
                "38mm(1-1/2\")包塑金属软管(波纹管)附镀锌接头: 3000 = 3000mm";
            CableStatResult stat = StatCalculator.Calculate(
                new[] { "⌀38线管 3500mm" }, 250);
            stat.Conduits[0].CatalogModel =
                "镀锌穿线管EMT PIPE 38mm(1-1/2\")";

            Dictionary<string, string> values = FrameBlockFiller.BuildValues(
                Row(), stat, false, false, true, true,
                oldBridge + "；" + oldConduit + "；" + oldHose);

            Assert.Equal(oldBridge + "；" + oldConduit,
                values[FrameBlockFiller.TagInfo2]);
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
        public void BuildValues_WritesConduitSegmentsInMillimeters_WithChineseComma()
        {
            var stat = StatCalculator.Calculate(new[]
            {
                "⌀20线管 2000mm",
                "⌀20线管 2500mm",
                "⌀25线管 3000mm"
            }, 250.0);
            stat.Conduits[0].CatalogModel = "镀锌穿线管DN20";
            stat.Conduits[1].CatalogModel = "镀锌穿线管DN25";
            var v = FrameBlockFiller.BuildValues(Row(), stat);

            Assert.Equal(
                "镀锌穿线管DN20: 2000 +2500 = 4500mm，镀锌穿线管DN25: 3000mm",
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

            Assert.Equal("ZB-YJV-3*70+1*35mm²: 2000 +3000 +3000 +4000 +4400mm",
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
                "ZB-YJV-3*70+1*35mm²: 2000 +3000 +3400（盘内） +500（设备内）"
                + " +3000（预留）=11900mm",
                v[FrameBlockFiller.TagCable]);
        }

        [Fact]
        public void BuildValues_SingleMeasuredCableUsesMillimeters()
        {
            var stat = StatCalculator.Calculate(new[] { "2000mm" }, 250.0);
            CableReservePolicy.Apply(stat, "插座盘", 500d);
            var v = FrameBlockFiller.BuildValues(Row(), stat);

            Assert.Equal(
                "ZB-YJV-3*70+1*35mm²: 2000 +1000（盘内） +500（设备内）"
                + " +3000（预留）=6500mm",
                v[FrameBlockFiller.TagCable]);
        }

        [Fact]
        public void BuildValues_OmitsDeviceReserveWhenNotReserved()
        {
            var stat = StatCalculator.Calculate(new[] { "2000mm" }, 250.0);
            CableReservePolicy.Apply(stat, "I-Line盘", 0d);
            var v = FrameBlockFiller.BuildValues(Row(), stat);

            Assert.Equal(
                "ZB-YJV-3*70+1*35mm²: 2000 +3400（盘内） +3000（预留）=8400mm",
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

            Assert.Equal(expected + "mm²: 2000mm", v[FrameBlockFiller.TagCable]);
        }

        [Fact]
        public void BuildValues_PrependsCableTypePrefixWithoutStatistics()
        {
            // 没有统计文字时图框只写型号，同样要带前缀，保持前后一致。
            var row = Row();
            row.Cable = "3*2.5";
            var v = FrameBlockFiller.BuildValues(row, new CableStatResult());

            Assert.Equal("ZB-YJVR-3*2.5", v[FrameBlockFiller.TagCable]);
        }

        [Fact]
        public void BuildValues_CableInfoMatchesMillimetreFormulaWithoutCableWord()
        {
            var row = Row();
            row.Cable = "3*2.5";
            var stat = new CableStatResult
            {
                CableSum = 74.6d,
                MeasuredCableSum = 70.6d,
                CableReserveApplied = true,
                PanelReserveMillimeters = 1000d,
                PanelReserveLabel = "盘内",
                OwnerReserveMillimeters = 3000d
            };
            stat.CableFormatted.AddRange(new[]
            {
                "2.3", "18.5", "1.5", "1.5", "27", "1.5", "1.5",
                "0.5", "2.5", "6", "2", "5", "0.5", "0.3"
            });

            string info = FrameBlockFiller.BuildValues(row, stat)[
                FrameBlockFiller.TagCable];

            Assert.Equal(
                "ZB-YJVR-3*2.5mm²: 2300 +18500 +1500 +1500 +27000 +1500 +1500"
                + " +500 +2500 +6000 +2000 +5000 +500 +300 +1000（盘内）"
                + " +3000（预留）=74600mm",
                info);
            Assert.DoesNotContain("电缆", info);
        }




        [Fact]
        public void BuildValues_UpdateModeOmitsStatisticsTagsWithoutFreshMeasurements()
        {
            var empty = new CableStatResult();

            Dictionary<string, string> values = FrameBlockFiller.BuildValues(
                Row(), empty, true);

            Assert.False(values.ContainsKey(FrameBlockFiller.TagCable));
            Assert.False(values.ContainsKey(FrameBlockFiller.TagConduit));
            Assert.False(values.ContainsKey(FrameBlockFiller.TagInfo2));
            Assert.Equal("MDAPT01-泵1", values[FrameBlockFiller.TagDevice]);
        }
    }
}
