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

        [Fact]
        public void Build_DoesNotExposeMeasuredLengthOverride()
        {
            // 线管标签必须保持人工修改用的 2000mm 占位，防止再次接入曲线实测距离。
            Assert.Null(typeof(ConduitLabelFormatter).GetMethod("Build",
                new[] { typeof(string), typeof(double) }));
            Assert.Equal("⌀32线管 2000mm", ConduitLabelFormatter.Build("DN32"));
        }
    }
}
