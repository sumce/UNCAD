using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    public class BridgeLabelFormatterTests
    {
        [Theory]
        [InlineData("桥架200x100 10格", 250.0, "桥架200*100 2500mm")]
        [InlineData("桥架200*100 2500mm", 250.0, "桥架200*100 2500mm")]
        [InlineData("桥架200×100 2500mm", 250.0, "桥架200*100 2500mm")]
        [InlineData("桥架200*100 10格", 250.0, "桥架200*100 2500mm")]
        public void TryNormalize_UsesMillimetreLabelsAndMigratesLegacyGrids(
            string source, double mmPerGrid, string expected)
        {
            Assert.True(BridgeLabelFormatter.TryNormalize(source, mmPerGrid,
                out string actual));
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void NormalizeOrDefault_FallsBackForInvalidText()
        {
            Assert.Equal("桥架200*100 2500mm", BridgeLabelFormatter.NormalizeOrDefault(
                "桥架型号 mm", 250.0));
        }

        [Fact]
        public void TryNormalize_AcceptsMillimetresWithoutConversionScale()
        {
            Assert.True(BridgeLabelFormatter.TryNormalize("桥架200*100 2500mm",
                0.0, out string actual));
            Assert.Equal("桥架200*100 2500mm", actual);
        }
    }
}
