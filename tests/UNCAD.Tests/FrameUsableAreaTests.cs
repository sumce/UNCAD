using System.Collections.Generic;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.2.0.3 图框可用绘图区（D-053 修订续）：铺满全框会把草图压到框内
    /// 清单表/标题栏上（用户现场报告"超了"）——可用区 = 边界内缩并扣除
    /// 表格占位：整列表裁横向、横条表裁纵向、四角小块不裁、裁空回退 null。
    /// </summary>
    public sealed class FrameUsableAreaTests
    {
        private static FrameRectangle Rect(double x1, double y1, double x2, double y2)
            => new FrameRectangle("r", x1, y1, x2, y2);

        [Fact]
        public void RightColumnTable_IsExcludedFromUsableArea()
        {
            // 75000×7500 图框，右侧 15000 宽清单表（整列高）：可用区只留左段。
            var target = FrameUsableArea.Compute(Rect(0, 0, 75000, 7500),
                new List<FrameRectangle> { Rect(60000, 0, 75000, 7500) }, 300.0);

            Assert.NotNull(target);
            Assert.Equal(60000 - 300 - 300, target.Width, 0);   // 59400
            Assert.Equal(7500 - 600, target.Height, 0);
            Assert.Equal((300 + 59700) / 2d, target.CentreX, 0); // 居中于扣除后的区
        }

        [Fact]
        public void BottomStripTable_TrimsHeightOnly()
        {
            var target = FrameUsableArea.Compute(Rect(0, 0, 75000, 7500),
                new List<FrameRectangle> { Rect(0, 0, 40000, 800) }, 300.0);

            Assert.NotNull(target);
            Assert.Equal(75000 - 600, target.Width, 0);
            Assert.Equal(7500 - 800 - 600, target.Height, 0);
        }

        [Fact]
        public void CornerBlock_DoesNotTrim()
        {
            // 四角小块（不够高也不够宽）：不裁——裁了可用区被削成小角。
            var target = FrameUsableArea.Compute(Rect(0, 0, 75000, 7500),
                new List<FrameRectangle> { Rect(70000, 6500, 75000, 7500) }, 300.0);

            Assert.NotNull(target);
            Assert.Equal(75000 - 600, target.Width, 0);
            Assert.Equal(7500 - 600, target.Height, 0);
        }

        [Fact]
        public void DegenerateFrameOrFullTable_ReturnsNull()
        {
            Assert.Null(FrameUsableArea.Compute(null, null, 300.0));
            // 表占满整框 → 裁空 → null（回退旧行为：原图包络内重排）。
            Assert.Null(FrameUsableArea.Compute(Rect(0, 0, 2000, 2000),
                new List<FrameRectangle> { Rect(0, 0, 2000, 2000) }, 300.0));
            // 图框比内缩小还小 → null。
            Assert.Null(FrameUsableArea.Compute(Rect(0, 0, 500, 500), null, 300.0));
        }
    }
}
