using System.IO;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using Xunit;

namespace UNCAD.Tests
{
    public class SubmissionRecordExtractorTests
    {
        [Fact]
        public void Extract_ReadsFilledBlocksAndPanelType()
        {
            var source = new SubmissionSourceData();
            source.AddAttribute(FrameBlockFiller.TagPower, "MQBAN01-POWER");
            source.AddAttribute(FrameBlockFiller.TagDevice, "MQBAN01-中央实验台1-1");
            source.AddAttribute(DeviceBlockFiller.TagDeviceName, "中央实验台1-1");
            source.AddAttribute(FrameBlockFiller.TagCable, "ZB-YJVR-3*2.5mm²: 2+3=5M");
            source.AddAttribute(FrameBlockFiller.TagBridge,
                "桥架200*100 3M; 桥架400*100 1.5M");
            source.AddAttribute(FrameBlockFiller.TagConduit,
                "⌀20线管 2M，⌀25线管 3M");
            source.AddTableRow("1", "包塑金属软管",
                "1.名称:25mm包塑金属软管(波纹管)", "M", "2", "3.1");
            source.AddAttribute(ConnectionBlockFiller.TagUpstreamInfo,
                "3U-UPS28D1-PP-03\\PU220 1P3W 1P20A");
            source.AddAttribute(ConnectionBlockFiller.TagDownstreamAxis, "2/T");
            source.AddAttribute(ConnectionBlockFiller.TagUpstreamAxis, "化学实验室");
            source.DynamicValues.Add("I-line_Panel");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.Equal("MQBAN01", record.MachineId);
            Assert.Equal("中央实验台1-1", record.DeviceName);
            Assert.Equal("I-Line盘", record.PanelType);
            Assert.Equal("ZB-YJVR-3*2.5", record.OriginalCable);
            Assert.Equal("ZB-YJVR-3*2.5", record.Cable);
            Assert.Equal("5", record.CableMeters);
            Assert.Equal("3U-UPS28D1-PP-03", record.Fr);
            Assert.Equal("U220 1P3W 1P20A", record.Detail);
            Assert.Equal("25", record.Diameter);
            Assert.Equal("2", record.FlexibleConduitMeters);
            Assert.Equal("桥架200*100 3M; 桥架400*100 1.5M", record.BridgeInfo);
            Assert.Equal("4.5", record.BridgeMeters);
            Assert.Equal("⌀20线管 2M，⌀25线管 3M", record.ConduitInfo);
            Assert.Equal("5", record.ConduitMeters);
            Assert.Equal("2/T", record.DownstreamAxis);
            Assert.Equal("化学实验室", record.UpstreamAxis);
            Assert.Single(record.Materials);
            Assert.Equal("包塑金属软管", record.Materials[0].Name);
            Assert.Equal("2", record.Materials[0].Quantity);
            Assert.Equal("3.1", record.Materials[0].Code);
            Assert.Equal(1, record.TableRowsRead);
        }

        [Fact]
        public void Extract_InfersPanelFromTableWhenDynamicStateMissing()
        {
            var source = new SubmissionSourceData();
            source.AddAttribute(FrameBlockFiller.TagPower, "M01-POWER");
            source.AddAttribute(DeviceBlockFiller.TagDeviceName, "插座1");
            source.TableValues.Add("母线插接箱");
            Assert.Equal("母线插接口", SubmissionRecordExtractor.Extract(source).PanelType);
        }

        [Fact]
        public void Extract_FallsBackToTableQuantities_WhenFrameStatisticsAreMissing()
        {
            var source = new SubmissionSourceData();
            source.AddAttribute(FrameBlockFiller.TagPower, "M01-POWER");
            source.AddAttribute(DeviceBlockFiller.TagDeviceName, "设备1");
            source.AddTableRow("1", "电缆", "1.名称:电缆", "M", "12.5", "1.1");
            source.AddTableRow("2", "桥架200*100", "1.名称:桥架200*100", "M", "3.2", "2.1");
            source.AddTableRow("3", "镀锌钢管", "1.名称:穿线管⌀20", "M", "4", "3.1");
            source.AddTableRow("4", "包塑金属软管", "1.名称:32mm包塑金属软管", "M", "2", "3.2");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.Equal("12.5", record.CableMeters);
            Assert.Equal("32", record.Diameter);
            Assert.Equal("2", record.FlexibleConduitMeters);
            Assert.Equal("3.2", record.BridgeMeters);
            Assert.Equal("4", record.ConduitMeters);
            Assert.Equal(4, record.Materials.Count);
            Assert.Equal("电缆", record.Materials[0].Name);
            Assert.Equal("3.2", record.Materials[1].Quantity);
            Assert.Equal("3.2", record.Materials[3].Code);
        }

        [Fact]
        public void Extract_CountsTableRowsEvenWhenNoMaterialsAreRecognized()
        {
            var source = new SubmissionSourceData();
            source.AddAttribute(FrameBlockFiller.TagPower, "M01-POWER");
            source.AddAttribute(DeviceBlockFiller.TagDeviceName, "设备1");
            // 只有表头行，没有数据行：Materials 为空，但行数必须可诊断。
            source.AddTableRow("No.", "项目名称", "项目特征", "单位", "数量", "编码");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.Empty(record.Materials);
            Assert.Equal(1, record.TableRowsRead);
        }

        [Fact]
        public void Extract_ExposesTextEntityCountForSelectionDiagnostics()
        {
            var source = new SubmissionSourceData();
            source.AddAttribute(FrameBlockFiller.TagPower, "M01-POWER");
            source.AddAttribute(DeviceBlockFiller.TagDeviceName, "设备1");
            source.TextEntityCount = 12;

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.Equal(12, record.TextEntityCount);
        }

        [Fact]
        public void Extract_DoesNotMistakeGenericCableCatalogTextForModel()
        {
            var source = new SubmissionSourceData();
            source.AddAttribute(FrameBlockFiller.TagPower, "M01-POWER");
            source.AddAttribute(DeviceBlockFiller.TagDeviceName, "设备1");
            source.AddAttribute(FrameBlockFiller.TagCable, "ZB-YJVR-3*2.5mm²: 5M");
            source.AddTableRow("1", "多芯电缆 XLPE", @"电缆第一行\P电缆第二行", "M", "5", "1.1");

            Assert.Equal("ZB-YJVR-3*2.5", SubmissionRecordExtractor.Extract(source).Cable);
        }

        [Fact]
        public void Extract_PrefersCurrentEditedTableValuesOverOldFrameAttributes()
        {
            var source = new SubmissionSourceData();
            source.AddAttribute(FrameBlockFiller.TagPower, "M01-POWER");
            source.AddAttribute(DeviceBlockFiller.TagDeviceName, "设备1");
            source.AddAttribute(FrameBlockFiller.TagCable, "OLD-CABLEmm²: 2+3=5M");
            source.AddAttribute(FrameBlockFiller.TagBridge, "旧桥架 4M");
            source.AddAttribute(FrameBlockFiller.TagConduit, "旧线管 6M");
            source.AddTableRow("1", "自定义供电线", "NEW-CABLE", "M", "18.5", "1.9");
            source.AddTableRow("2", "用户修改托盘", "任意特征", "M", "7.25", "2.9");
            source.AddTableRow("3", "用户修改钢管", "任意特征", "M", "9", "3.9");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.Equal("OLD-CABLE", record.OriginalCable);
            Assert.Equal("NEW-CABLE", record.Cable);
            Assert.Equal("18.5", record.CableMeters);
            Assert.Equal("7.25", record.BridgeMeters);
            Assert.Contains("用户修改托盘", record.BridgeInfo);
            Assert.Equal("9", record.ConduitMeters);
            Assert.Contains("用户修改钢管", record.ConduitInfo);
            Assert.Equal(3, record.Materials.Count);
        }

        [Fact]
        public void Extract_RejectsMultipleDifferentDevices()
        {
            var source = new SubmissionSourceData();
            source.AddAttribute(FrameBlockFiller.TagPower, "M01-POWER");
            source.AddAttribute(DeviceBlockFiller.TagDeviceName, "设备1");
            source.AddAttribute(DeviceBlockFiller.TagDeviceName, "设备2");
            Assert.Throws<InvalidDataException>(() => SubmissionRecordExtractor.Extract(source));
        }
    }
}
