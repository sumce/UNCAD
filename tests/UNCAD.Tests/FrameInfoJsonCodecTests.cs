using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FrameInfoJsonCodecTests
    {
        [Fact]
        public void RoundTrip_PreservesIdentityCableOverrideAndHistory()
        {
            var source = new FrameInfoJsonRecord
            {
                MachineId = "M01",
                DeviceName = "插座01",
                OriginalCableModel = "OLD",
                BoqCableModel = "NEW",
                LastModifiedUtc = "2026-09-02T08:00:00Z",
                LastModifiedUser = "tester"
            };
            source.Changes.Add(new FrameInfoJsonChange
            {
                Field = "BoqCableModel",
                Before = "OLD",
                After = "NEW",
                Command = "U1U"
            });

            FrameInfoJsonRecord actual = FrameInfoJsonCodec.Parse(
                FrameInfoJsonCodec.Serialize(source));

            Assert.NotNull(actual);
            Assert.Equal("M01", actual.MachineId);
            Assert.Equal("插座01", actual.DeviceName);
            Assert.Equal("OLD", actual.OriginalCableModel);
            Assert.Equal("NEW", actual.BoqCableModel);
            Assert.Equal("tester", actual.LastModifiedUser);
            Assert.Equal("BoqCableModel", Assert.Single(actual.Changes).Field);
        }

        [Fact]
        public void Parse_AcceptsLegacyCaseAndRejectsDamagedPayload()
        {
            FrameInfoJsonRecord legacy = FrameInfoJsonCodec.Parse(
                "{\"MachineId\":\"M02\",\"DeviceName\":\"回路A\"}");

            Assert.Equal("M02", legacy.MachineId);
            Assert.Equal("回路A", legacy.DeviceName);
            Assert.Null(FrameInfoJsonCodec.Parse("{bad json"));
            Assert.Null(FrameInfoJsonCodec.Parse("not json"));
        }
    }
}
