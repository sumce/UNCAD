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
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var blue = new SolidBrush(Color.FromArgb(0, 112, 173)))
            using (var orange = new SolidBrush(Color.FromArgb(236, 126, 34)))
            using (var white = new SolidBrush(Color.White))
            using (var ink = new SolidBrush(Color.FromArgb(35, 43, 52)))
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
