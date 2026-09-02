using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FillColorSettingsTests
    {
        [Fact]
        public void Defaults_AreGreenForDeviceAndMagentaForUpstream()
        {
            Assert.Equal(3, FillColorSettings.DefaultDeviceColorIndex);
            Assert.Equal(6, FillColorSettings.DefaultUpstreamColorIndex);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(255, 255)]
        [InlineData(0, 3)]
        [InlineData(256, 3)]
        public void Normalize_AcceptsOnlyExplicitAciColors(int value, int expected)
            => Assert.Equal(expected, FillColorSettings.Normalize(value, 3));
    }
}
