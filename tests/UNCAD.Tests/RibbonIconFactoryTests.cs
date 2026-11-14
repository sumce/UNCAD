using System.Windows.Media;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.1.0.2 功能区图标统一单色（AutoCAD 原生风格浅灰 UiTheme.RibbonIcon）：
    /// 旧的“一命令一色”商务色方案（绿提交/红 PDF/橙拱桥/各档钢蓝）被用户
    /// 对照 AutoCAD 原生功能区截图否决（2026-11-15“我们的明显难看”）。
    /// 锁定：全部图标名返回同一颜色且等于 RibbonIcon 色标——任何按图标
    /// 分色的回归都会在这里被抓到。
    /// </summary>
    public sealed class RibbonIconFactoryTests
    {
        private static readonly string[] AllIconNames =
        {
            "fill", "submit", "dwg", "pdf", "layout", "merge", "settings",
            "data", "login", "about", "help", "conduit", "tray", "line3d",
            "line", "arch", "stat", "excel", "tool"
        };

        [Fact]
        public void AllIcons_UseSingleUnifiedRibbonColor()
        {
            var expected = Color.FromRgb(0xD0, 0xD0, 0xCE);
            foreach (string icon in AllIconNames)
                Assert.Equal(expected, RibbonIconFactory.IconColor(icon));
        }

        [Fact]
        public void IconSet_ParsesForBothRibbonSizes()
        {
            // 抽查管线：SVG 路径解析与 16/32 两个缓存尺寸都产出图像。
            Assert.NotNull(RibbonIconFactory.Create("fill", 32));
            Assert.NotNull(RibbonIconFactory.Create("submit", 16));
            Assert.NotNull(RibbonIconFactory.Create("tool", 32));
        }
    }
}
