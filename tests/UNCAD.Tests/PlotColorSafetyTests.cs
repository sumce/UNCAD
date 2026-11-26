using UNCAD.Core.Dwg;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.8.0.10（D-054 Amendment）：出图颜色安全判据——真彩白与 255 号白在
    /// 白纸上打印不可见；7 号色是「黑/白自适应」，白纸出图变黑，不算。
    /// </summary>
    public sealed class PlotColorSafetyTests
    {
        [Fact]
        public void WhiteAci_Is255Only()
        {
            Assert.True(PlotColorSafety.IsWhiteAci(PlotColorSafety.WhiteColorIndex));
            Assert.True(PlotColorSafety.IsWhiteAci(255));
            Assert.False(PlotColorSafety.IsWhiteAci(7));
            Assert.False(PlotColorSafety.IsWhiteAci(254));
            Assert.False(PlotColorSafety.IsWhiteAci(0));
        }

        [Fact]
        public void TrueWhite_IsPureWhiteOnly()
        {
            Assert.True(PlotColorSafety.IsTrueWhite(0xFFFFFF));
            Assert.False(PlotColorSafety.IsTrueWhite(0xFEFEFE));
            Assert.False(PlotColorSafety.IsTrueWhite(0x000000));
        }
    }
}
