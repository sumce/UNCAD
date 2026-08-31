using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UNCAD.UI
{
    internal sealed class BrandMarkControl : Control
    {
        public BrandMarkControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            Width = 126;
            Height = 42;
            // WinForms does not support transparent backgrounds on every AutoCAD host control.
            BackColor = UiTheme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var blue = new SolidBrush(UiTheme.BrandBlue))
            using (var orange = new SolidBrush(UiTheme.BrandOrange))
            using (var white = new SolidBrush(UiTheme.Surface))
            using (var ink = new SolidBrush(UiTheme.TextPrimary))
            using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
            using (var markFont = new Font("Segoe UI", 16f, FontStyle.Bold))
            {
                g.FillRectangle(blue, 2, 7, 28, 28);
                g.FillRectangle(orange, 2, 30, 28, 5);
                g.DrawString("U", markFont, white, 7, 8);
                g.DrawString("UNSIAO", font, ink, 38, 12);
            }
        }
    }
}
