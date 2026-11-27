using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class RuanguanLengthParserTests
    {
        [Theory]
        [InlineData("LENGTH", "2500", 2500)]
        [InlineData("长度", "2.5m", 2500)]
        [InlineData("LABEL", "100mm软管:1800mm", 1800)]
        public void TryReadLength_AcceptsQualifiedLengths(string tag, string value,
            double expectedMillimetres)
        {
            Assert.True(RuanguanLengthParser.TryReadLength(value, tag, out double mm));
            Assert.Equal(expectedMillimetres, mm);
        }

        [Theory]
        [InlineData("MODEL", "20mm")]
        [InlineData("DIA", "25mm")]
        [InlineData("OTHER", "2500")]
        [InlineData("LENGTH", "0")]
        public void TryReadLength_RejectsDiametersAndUnqualifiedNumbers(string tag, string value)
        {
            Assert.False(RuanguanLengthParser.TryReadLength(value, tag, out _));
        }
    }
}
