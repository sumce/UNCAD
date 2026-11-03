using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UNCAD.UiPreview
{
    /// <summary>用 GDI 屏幕复制抓窗口的真实合成画面（含 Mica/半透明 Fluent 材质）。</summary>
    internal static class ScreenCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public static void CaptureWindow(Window window, string path)
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero)
            {
                helper.EnsureHandle();
            }
            SetForegroundWindow(helper.Handle);
            ThreadSleep(200);

            RECT rect;
            if (!GetWindowRect(helper.Handle, out rect))
            {
                return;
            }
            var width = Math.Max(1, rect.Right - rect.Left);
            var height = Math.Max(1, rect.Bottom - rect.Top);

            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0,
                    new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void ThreadSleep(int ms)
        {
            System.Threading.Thread.Sleep(ms);
        }
    }
}
