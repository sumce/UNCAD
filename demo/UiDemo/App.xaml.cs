using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace UNCAD.UiDemo
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length > 0 && e.Args[0] == "--shots")
            {
                RunShots(e.Args.Length > 1 ? e.Args[1] : "shots");
                return;
            }

            var win = new MainWindow();
            win.Show();
        }

        /// <summary>无人值守截图模式：把每个界面 × 深/浅主题截成 PNG。
        /// 用屏幕复制而不是 RenderTargetBitmap：Fluent 的 Mica/半透明侧栏只有 DWM
        /// 合成后的真实画面才可见，RTB 会把材质层拍成透明。</summary>
        private void RunShots(string dir)
        {
            Directory.CreateDirectory(dir);
            var win = new MainWindow();
            win.Show();
            Pump(3);

            foreach (var theme in new[] { "light", "dark" })
            {
                ApplyTheme(theme);
                Pump(3);
                foreach (var key in new[] { "settings", "review", "batch" })
                {
                    win.ShowPage(key);
                    Pump(5);
                    Thread.Sleep(500);
                    Pump(3);
                    ScreenCapture.CaptureWindow(win, Path.Combine(dir, key + "-" + theme + ".png"));
                }
            }

            foreach (var theme in new[] { "light", "dark" })
            {
                ApplyTheme(theme);
                Pump(3);
                var login = new LoginWindow { Left = 480, Top = 220 };
                login.Show();
                Pump(5);
                Thread.Sleep(500);
                Pump(3);
                ScreenCapture.CaptureWindow(login, Path.Combine(dir, "login-" + theme + ".png"));
                login.Close();
            }

            win.Close();
            Shutdown();
        }

        private static void ApplyTheme(string theme)
        {
            ApplicationThemeManager.Apply(
                theme == "dark" ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(40);
            }
        }
    }
}
