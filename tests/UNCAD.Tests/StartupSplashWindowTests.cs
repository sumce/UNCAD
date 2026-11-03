using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 启动动画窗（D-038，P6 瞬时提示）：宿主限制、像素级覆盖
    /// 区域、WebView2 + 原生粒子兜底、内嵌 HTML 与自动关闭时序。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class StartupSplashWindowTests
    {
        [Theory]
        [InlineData("acad", true, true)]
        [InlineData("acad", false, false)]
        [InlineData("accoreconsole", true, false)]
        [InlineData("AcCoreConsole.exe", true, false)]
        public void StartupUi_IsLimitedToInteractiveFullAutoCad(
            string processName, bool userInteractive, bool expected)
        {
            Assert.Equal(expected,
                StartupSplashWindow.SupportsHost(processName, userInteractive));
        }

        [Fact]
        public void Splash_UsesWebViewWithNativeFallbackAtRequestedScreenBounds()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var bounds = new Rectangle(12, 34, 800, 600);
                    var window = new StartupSplashWindow(bounds);
                    window.Show();
                    Pump(3);

                    Assert.Equal(WindowStyle.None, window.WindowStyle);
                    Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                    Assert.True(window.Topmost);
                    Assert.False(window.ShowInTaskbar);

                    // 覆盖区域按设备像素对齐（WPF 用 DIP，换算回像素核对）。
                    var source = PresentationSource.FromVisual(window);
                    double scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1d;
                    double scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1d;
                    Assert.True(Math.Abs(window.Left * scaleX - bounds.X) <= 1,
                        "左缘未按像素对齐");
                    Assert.True(Math.Abs(window.Top * scaleY - bounds.Y) <= 1,
                        "上缘未按像素对齐");
                    Assert.True(Math.Abs(window.Width * scaleX - bounds.Width) <= 1,
                        "宽度未按像素对齐");
                    Assert.True(Math.Abs(window.Height * scaleY - bounds.Height) <= 1,
                        "高度未按像素对齐");

                    // WebView2 是首选动画，原生粒子画布是兜底（2400 粒子布局不变）。
                    Assert.Single(FindAll<Microsoft.Web.WebView2.Wpf.WebView2>(window));
                    StartupSplashCanvas canvas = Assert.Single(
                        FindAll<StartupSplashCanvas>(window));
                    Assert.Equal(2400, canvas.ParticleCount);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void EmbeddedWebAnimation_IsHostTimedAndIncludesVersion()
        {
            string html = StartupSplashWindow.LoadSplashHtml();

            Assert.Equal(5000, StartupSplashWindow.AutoCloseMilliseconds);
            Assert.Contains("id=\"particleCanvas\"", html);
            Assert.Contains("id=\"loaderContainer\"", html);
            Assert.Contains("class=\"win-loader\"", html);
            Assert.Contains("UNCAD Pro", html);
            Assert.Contains("v" + ProductMetadata.VersionText, html);
            Assert.DoesNotContain("{{VERSION}}", html);
            Assert.DoesNotContain("window.close", html);
        }

        private static List<T> FindAll<T>(DependencyObject root) where T : class
        {
            var result = new List<T>();
            Visit(root, result);
            return result;
        }

        private static void Visit<T>(DependencyObject node, List<T> result) where T : class
        {
            if (node == null) return;
            if (node is T match) result.Add(match);
            int children = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < children; i++)
                Visit(VisualTreeHelper.GetChild(node, i), result);
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }
    }
}
