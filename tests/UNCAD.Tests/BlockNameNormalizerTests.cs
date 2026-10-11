using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>软管块族识别：Ruanguan 与 Ruanguan_GW 必须同等处理。</summary>
    public sealed class BlockNameNormalizerTests
    {
        [Theory]
        [InlineData("Ruanguan", true)]
        [InlineData("ruanguan", true)]
        [InlineData("Ruanguan_GW", true)]        // 现场变体：同样是软管
        [InlineData("Ruanguan-GW", true)]
        [InlineData("RUANGUAN 2", true)]
        [InlineData("Ruanguan$0$", true)]        // Xmerge 重命名后的块名
        [InlineData("Ruanguan_GW$0$$12$", true)]
        [InlineData("RuanguanXYZ", false)]       // 黏连名不是同族
        [InlineData("xframe", false)]
        [InlineData("", false)]
        public void IsFlexibleConduitBlock_AcceptsTheRuanguanFamilyOnly(
            string name, bool expected)
        {
            Assert.Equal(expected, BlockNameNormalizer.IsFlexibleConduitBlock(name));
        }

        [Fact]
        public void RemoveMangledSuffix_KeepsTheOriginalFamilyName()
        {
            Assert.Equal("Ruanguan_GW",
                BlockNameNormalizer.RemoveMangledSuffix("Ruanguan_GW$0$$12$"));
        }
    }
}
