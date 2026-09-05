using UNCAD.Cad;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FrameRegionCollectorTests
    {
        [Theory]
        [InlineData("frame")]
        [InlineData("frame_20260812")]
        [InlineData("xframe")]
        [InlineData("xframe$0$")]
        [InlineData("FRAME_20260812$12$$3$")]
        public void SupportedFrameNamesIncludeLegacyAndMangledDefinitions(string name)
        {
            Assert.True(FrameRegionCollector.IsSupportedFrameName(name));
        }

        [Theory]
        [InlineData("")]
        [InlineData("*U42")]
        [InlineData("frameinfo_json")]
        [InlineData("xframe_backup")]
        public void UnsupportedFrameNamesAreNotAccepted(string name)
        {
            Assert.False(FrameRegionCollector.IsSupportedFrameName(name));
        }

        [Fact]
        public void LegacyFrameClassificationExcludesCurrentXframe()
        {
            Assert.True(FrameRegionCollector.IsLegacyFrameName("frame"));
            Assert.True(FrameRegionCollector.IsLegacyFrameName("frame_20260812"));
            Assert.False(FrameRegionCollector.IsLegacyFrameName("xframe"));
        }
    }
}
