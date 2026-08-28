using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    public class ConduitLabelFormatterTests
    {
        [Theory]
        [InlineData("20", "⌀20线管 2000mm")]
        [InlineData("25", "⌀25线管 2000mm")]
        [InlineData("32", "⌀32线管 2000mm")]
        public void Build_WithoutMeasuredLengthKeepsLegacyTwoMeterDefault(
            string diameter, string expected)
        {
            Assert.Equal(2000.0, ConduitLabelFormatter.DefaultLengthMm);
            Assert.Equal(expected, ConduitLabelFormatter.Build(diameter));
        }

        [Theory]
        [InlineData(3150.5, "⌀32线管 3150.5mm")]
        [InlineData(800, "⌀32线管 800mm")]
        [InlineData(-1, "⌀32线管 2000mm")]
        public void Build_WithMeasuredLengthUsesActualPositiveCurveLength(
            double lengthMm, string expected)
        {
            Assert.Equal(expected, ConduitLabelFormatter.Build("DN32", lengthMm));
        }
    }
}
