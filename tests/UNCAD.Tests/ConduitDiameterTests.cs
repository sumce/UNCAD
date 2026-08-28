using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    public class ConduitDiameterTests
    {
        [Theory]
        [InlineData("32", "32")]
        [InlineData("Φ32", "32")]
        [InlineData("⌀ 32mm", "32")]
        [InlineData("DN32", "32")]
        [InlineData("32线管", "32")]
        [InlineData("38.0软管", "38")]
        public void TryNormalize_AcceptsEquivalentDiameterNotation(
            string source, string expected)
        {
            Assert.True(ConduitDiameter.TryNormalize(source, out string actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("")]
        [InlineData("DN")]
        [InlineData("32/38")]
        [InlineData("-32")]
        [InlineData("1001")]
        public void TryNormalize_RejectsAmbiguousOrUnsafeDiameter(string source)
        {
            Assert.False(ConduitDiameter.TryNormalize(source, out string actual));
            Assert.Equal("", actual);
        }
    }
}
