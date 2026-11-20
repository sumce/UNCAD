using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace UNCAD.Preview
{
    /// <summary>Local, read-only viewer process; controller stalls cannot block AutoCAD.</summary>
    internal sealed class PreviewWindow : Window
    {
        private const string Origin = "https://uncad-preview.local/";
        private readonly Grid _host = new Grid();
        private readonly TextBlock _status = new TextBlock
        {
            Text = "正在启动 WebV 预览...",
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(32),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        private readonly string _profile;
        private WebView2 _web;
        private volatile bool _closed;
        private bool _failed;
        private long _lastUiTick = DateTime.UtcNow.Ticks;
        private readonly TaskCompletionSource<bool> _rendered = new TaskCompletionSource<bool>();

        internal PreviewWindow(string profile)
        {
            _profile = profile;
            Title = "UNCAD Pro - WebV / Three.js";
            Width = 1180;
            Height = 760;
            MinWidth = 360;
            MinHeight = 320;
            Background = new SolidColorBrush(Color.FromRgb(22, 24, 27));
            Content = _host;
            _host.Children.Add(_status);
            Loaded += OnLoaded;
            var heartbeat = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            heartbeat.Tick += (sender, args) => Interlocked.Exchange(
                ref _lastUiTick, DateTime.UtcNow.Ticks);
            heartbeat.Start();
            Closed += (sender, args) =>
            {
                _closed = true;
                heartbeat.Stop();
                _web?.Dispose();
                _rendered.TrySetResult(false);
            };
        }

        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            Loaded -= OnLoaded;
            // A separate watchdog thread also catches synchronous STA stalls.
            using (var watchdog = new Timer(_ =>
            {
                long lastTick = Interlocked.Read(ref _lastUiTick);
                if (!_closed && DateTime.UtcNow.Ticks - lastTick
                    > TimeSpan.FromSeconds(15).Ticks)
                    Environment.Exit(2);
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)))
            {
                try
                {
                    string assets = _profile;
                    if (!File.Exists(Path.Combine(assets, "index.html")))
                        throw new FileNotFoundException("WebV 页面缺失，请重新安装完整发布包。");
                    _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(22, 24, 27) };
                    _host.Children.Insert(0, _web);
                    var environment = await CoreWebView2Environment.CreateAsync(null, _profile);
                    if (_closed) return;
                    Task controller = _web.EnsureCoreWebView2Async(environment);
                    if (await Task.WhenAny(controller, Task.Delay(60000)) != controller)
                    {
                        // Late faults are observed even after the visible failure path.
                        _ = controller.ContinueWith(t => { var ignored = t.Exception; },
                            TaskContinuationOptions.OnlyOnFaulted);
                        throw new TimeoutException("WebV 启动超时，请返回 WPF 预览。");
                    }
                    await controller;
                    if (_closed || _failed) return;
                    CoreWebView2 core = _web.CoreWebView2;
                    core.Settings.AreHostObjectsAllowed = false;
                    core.Settings.AreDefaultContextMenusEnabled = false;
                    core.Settings.AreDevToolsEnabled = false;
                    core.Settings.IsStatusBarEnabled = false;
                    core.Settings.IsZoomControlEnabled = false;
                    core.SetVirtualHostNameToFolderMapping("uncad-preview.local", assets,
                        CoreWebView2HostResourceAccessKind.DenyCors);
                    core.NavigationStarting += (s, e) =>
                    {
                        if (!string.Equals(e.Uri, Origin + "index.html", StringComparison.OrdinalIgnoreCase))
                            e.Cancel = true;
                    };
                    core.NewWindowRequested += (s, e) => e.Handled = true;
                    core.PermissionRequested += (s, e) => e.State = CoreWebView2PermissionState.Deny;
                    core.DownloadStarting += (s, e) => e.Cancel = true;
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    core.WebResourceRequested += (s, e) =>
                    {
                        if (!IsLocalAsset(e.Request.Uri))
                            e.Response = environment.CreateWebResourceResponse(
                                Stream.Null, 403, "Forbidden", "Content-Type: text/plain");
                    };
                    core.ProcessFailed += (s, e) => Fail("WebV 渲染进程异常，请返回 WPF 预览。");
                    core.NavigationCompleted += (s, e) =>
                    {
                        if (!e.IsSuccess) Fail("WebV 页面加载失败：" + e.WebErrorStatus);
                    };
                    core.WebMessageReceived += (s, e) =>
                    {
                        if (_closed || _failed || !IsLocalAsset(e.Source)) return;
                        string message;
                        try { message = e.TryGetWebMessageAsString(); }
                        catch (ArgumentException) { return; }
                        if (message == "ready") core.PostWebMessageAsString("load");
                        else if (message == "scene-loaded")
                        {
                            try { File.Delete(Path.Combine(_profile, "scene.json")); }
                            catch (IOException) { }
                        }
                        else if (message == "rendered")
                        {
                            _status.Visibility = Visibility.Collapsed;
                            _rendered.TrySetResult(true);
                        }
                        else if (message == "close") Close();
                        else if (message.StartsWith("error:", StringComparison.Ordinal))
                            Fail("WebV 场景加载失败：" + message.Substring(6));
                    };
                    core.Navigate(Origin + "index.html");
                    if (await Task.WhenAny(_rendered.Task, Task.Delay(60000)) != _rendered.Task)
                        Fail("WebV 页面响应超时，请返回 WPF 预览。");
                }
                catch (Exception ex)
                {
                    if (!_closed) Fail(ex.Message + "\n可关闭此窗口，继续使用 WPF 预览。");
                }
            }
        }

        internal static bool IsLocalAsset(string address)
            => Uri.TryCreate(address, UriKind.Absolute, out Uri uri)
                && uri.Scheme == "https" && uri.Host == "uncad-preview.local"
                && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);

        private void Fail(string message)
        {
            if (_closed || _failed) return;
            _failed = true;
            // Remove the native browser HWND so it cannot cover the error text.
            if (_web != null) _web.Visibility = Visibility.Collapsed;
            _status.Text = message;
            _status.Visibility = Visibility.Visible;
            _rendered.TrySetResult(false);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string session = null;
            try
            {
                if (args.Length != 1) return 1;
                session = Path.GetFullPath(args[0]);
                string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "UNCAD", "Preview"))
                    + Path.DirectorySeparatorChar;
                if (!session.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetDirectoryName(session)
                        + Path.DirectorySeparatorChar, root,
                        StringComparison.OrdinalIgnoreCase)
                    || !Guid.TryParseExact(Path.GetFileName(session), "N", out _))
                    return 1;
                string path = Path.Combine(session, "scene.json");
                if (!File.Exists(Path.Combine(session, "index.html"))) return 1;
                var info = new FileInfo(path);
                if (info.Length > 128 * 1024 * 1024) return 1;
                int result = new Application().Run(new PreviewWindow(session));
                TryDeleteSession(session);
                return result;
            }
            catch (Exception ex)
            {
                if (session != null)
                {
                    TryDeleteSession(session);
                }
                MessageBox.Show(ex.Message, "UNCAD WebV", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 1;
            }
        }

        private static void TryDeleteSession(string session)
        {
            try { Directory.Delete(session, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
