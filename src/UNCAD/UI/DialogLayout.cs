using System;
using System.Drawing;
using System.Windows.Forms;

namespace UNCAD.UI
{
    internal static class DialogLayout
    {
        public static void Apply(Form form, string title, Size clientSize,
            Size minimumSize, bool resizable = true)
        {
            form.Text = title;
            form.StartPosition = FormStartPosition.CenterParent;
            form.AutoScaleMode = AutoScaleMode.Dpi;
            // AutoScaleDimensions 缺省为空时 WinForms 不执行任何缩放；显式给出
            // 96-DPI 设计基准，让控件边界、窗体尺寸和内边距按实际 DPI 缩放。
            form.AutoScaleDimensions = new SizeF(96F, 96F);
            form.Font = UiTheme.FontBody;
            form.BackColor = UiTheme.WindowBg;
            form.ClientSize = clientSize;
            form.MinimumSize = minimumSize;
            form.Padding = UiTheme.FormPadding;
            form.FormBorderStyle = resizable
                ? FormBorderStyle.Sizable
                : FormBorderStyle.FixedDialog;
            form.MaximizeBox = resizable;
            form.MinimizeBox = false;

            // 设计尺寸在高 DPI 下可能超过当前显示器；在布局完成后压回工作区，
            // 并保留 DpiChanged 入口，避免拖到另一块屏幕后再次被裁剪。
            Rectangle lastWorkingArea = Rectangle.Empty;
            bool fitting = false;
            Action<bool> fit = reposition =>
            {
                if (fitting || form.IsDisposed) return;
                fitting = true;
                try { lastWorkingArea = FitToWorkingArea(form, reposition); }
                finally { fitting = false; }
            };
            form.Load += (sender, args) => fit(false);
            form.Shown += (sender, args) => fit(true);
            form.DpiChanged += (sender, args) => RefitAfterDpiChange(form, fit);
            form.Move += (sender, args) =>
            {
                if (fitting || !form.IsHandleCreated) return;
                if (Screen.FromHandle(form.Handle).WorkingArea != lastWorkingArea)
                    fit(true);
            };
        }

        internal static Size FitSize(Size desired, Size minimum, Size workingArea,
            int margin = 8)
        {
            int maxWidth = Math.Max(1, workingArea.Width - margin * 2);
            int maxHeight = Math.Max(1, workingArea.Height - margin * 2);
            int minWidth = Math.Min(Math.Max(0, minimum.Width), maxWidth);
            int minHeight = Math.Min(Math.Max(0, minimum.Height), maxHeight);
            return new Size(
                Math.Max(minWidth, Math.Min(Math.Max(0, desired.Width), maxWidth)),
                Math.Max(minHeight, Math.Min(Math.Max(0, desired.Height), maxHeight)));
        }

        private static void RefitAfterDpiChange(Form form, Action<bool> fit)
        {
            if (form.IsDisposed || !form.IsHandleCreated) return;
            try
            {
                form.BeginInvoke((MethodInvoker)(() =>
                {
                    if (!form.IsDisposed) fit(true);
                }));
            }
            catch (InvalidOperationException) { }
        }

        private static Rectangle FitToWorkingArea(Form form, bool reposition)
        {
            if (form.IsDisposed) return Rectangle.Empty;
            Rectangle area = form.IsHandleCreated
                ? Screen.FromHandle(form.Handle).WorkingArea
                : Screen.PrimaryScreen.WorkingArea;
            Size available = new Size(Math.Max(1, area.Width - 16),
                Math.Max(1, area.Height - 16));
            Size minimum = form.MinimumSize;
            Size fitted = FitSize(form.Size, minimum, new Size(area.Width, area.Height));

            // MinimumSize 是窗口外框尺寸，不能让它把 200% 窗口锁在屏幕之外。
            form.MinimumSize = new Size(Math.Min(minimum.Width, available.Width),
                Math.Min(minimum.Height, available.Height));
            if (form.Size != fitted) form.Size = fitted;

            if (reposition)
            {
                int left = Math.Max(area.Left, Math.Min(form.Left, area.Right - form.Width));
                int top = Math.Max(area.Top, Math.Min(form.Top, area.Bottom - form.Height));
                if (form.Left != left || form.Top != top)
                    form.Location = new Point(left, top);
            }
            return area;
        }
    }
}
