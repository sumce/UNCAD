using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 从固定清单「项目特征」取出 1.名称 段（BOQ 型号）。
    /// 内存中的特征用真实换行分段（ListItemReader 已还原源文件的 \n）。
    /// </summary>
    public class BoqFeatureNameTests
    {
        [Fact]
        public void Extract_ReadsNameSegmentBeforeFirstNewline()
        {
            string feature = "1.名称:梯形桥架200Wx100H\n2.材质:铝合金粉体烤漆\n3.说明:包含成品弯头";
            Assert.Equal("梯形桥架200Wx100H", BoqFeatureName.Extract(feature));
        }

        [Fact]
        public void Extract_ToleratesFullWidthColon()
        {
            Assert.Equal("深化图纸和竣工图绘制", BoqFeatureName.Extract("1.名称：深化图纸和竣工图绘制\n2.其他:…"));
        }

        [Theory]
        [InlineData("2.材质:铝合金\n3.说明:…")]     // 没有 1.名称 段
        [InlineData("桥架描述")]                      // 完全不是特征格式
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Extract_ReturnsEmptyWhenNoNameSegment(string feature)
        {
            Assert.Equal("", BoqFeatureName.Extract(feature));
        }

        [Fact]
        public void Extract_StopsAtCadParagraphBreak()
        {
            Assert.Equal("梯形桥架200Wx100H", BoqFeatureName.Extract("1.名称:梯形桥架200Wx100H\\P2.材质:…"));
        }

        [Fact]
        public void Extract_HandlesCarriageReturnLineEndings()
        {
            Assert.Equal("梯形桥架400Wx100H", BoqFeatureName.Extract("1.名称:梯形桥架400Wx100H\r\n2.材质:…"));
        }

        [Fact]
        public void Extract_ReturnsEmptyWhenNameSegmentIsBlank()
        {
            Assert.Equal("", BoqFeatureName.Extract("1.名称:\n2.材质:铝合金"));
        }
    }
}
