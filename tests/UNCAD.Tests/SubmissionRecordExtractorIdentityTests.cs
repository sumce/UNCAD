using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class SubmissionRecordExtractorIdentityTests
    {
        [Fact]
        public void Extract_UsesPersistedIdentityWhenLegacyAttributesAreMissing()
        {
            var source = new SubmissionSourceData();
            var persisted = new FrameInfoJsonRecord
            {
                MachineId = "208AE",
                DeviceName = "设备回路A"
            };

            SubmissionRecord result = SubmissionRecordExtractor.Extract(source, false,
                persisted);

            Assert.Equal("208AE", result.MachineId);
            Assert.Equal("设备回路A", result.DeviceName);
        }

        [Fact]
        public void Extract_PersistedIdentityOverridesStaleCadAttributes()
        {
            var source = new SubmissionSourceData();
            source.AddAttribute("MACHINEID-POWER", "OLD-POWER");
            source.AddAttribute("DEVICENAME", "旧设备");
            source.AddAttribute("DEVICENAME", "另一个旧设备");
            var persisted = new FrameInfoJsonRecord
            {
                MachineId = "208AE",
                DeviceName = "新设备"
            };

            SubmissionRecord result = SubmissionRecordExtractor.Extract(source, false,
                persisted);

            Assert.Equal("208AE", result.MachineId);
            Assert.Equal("新设备", result.DeviceName);
        }
    }
}
