using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    public class DimensionTextFormatterTests
    {
        [Theory]
        [InlineData("", "", 3000.0, "3000mm")]
        [InlineData("<>mm", "", 2500.0, "2500mm")]
        [InlineData("", "<> mm", 1200.0, "1200mm")]
        [InlineData(@"{\fSimSun;3400mm}", "", 1.0, "3400mm")]
        [InlineData("桥架200*100 <>mm", "", 2500.0, "桥架200*100 2500mm")]
        public void TryBuildSingleLine_PreservesDisplayedMeaning(string dimensionText,
            string dimPost, double measurement, string expected)
        {
            Assert.True(DimensionTextFormatter.TryBuildSingleLine(
                dimensionText, dimPost, measurement, out string actual));
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryBuildSingleLine_RejectsSuppressedOrInvalidDimensions()
        {
            Assert.False(DimensionTextFormatter.TryBuildSingleLine(
                DimensionTextFormatter.SuppressedDimensionText, "", 3000, out _));
            Assert.False(DimensionTextFormatter.TryBuildSingleLine(
                "", "", double.NaN, out _));
        }
    }
}
