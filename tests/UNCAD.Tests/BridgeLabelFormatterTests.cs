using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    public class BridgeLabelFormatterTests
    {
        [Theory]
        [InlineData("桥架200x100 10格", 250.0, "桥架200*100 2500mm")]
        [InlineData("桥架200*100 2500mm", 250.0, "桥架200*100 2500mm")]
        [InlineData("桥架200×100 2500mm", 250.0, "桥架200*100 2500mm")]
        [InlineData("桥架200*100 10格", 250.0, "桥架200*100 2500mm")]
        public void TryNormalize_UsesMillimetreLabelsAndMigratesLegacyGrids(
            string source, double mmPerGrid, string expected)
        {
            Assert.True(BridgeLabelFormatter.TryNormalize(source, mmPerGrid,
                out string actual));
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void NormalizeOrDefault_FallsBackForInvalidText()
        {
            Assert.Equal("桥架200*100 2500mm", BridgeLabelFormatter.NormalizeOrDefault(
                "桥架型号 mm", 250.0));
        }

        [Fact]
        public void TryNormalize_AcceptsMillimetresWithoutConversionScale()
        {
            Assert.True(BridgeLabelFormatter.TryNormalize("桥架200*100 2500mm",
                0.0, out string actual));
            Assert.Equal("桥架200*100 2500mm", actual);
        }

        [Fact]
        public void TryMigrateLegacyGrid_ConvertsOnlyGridLabels()
        {
            Assert.True(BridgeLabelFormatter.TryMigrateLegacyGrid(
                "桥架200*100 10格", 250.0, out string migrated));
            Assert.Equal("桥架200*100 2500mm", migrated);

            Assert.False(BridgeLabelFormatter.TryMigrateLegacyGrid(
                "桥架200*100 2500mm", 250.0, out string current));
            Assert.Equal("", current);
        }

        [Fact]
        public void ReadableMigration_StillConvertsWhenTheResultIsReadable()
        {
            Assert.True(BridgeLabelFormatter.TryMigrateLegacyGridReadable(
                "桥架200*100 10格", 250.0, out string migrated));
            Assert.Equal("桥架200*100 2500mm", migrated);
        }

        [Fact]
        public void ReadableMigration_SkipsWhenTheResultWouldBeUnreadable()
        {
            // 12.5格 × 250 = 3125mm，不以 0 结尾，统计读不出。
            // 这种情况下迁移会把标注改成统计看不见的样子，所以不迁移。
            Assert.False(BridgeLabelFormatter.TryMigrateLegacyGridReadable(
                "桥架200*100 12.5格", 250.0, out string migrated));
            Assert.Equal("", migrated);
            // 保留的原写法统计照样认，数据不丢。
            Assert.True(TextParser.TryExtractBridgeLabel("桥架200*100 12.5格", 250.0,
                out string spec, out _));
            Assert.Equal("桥架200*100", spec);
        }

        [Fact]
        public void TryMigrateLegacyGrid_ItselfStillConvertsFractionalGrids()
        {
            // U1Q 的标识规范化走的是这个方法，判据不能并进去：
            // 否则配置里的 12.5格 会悄悄回退成默认规格。
            Assert.True(BridgeLabelFormatter.TryMigrateLegacyGrid(
                "桥架200*100 12.5格", 250.0, out string migrated));
            Assert.Equal("桥架200*100 3125mm", migrated);
        }

        [Theory]
        [InlineData("桥架200*100 12格", "桥架200*100 3000mm")]      // 3000 是 10 的倍数 → 迁移
        [InlineData("桥架200*100 12.5格", "桥架200*100 12.5格")]    // 3125 统计读不出 → 保留原写法
        public void NormalizeForWriting_OnlyMigratesWhenStatisticsCanStillReadIt(
            string value, string expected)
        {
            // U1Q 写入前用的就是它：写出去的长度必须下一次 U1F/U1U 还能读回来。
            Assert.Equal(expected, BridgeLabelFormatter.NormalizeForWriting(
                value, 250.0, "桥架200*100 2500mm"));
        }

        [Fact]
        public void TryMigrateLegacyGrid_RejectsInvalidScaleWithoutRewriting()
        {
            Assert.False(BridgeLabelFormatter.TryMigrateLegacyGrid(
                "桥架200*100 10格", 0.0, out string actual));
            Assert.Equal("", actual);
        }
    }
}
