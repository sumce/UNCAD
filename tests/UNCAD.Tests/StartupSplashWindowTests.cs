using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.1.0.1 起启动动画单路径化（原生粒子唯一路径）：宿主限制、像素级覆盖
    /// 区域、点击/按键跳过直达关闭、品牌标语与版本说明、自动关闭时序。
    /// 2026-11-15 用户报告三项（每次启动换手闪烁/点击跳过失效/要展示版本标语）
    /// 的根因都是 3.0~3.1.0 的 WebView2+原生双路径换手机制，见
    /// StartupSplashWindow 类头注释。
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
        public void Splash_UsesSingleNativeAnimationAtRequestedScreenBounds()
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

                    // 3.1.0.1 单路径：原生动画是唯一渲染路径，不再挂 WebView2
                    // 控件（双路径换手 = 每次启动的可见闪烁与点击跳过失效）。
                    Assert.Empty(FindAll<Microsoft.Web.WebView2.Wpf.WebView2>(window));
                    StartupSplashCanvas canvas = Assert.Single(
                        FindAll<StartupSplashCanvas>(window));
                    Assert.Equal(2400, canvas.ParticleCount);

                    // 内容 Grid 带背景画刷：整窗任意位置可命中，点击跳过不依赖落点。
                    var grid = Assert.IsType<Grid>(window.Content);
                    Assert.NotNull(grid.Background);

                    // 5 秒自动关闭时序不变。
                    Assert.Equal(5000, StartupSplashWindow.AutoCloseMilliseconds);
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
        public void Splash_ClickOrKey_SkipsImmediately()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // 鼠标点击 → 立即关闭（无浏览器层中转）。
                    var byMouse = new StartupSplashWindow(new Rectangle(0, 0, 400, 300));
                    bool mouseClosed = false;
                    byMouse.Closed += (s, e) => mouseClosed = true;
                    byMouse.Show();
                    Pump(2);
                    byMouse.RaiseEvent(new MouseButtonEventArgs(
                        Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseDownEvent });
                    Assert.True(mouseClosed, "点击应立即关闭启动动画");

                    // 任意按键 → 立即关闭。
                    var byKey = new StartupSplashWindow(new Rectangle(0, 0, 400, 300));
                    bool keyClosed = false;
                    byKey.Closed += (s, e) => keyClosed = true;
                    byKey.Show();
                    Pump(2);
                    byKey.RaiseEvent(new KeyEventArgs(
                        Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(byKey), 0, Key.Escape)
                    { RoutedEvent = UIElement.PreviewKeyDownEvent });
                    Assert.True(keyClosed, "按键应立即关闭启动动画");
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void Splash_ShowsBrandMottoAndVersionCaption()
        {
            // 3.1 版本标语：动画尾段在版本号下方淡入（DrawBrandMotto）。
            Assert.Equal("如切如磋，如琢如磨", StartupSplashCanvas.BrandMotto);
            string source = File.ReadAllText(RepoFile(
                "src", "UNCAD", "UI", "Wpf", "StartupSplashWindow.cs"));
            Assert.Contains("DrawBrandMotto", source);
            Assert.Contains("UNCAD Pro · v\" + ProductMetadata.DetailedVersionText", source);
        }

        [Fact]
        public void Splash_HasNoWebTakeoverSwitch_Anymore()
        {
            // 锁死移除：WebView2 换手机制（环境创建/导航/接管期限/回退）不得
            // 回流——它就是 2026-11-15 报告的"每次启动闪烁一下 + 卡屏"根因。
            string source = File.ReadAllText(RepoFile(
                "src", "UNCAD", "UI", "Wpf", "StartupSplashWindow.cs"));
            Assert.DoesNotContain("CoreWebView2", source);
            Assert.DoesNotContain("EnsureCoreWebView2Async", source);
            Assert.DoesNotContain("WebTakeover", source);
            Assert.DoesNotContain("UseNativeFallback", source);
            Assert.DoesNotContain("StartupSplash.html", source);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
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
