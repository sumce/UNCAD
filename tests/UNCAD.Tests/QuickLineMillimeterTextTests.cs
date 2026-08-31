using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLineMillimeterTextTests
    {
        [Theory]
        [InlineData("2000mm", 2000.0)]
        [InlineData(" 1234.5 MM ", 1234.5)]
        [InlineData(".5mm", 0.5)]
        [InlineData("0mm", 0.0)]
        public void TryParse_AcceptsStandaloneDecimalMillimeterLabels(
            string input, double expected)
        {
            double actual;
            Assert.True(QuickLineMillimeterText.TryParse(input, out actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("2000")]
        [InlineData("2000 m")]
        [InlineData("cable 2000mm")]
        [InlineData("-1mm")]
        [InlineData("NaNmm")]
        [InlineData("1.2.3mm")]
        public void TryParse_RejectsNonStandaloneOrInvalidLabels(string input)
        {
            double actual;
            Assert.False(QuickLineMillimeterText.TryParse(input, out actual));
        }

        [Theory]
        [InlineData(2000.0, "2000mm")]
        [InlineData(1234.5, "1234.5mm")]
        [InlineData(0.25, "0.25mm")]
        public void Format_UsesInvariantMillimetersWithoutTrailingZeros(
            double input, string expected)
        {
            Assert.Equal(expected, QuickLineMillimeterText.Format(input));
        }

        [Fact]
        public void Format_RejectsNonFiniteAndNegativeValues()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => QuickLineMillimeterText.Format(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => QuickLineMillimeterText.Format(double.NaN));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => QuickLineMillimeterText.Format(double.PositiveInfinity));
        }

        [Theory]
        [InlineData("Phi20 conduit 2700mm", 2700)]
        [InlineData("distance: .5 MM / confirmed", 0.5)]
        public void TryExtractSingle_AcceptsOneEmbeddedToken(string text,
            double expected)
        {
            Assert.True(QuickLineMillimeterText.TryExtractSingle(text,
                out double actual));
            Assert.Equal(expected, actual, 8);
        }

        [Theory]
        [InlineData("no distance")]
        [InlineData("100mm + 200mm")]
        public void TryExtractSingle_RejectsMissingOrAmbiguousTokens(string text)
        {
            Assert.False(QuickLineMillimeterText.TryExtractSingle(text, out _));
        }

        [Theory]
        [InlineData("1250", 1250.0)]
        [InlineData(" 1250.5 ", 1250.5)]
        [InlineData(".5", 0.5)]
        public void TryParseBareNumber_AcceptsExplicitNumericAnnotations(
            string text, double expected)
        {
            Assert.True(QuickLineMillimeterText.TryParseBareNumber(text,
                out double actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1250mm")]
        [InlineData("cable 1250")]
        [InlineData("-1")]
        public void TryParseBareNumber_RejectsAmbiguousValues(string text)
        {
            Assert.False(QuickLineMillimeterText.TryParseBareNumber(text,
                out _));
        }

        [Theory]
        [InlineData(@"{\fSimSun|b0;3400mm}", 3400.0)]
        [InlineData(@"{\fArial|b1;12\~mm}", 12.0)]
        [InlineData(@"３２００ｍｍ", 3200.0)]
        public void TryParseCad_RemovesPresentationCodesAndNormalizesDigits(
            string text, double expected)
        {
            Assert.True(QuickLineMillimeterText.TryParseCad(text, out double actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("3400", 3400.0)]
        [InlineData("1,5", 1.5)]
        public void TryParseNumber_AcceptsOnlyNumericDimensionText(
            string text, double expected)
        {
            Assert.True(QuickLineMillimeterText.TryParseNumber(text, out double actual));
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryParseNumber_RejectsUnitsAndDescriptions()
        {
            Assert.False(QuickLineMillimeterText.TryParseNumber("3400mm", out _));
            Assert.False(QuickLineMillimeterText.TryParseNumber("length 3400", out _));
        }

        [Theory]
        [InlineData("3400", 2000, 3400)]
        [InlineData("3400mm", 2000, 3400)]
        [InlineData("actual 3400mm", 2000, 3400)]
        [InlineData("", 2000, 2000)]
        [InlineData("<>mm", 2000, 2000)]
        public void ResolveDimensionValue_PrefersExplicitDisplayedDistance(
            string dimensionText, double measurement, double expected)
        {
            Assert.True(QuickLineMillimeterText.TryResolveDimensionValue(
                dimensionText, measurement, out double actual));
            Assert.Equal(expected, actual, 8);
        }

        [Fact]
        public void TryReplaceSingle_PreservesSurroundingDescription()
        {
            Assert.True(QuickLineMillimeterText.TryReplaceSingle(
                "Phi20 conduit 2700mm / A", 1250.5, out string replacement));
            Assert.Equal("Phi20 conduit 1250.5mm / A", replacement);
        }

        [Fact]
        public void TryReplaceSingleCad_HandlesFormattedMText()
        {
            Assert.True(QuickLineMillimeterText.TryReplaceSingleCad(
                @"{\fSimSun|b0;3400mm}", 1250, out string replacement));
            Assert.Equal(@"{\fSimSun|b0;1250mm}", replacement);
        }
    }
}
