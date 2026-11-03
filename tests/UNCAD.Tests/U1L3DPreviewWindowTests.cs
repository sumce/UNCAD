using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF U1L3D 预览外壳（D-038 P5；内容按 D-025 维持 WebView2）：
    /// 窗口结构、目录校验与致命错误降级路径。
    ///
    /// 无头环境无法保证 WebView2 运行时/本地页面渲染，因此
    /// AutoInitializeWebView=false 只测外壳；虚拟主机映射、导航白名单、
    /// 场景消息（ready/close/renderError）行为留真机回归。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class U1L3DPreviewWindowTests
    {
        [Fact]
        public void Ctor_ValidatesSceneAndWebRoot()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // 场景与目录校验都在 InitializeComponent 前抛出，
                    // 但字段初始化器已创建 WebView2 控件——仍需 STA。
                    Assert.Throws<ArgumentNullException>(
                        () => new U1L3DPreviewWindow(null, "unused"));
                    Assert.Throws<ArgumentException>(
                        () => new U1L3DPreviewWindow(Scene(), " "));
                    Assert.Throws<DirectoryNotFoundException>(
                        () => new U1L3DPreviewWindow(Scene(),
                            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void Window_ShellShowsReadOnlyNoticeAndCloseButton()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (TempDir temp = TempDir.Create())
                    {
                        File.WriteAllText(
                            Path.Combine(temp.Path, "index.html"), "<html></html>");
                        var window = NewWindow(temp.Path);
                        window.Show();
                        Pump(3);

                        // 主题按窗口合并（D-038：绝不进 Application 级资源）。
                        Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                            "Fluent 主题字典必须按窗口合并");
                        // 只读外壳：不进任务栏，关闭按钮可 Esc。
                        Assert.False(window.ShowInTaskbar);
                        Assert.Equal("关闭", window.CloseButton.Content);
                        Assert.True(window.CloseButton.IsCancel,
                            "关闭必须是取消按钮（Esc 触发）");
                        // WebView2 已挂载且错误面板默认隐藏。
                        Assert.True(window.BrowserHost.Children
                            .OfType<Microsoft.Web.WebView2.Wpf.WebView2>().Any(),
                            "WebView2（WPF 控件）必须挂在外壳里");
                        Assert.Equal(Visibility.Collapsed, window.ErrorPanel.Visibility);
                        window.Close();
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void FatalError_ReplacesWebViewWithMessageAndIgnoresFurtherCalls()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (TempDir temp = TempDir.Create())
                    {
                        File.WriteAllText(
                            Path.Combine(temp.Path, "index.html"), "<html></html>");
                        var window = NewWindow(temp.Path);
                        window.Show();
                        Pump(2);

                        window.ShowFatalError("3D 预览已阻止非本地页面导航。");
                        Pump(1);
                        Assert.Equal(Visibility.Visible, window.ErrorPanel.Visibility);
                        Assert.Equal("3D 预览已阻止非本地页面导航。",
                            window.ErrorMessage.Text);

                        // 致命错误只报一次：后续异常不得覆盖已有说明。
                        window.ShowFatalError("3D 渲染进程异常终止：BrowserProcessExited");
                        Pump(1);
                        Assert.Equal("3D 预览已阻止非本地页面导航。",
                            window.ErrorMessage.Text);
                        window.Close();
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        /// <summary>调用点契约：U1L3D 入口只允许 3.0 WPF 外壳，旧 WinForms 窗已删除。</summary>
        [Fact]
        public void PreviewEntryPoints_UseWpfWindowOnly()
        {
            string feature = File.ReadAllText(FindSource(
                "src", "UNCAD", "Features", "Unl", "QuickLine3dPreviewFeature.cs"));
            Assert.Contains("U1L3DPreviewWindow", feature);
            Assert.DoesNotContain("U1L3DPreviewForm", feature);

            string shell = File.ReadAllText(FindSource(
                "src", "UNCAD", "UI", "Wpf", "U1L3DPreviewWindow.xaml.cs"));
            // D-025：本地资源必须走虚拟主机映射，且访问级别为 Deny。
            Assert.Contains("SetVirtualHostNameToFolderMapping", shell);
            Assert.Contains("CoreWebView2HostResourceAccessKind.Deny", shell);
            Assert.DoesNotContain("U1L3DPreviewForm", shell);
        }

        private static U1L3DPreviewWindow NewWindow(string webRoot)
            // 无头测试：只构造外壳，不初始化 WebView2、不加载本地页面。
            => new U1L3DPreviewWindow(Scene(), webRoot)
            {
                AutoInitializeWebView = false
            };

        private static Dictionary<string, object> Scene()
            => new Dictionary<string, object> { ["type"] = "initialize" };

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

        private static string FindSource(params string[] parts)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName,
                    Path.Combine(parts));
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("找不到源文件：" + string.Join("/", parts));
        }

        private sealed class TempDir : IDisposable
        {
            public string Path { get; }
            private TempDir(string path) { Path = path; }
            public static TempDir Create()
            {
                var temp = new TempDir(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "u1l3d-window-" + Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(temp.Path);
                return temp;
            }
            public void Dispose()
            {
                try { Directory.Delete(Path, true); } catch { }
            }
        }
    }
}
