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
            source.AddAttribute(FrameBlockFiller.TagConduit, "⌀20线管 2M");
            source.AddAttribute(ConnectionBlockFiller.TagUpstreamInfo,
                "3U-UPS28D1-PP-03\\PU220 1P3W 1P20A");
            source.AddAttribute(ConnectionBlockFiller.TagDownstreamAxis, "2/T");
            source.AddAttribute(ConnectionBlockFiller.TagUpstreamAxis, "化学实验室");
            source.DynamicValues.Add("I-line_Panel");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.Equal("MQBAN01", record.MachineId);
            Assert.Equal("中央实验台1-1", record.DeviceName);
            Assert.Equal("I-Line盘", record.PanelType);
            Assert.Equal("ZB-YJVR-3*2.5", record.Cable);
            Assert.Equal("3U-UPS28D1-PP-03", record.Fr);
            Assert.Equal("U220 1P3W 1P20A", record.Detail);
            Assert.Equal("20", record.Diameter);
            Assert.Equal("2/T", record.DownstreamAxis);
            Assert.Equal("化学实验室", record.UpstreamAxis);
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
