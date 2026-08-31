using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>TextParser 规则测试：与 LISP 版行为逐字对应。</summary>
    public class TextParserTests
    {
        [Theory]
        [InlineData("1200mm", 1200.0)]
        [InlineData("2000mm", 2000.0)]
        [InlineData(" 2000mm ", 2000.0)]       // 首尾空格容忍
        [InlineData("2000MM", 2000.0)]
        [InlineData("100mm", 100.0)]
        [InlineData("电缆 2000mm 长度", null)]  // 带前后缀不算（必须纯数字+mm）
        [InlineData("1200mm 备注", null)]
        [InlineData("1050mm", null)]            // 数字结尾必须 00
        [InlineData("2000m", null)]             // 必须 mm
        [InlineData("无长度数据", null)]
        [InlineData(null, null)]
        public void ExtractCableLength_StrictWholeLine(string input, double? expected)
        {
            Assert.Equal(expected, TextParser.ExtractCableLength(input));
        }

        [Theory]
        [InlineData("桥架200*100 10格", "桥架200*100")]
        [InlineData("桥架 300 x 150 5格", "桥架300*150")]
        [InlineData("桥架300X150 5格", "桥架300*150")]
            [InlineData("桥架300X150 10格", "桥架300*150")]
            [InlineData("桥架300×150 2500mm", "桥架300*150")]
        [InlineData("安装桥架400*100 10格", null)] // 必须以"桥架"开头
        [InlineData("(共用)桥架200*100 12格", null)]
        [InlineData("桥架200*100 共用 12格", null)]
        [InlineData("桥架200*100 12格 备注", null)]
            [InlineData("桥架200×100 12格", null)]
            [InlineData("桥架200×100 3000mm", "桥架200*100")]
        [InlineData("桥架200*10012格", null)]
        [InlineData("只有桥架两字", null)]         // 无规格尺寸不算
        [InlineData("普通文字", null)]
        public void ExtractBridgeSpec_RequiresStrictWholeLine(string input, string expected)
        {
            Assert.Equal(expected, TextParser.ExtractBridgeSpec(input));
        }

        [Fact]
        public void TryExtractBridgeLabel_ReturnsNormalizedSpecAndGridCount()
        {
            Assert.True(TextParser.TryExtractBridgeLabel("桥架 300 x 150 12.5格",
                out string spec, out double grids));
            Assert.Equal("桥架300*150", spec);
            Assert.Equal(12.5, grids);
        }

        [Theory]
        [InlineData("⌀20线管 2000mm", "⌀20线管", 2000.0)]
        [InlineData("Ø25线管 2500mm", "⌀25线管", 2500.0)]
        [InlineData("Φ32 线管 3000MM", "⌀32线管", 3000.0)]
        public void ExtractConduit_NormalizesSpecAndReadsLength(
            string input, string expectedSpec, double expectedLength)
        {
            Assert.Equal(expectedSpec, TextParser.ExtractConduitSpec(input));
            Assert.Equal(expectedLength, TextParser.ExtractConduitLength(input));
        }

        [Theory]
        [InlineData("⌀20线管")]
        [InlineData("20线管 2000mm")]
        [InlineData("⌀20线管 2000mm 备注")]
        public void ExtractConduit_RejectsIncompleteLabels(string input)
        {
            Assert.Null(TextParser.ExtractConduitLength(input));
        }

        [Fact]
        public void SplitMTextLines_SplitsOnBackslashP_AndTrims()
        {
            var lines = TextParser.SplitMTextLines("第一行\\P  第二行  \\P第三行");
            Assert.Equal(new List<string> { "第一行", "第二行", "第三行" }, lines);
        }

        [Fact]
        public void SplitMTextLines_EmptyReturnsEmpty()
        {
            Assert.Empty(TextParser.SplitMTextLines(null));
            Assert.Empty(TextParser.SplitMTextLines(""));
        }

        [Fact]
        public void CleanMText_StripsControlCodes_KeepsStackedText()
        {
            string dirty = "{\\fSimSun;说明} 1\\S2/3;";
            string cleaned = TextParser.CleanMText(dirty);
            Assert.DoesNotContain("\\f", cleaned);
            Assert.DoesNotContain("{", cleaned);
            Assert.DoesNotContain("}", cleaned);
        }

        [Fact]
        public void FormatNum_TrimsTrailingZeros_LikeRtos()
        {
            Assert.Equal("1", TextFormatter.FormatNum(1.00));
            Assert.Equal("1.5", TextFormatter.FormatNum(1.50));
            Assert.Equal("12.34", TextFormatter.FormatNum(12.34));
        }
    }
}
