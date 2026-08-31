using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    public class RuanguanLabelFormatterTests
    {
        [Theory]
        [InlineData("20", "1500mm", "20mm软管:1500mm", 1500.0)]
        [InlineData("DN20", "1.5m", "20mm软管:1500mm", 1500.0)]
        [InlineData("⌀ 25mm", "软管长度=2,500mm", "25mm软管:2500mm", 2500.0)]
        public void TryBuild_FormatsMappedDiameterAndLength(string diameter,
            string source, string expected, double expectedMillimetres)
        {
            Assert.True(RuanguanLabelFormatter.TryBuild(diameter, source,
                out string label, out double millimetres));
            Assert.Equal(expected, label);
            Assert.Equal(expectedMillimetres, millimetres);
        }

        [Fact]
        public void TryBuild_IsIdempotentForExistingLabel()
        {
            Assert.True(RuanguanLabelFormatter.TryBuild("20",
                "20mm软管:1500mm", out string label, out double millimetres));
            Assert.Equal("20mm软管:1500mm", label);
            Assert.Equal(1500.0, millimetres);
        }

        [Theory]
        [InlineData("", "1500mm")]
        [InlineData("20", "软管型号 DN20")]
        [InlineData("20", "0mm")]
        [InlineData("1001", "1500mm")]
        public void TryBuild_RejectsMissingOrUnsafeValues(string diameter, string source)
        {
            Assert.False(RuanguanLabelFormatter.TryBuild(diameter, source,
                out string label, out double millimetres));
            Assert.Equal("", label);
            Assert.Equal(0.0, millimetres);
        }
    }
}
