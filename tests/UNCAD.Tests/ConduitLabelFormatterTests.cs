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
        public void LengthText_IsTheTwoLineLabelSecondLine()
        {
            // 两行标注的第二行与单行写法的长度段必须是同一份文字。
            Assert.Equal("2000mm", ConduitLabelFormatter.LengthText);
            Assert.True(TextParser.IsBareLengthToken(ConduitLabelFormatter.LengthText));
            Assert.EndsWith(" " + ConduitLabelFormatter.LengthText,
                ConduitLabelFormatter.Build("20"));
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
