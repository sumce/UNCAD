using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UNCAD.UI
{
    internal sealed class BrandMarkControl : Control
    {
        // 96-DPI 设计基准。控件尺寸由 WinForms 自动缩放，但 OnPaint 里的绘制
        // 几何不会跟着缩放，因此下面所有坐标与字号都按实际宽度相对基准换算，
        // 否则 150% 屏幕上商标会缩在左上角。
        private const int DesignWidth = 126;
        private const int DesignHeight = 42;

        public BrandMarkControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            Width = DesignWidth;
            Height = DesignHeight;
            // WinForms does not support transparent backgrounds on every AutoCAD host control.
            BackColor = UiTheme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = Width <= 0 ? 1f : Width / (float)DesignWidth;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var blue = new SolidBrush(UiTheme.BrandBlue))
            using (var orange = new SolidBrush(UiTheme.BrandOrange))
            using (var white = new SolidBrush(UiTheme.Surface))
            using (var ink = new SolidBrush(UiTheme.TextPrimary))
            using (var font = new Font(UiTheme.BrandFontFamily, 10f * scale, FontStyle.Bold))
            using (var markFont = new Font(UiTheme.BrandFontFamily, 16f * scale, FontStyle.Bold))
            {
                g.FillRectangle(blue, Scaled(2, scale), Scaled(7, scale),
                    Scaled(28, scale), Scaled(28, scale));
                g.FillRectangle(orange, Scaled(2, scale), Scaled(30, scale),
                    Scaled(28, scale), Scaled(5, scale));
                g.DrawString("U", markFont, white, Scaled(7, scale), Scaled(8, scale));
                g.DrawString("UNSIAO", font, ink, Scaled(38, scale), Scaled(12, scale));
            }
        }

        private static float Scaled(float designValue, float scale) => designValue * scale;
    }
}
