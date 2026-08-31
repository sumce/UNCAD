using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// Covers BOQ migration for drawings whose table predates the 4.x socket-panel rows.
    /// </summary>
    public class SubmissionRecordExtractorSocketPanelTests
    {
        [Fact]
        public void Extract_InfersTwentyAmpPanelWhenLegacyTableHasOnlyOutlet()
        {
            SubmissionSourceData source = Source("M01", "U220 1P3W 1P20A", "socket box");
            source.AddTableRow("1", "插座", "1.名称:插座20A~30A", "个", "1", "8.3");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.Single(record.Materials, item => item.Code == "4.11");
            Assert.Contains(record.Materials, item => item.Code == "8.3");
        }

        [Fact]
        public void Extract_DoesNotDuplicateExistingSocketPanelRow()
        {
            SubmissionSourceData source = Source("M01", "U220 1P3W 1P20A", "socket box");
            source.AddTableRow("1", "插座盘", "1.名称:插座盘", "个", "1", "4.11");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.Single(record.Materials, item => item.Code == "4.11");
        }

        [Fact]
        public void Extract_UsesUpstreamStateAndDoesNotRequireDeviceSocketState()
        {
            SubmissionSourceData source = Source("M01", "N220 1P3W 1P16A", "socket box");
            source.DynamicValues.Add("设备");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.Contains(record.Materials, item => item.Code == "4.12");
        }

        [Fact]
        public void Extract_DoesNotInferPanelForNonSocketUpstream()
        {
            SubmissionSourceData source = Source("M01", "U220 1P3W 1P20A", "I-line_Panel");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.DoesNotContain(record.Materials, item => item.Code.StartsWith("4."));
        }

        [Fact]
        public void Extract_DoesNotGuessUnsupportedPanelRating()
        {
            SubmissionSourceData source = Source("M01", "N220 1P3W 1P10A", "socket box");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.DoesNotContain(record.Materials, item => item.Code.StartsWith("4."));
        }

        [Fact]
        public void Extract_DoesNotRecreatePanelWhenPostUpdateInferenceIsDisabled()
        {
            SubmissionSourceData source = Source("M01", "U220 1P3W 1P20A", "socket box");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source, false);

            Assert.Empty(record.Materials);
        }

        [Fact]
        public void Extract_TreatsNumberColumnAsPanelCodeDuringMigration()
        {
            SubmissionSourceData source = Source("M01", "U220 1P3W 1P20A", "socket box");
            // Some manually edited legacy tables put the fixed code in NO. and leave
            // the final 项次编码 cell blank. This must still suppress inference.
            source.AddTableRow("4.11", "插座盘", "1.名称:插座盘", "个", "1", "");

            SubmissionRecord record = SubmissionRecordExtractor.Extract(source);

            Assert.Single(record.Materials);
            Assert.Equal("4.11", record.Materials[0].Number);
        }

        private static SubmissionSourceData Source(string machineId, string detail,
            string upstreamState)
        {
            var source = new SubmissionSourceData();
            source.AddAttribute(FrameBlockFiller.TagPower, machineId + "-POWER");
            source.AddAttribute(DeviceBlockFiller.TagDeviceName, "设备1");
            source.AddAttribute(ConnectionBlockFiller.TagUpstreamInfo,
                "UPSTREAM\\P" + detail);
            source.DynamicValues.Add(upstreamState);
            return source;
        }
    }
}
