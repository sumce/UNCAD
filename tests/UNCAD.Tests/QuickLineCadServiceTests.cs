using UNCAD.Cad.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLineCadServiceTests
    {
        [Theory]
        [InlineData("2000mm", 2000)]
        [InlineData("  1250.5 MM ", 1250.5)]
        [InlineData(".5mm", 0.5)]
        [InlineData("0mm", 0)]
        public void TryParseMillimetres_AcceptsCanonicalLabels(string value, double expected)
        {
            Assert.True(QuickLineCadService.TryParseMillimetres(value, out double actual));
            Assert.Equal(expected, actual, 8);
        }

        [Theory]
        [InlineData("2000")]
        [InlineData("length 2000mm")]
        [InlineData("2000.5.5mm")]
        public void TryParseMillimetres_RejectsNonLabels(string value)
        {
            Assert.False(QuickLineCadService.TryParseMillimetres(value, out _));
        }

        [Fact]
        public void FormatMillimetres_UsesInvariantCompactValue()
        {
            Assert.Equal("1250.5mm", QuickLineCadService.FormatMillimetres(1250.5));
            Assert.Equal("2000mm", QuickLineCadService.FormatMillimetres(2000));
        }
    }
}
