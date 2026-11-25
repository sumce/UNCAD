using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UNCAD.Core.QuickLine;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 3.8.0.5（D-054/D-059 修订）：U1U 确认窗左侧 3D 预览宿主。
    ///
    /// 两层结构，**任何情况下都有画面**（用户口径「不要踩之前的无法显示的坑」）：
    ///  · 底层 = 现有 WPF 渲染器 <see cref="QuickLineOrbitPreviewControl"/>：
    ///    每次 SetModel 同步建场景、立即可见——WebView2 冷启动（6~33 s）或
    ///    初始化失败时看到的是它，不是白屏/空舱。
    ///  · 顶层 = WebView2 + three.js（本页 Web/QuickLineOrbit，本地虚拟主机，
    ///    自带 vendor）。以 1×1 尺寸经历 Loaded（3.3.6.1 教训：Collapsed 起步
    ///    会白屏），后台初始化；页面发 ready 后喂信封、再铺满盖住 WPF 层。
    ///  · 页面/内核任何异常（运行时缺失、30 s 控制器超时、导航失败、渲染进程
    ///    终止、页面 renderError）→ 收起 WebView2、亮出 WPF 场景和一行原因。
    ///
    /// 安全口径沿用 D-025 / D-054 3.4.0.4：虚拟主机只映射程序目录的 Web 目录，
    /// 非本地导航一律取消，无宿主对象/下载/新窗口/权限，页面只读、不联网。
    /// WebV（独立 WebView2 进程 UNCAD.Preview.exe）保持删除，不在此列。
    /// </summary>
    public sealed class QuickLineOrbitWebHost : Grid
    {
        private const string VirtualHostName = "u1orbit.uncad.invalid";

        private static readonly Brush BannerBackground =
            CreateFrozenBrush(0xD9, 0x3A, 0x2A, 0x12);
        private static readonly Brush BannerForeground =
            CreateFrozenBrush(0xFF, 0xF2, 0xC9, 0x4C);

        private readonly QuickLineOrbitPreviewControl _fallback =
            new QuickLineOrbitPreviewControl();
        private readonly WebView2 _webView;
        private readonly TextBlock _bannerText;
        private readonly Border _banner;
        private string _pendingJson;
        private bool _initializationStarted;
        private bool _loadedSeen;
        private bool _pageReady;
        private bool _pageFatal;
        private bool _closed;
        private string _webRoot;

        /// <summary>
        /// 无头测试用：false 时不初始化 WebView2（不装运行时也不开页面）；
        /// 生产调用方永不设置。
        /// </summary>
        internal static bool DefaultAutoInitialize { get; set; } = true;

        /// <summary>页面目录覆盖（测试宿主专用；生产按安装布局解析）。</summary>
        internal string WebRootOverride { get; set; }

        public QuickLineOrbitWebHost()
        {
            ClipToBounds = true;
            Children.Add(_fallback);

            // 1×1 可见起步：不覆盖 WPF 画面，又能正常经历 Loaded 完成视觉绑定。
            _webView = new WebView2
            {
                Width = 1.0,
                Height = 1.0,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                DefaultBackgroundColor =
                    System.Drawing.Color.FromArgb(0x0D, 0x13, 0x1A)
            };
            Children.Add(_webView);

            _bannerText = new TextBlock
            {
                Foreground = BannerForeground,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };
            _banner = new Border
            {
                Background = BannerBackground,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(8, 8, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                MaxWidth = 760.0,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Child = _bannerText
            };
            Children.Add(_banner);

            Loaded += (sender, args) =>
            {
                _loadedSeen = true;
                if (_fallback.Model != null) BeginInitialize();
            };
            Unloaded += OnUnloaded;
        }

        // ---- 与 QuickLineOrbitPreviewControl 同形的对外 API ----------------------

        public QuickLineOrbitPreviewModel Model => _fallback.Model;

        internal QuickLineOrbitPreviewEnvelope CurrentEnvelope =>
            _fallback.CurrentEnvelope;

        internal int BuiltSegmentLineCount => _fallback.BuiltSegmentLineCount;
        internal int BuiltLabelCount => _fallback.BuiltLabelCount;
        internal int BuiltColumnGridLineCount => _fallback.BuiltColumnGridLineCount;
        internal int BuiltAxisTickCount => _fallback.BuiltAxisTickCount;
        internal int BuiltLetterLabelCount => _fallback.BuiltLetterLabelCount;
        internal int BuiltEndpointPinCount => _fallback.BuiltEndpointPinCount;
        internal int BuiltTrayChannelCount => _fallback.BuiltTrayChannelCount;
        internal int BuiltConduitChannelCount => _fallback.BuiltConduitChannelCount;
        internal int BuiltFloorCount => _fallback.BuiltFloorCount;
        internal bool ColumnGridEnabled => _fallback.ColumnGridEnabled;

        /// <summary>
        /// 切换场景（含 null=提示文字）。WPF 场景同步建好立即可见；页面就绪时
        /// 信封锁在同一处构建，序列化后投递（PascalCase 键名＝C#/JS 契约）。
        /// </summary>
        public void SetModel(QuickLineOrbitPreviewModel model)
        {
            _fallback.SetModel(model);
            _pendingJson = BuildInitializePayload(_fallback.CurrentEnvelope);
            if (_pageFatal || _closed) return;
            if (_pageReady)
            {
                DeliverEnvelope();
                ApplyLayerVisibility();
            }
            else if (model != null && _loadedSeen)
            {
                BeginInitialize();
            }
        }

        /// <summary>复位视角（WPF 层与页面同时）。</summary>
        internal void ResetCameraView()
        {
            _fallback.ResetCameraView();
            PostJson(new JavaScriptSerializer().Serialize(
                new Dictionary<string, object>
                {
                    { "type", "resetCamera" }, { "schemaVersion", 1 }
                }));
        }

        /// <summary>轴位开关（WPF 层与页面同时）。</summary>
        internal void ToggleColumnGrid()
        {
            _fallback.ToggleColumnGrid();
            PostJson(new JavaScriptSerializer().Serialize(
                new Dictionary<string, object>
                {
                    { "type", "setColumnGrid" },
                    { "schemaVersion", 1 },
                    { "enabled", _fallback.ColumnGridEnabled }
                }));
        }

        internal string BuildIssueText(QuickLineOrbitPreviewEnvelope envelope)
            => _fallback.BuildIssueText(envelope);

        /// <summary>页面侧已降级到 WPF 渲染（测试/诊断用）。</summary>
        internal bool PageFailed => _pageFatal;

        /// <summary>页面已报到 ready（测试/诊断用）。</summary>
        internal bool PageReady => _pageReady;

        /// <summary>页面已回报首帧场景（测试/诊断用）。</summary>
        internal bool PageRendered { get; private set; }

        /// <summary>页面首帧回报的段数。</summary>
        internal int RenderedSegmentCount { get; private set; }

        /// <summary>降级原因（页面失败时显示在左上角）。</summary>
        internal string ErrorBannerText => _bannerText.Text;

        /// <summary>
        /// 页面侧致命错误：收起浏览器、亮出 WPF 场景和原因。WPF 场景自身的
        /// 构建失败由 <see cref="QuickLineOrbitPreviewControl"/> 自己处理。
        /// </summary>
        internal void ShowFatalError(string message)
        {
            if (_pageFatal || _closed) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action<string>)ShowFatalError,
                    DispatcherPriority.Normal, message);
                return;
            }
            _pageFatal = true;
            _webView.Visibility = Visibility.Collapsed;
            _bannerText.Text = message;
            _banner.Visibility = Visibility.Visible;
        }

        // ---- WebView2 初始化与消息 ----------------------------------------------

        private static string BuildInitializePayload(
            QuickLineOrbitPreviewEnvelope envelope)
        {
            var payload = new Dictionary<string, object>
            {
                { "type", "initialize" },
                { "schemaVersion", 1 },
                { "envelope", envelope }
            };
            return new JavaScriptSerializer
            {
                MaxJsonLength = 16 * 1024 * 1024,
                RecursionLimit = 64
            }.Serialize(payload);
        }

        private async void BeginInitialize()
        {
            if (_initializationStarted || _closed || _pageFatal
                || !DefaultAutoInitialize) return;
            _initializationStarted = true;
            try
            {
                _webRoot = ResolveWebRoot();
                Log.Info("[U1U] 3D 预览页面目录: " + _webRoot);
                // 共享环境：30 s 控制器超时、备用配置目录（登录页同源）。
                await SharedWebView2Environment.EnsureControllerAsync(_webView);
                if (_closed) return;
                await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    PageErrorBridgeScript);
                if (_closed) return;
                ConfigureBrowser(_webView.CoreWebView2);
                Log.Info("[U1U] 3D 预览 WebView2 内核就绪。");
            }
            catch (Exception ex)
            {
                if (_closed) return;
                Log.Warn("[U1U] 3D 预览 WebView2 启动失败（改用内置渲染）: " + ex);
                ShowFatalError("3D 预览（WebView2）启动失败，已改用内置渲染："
                    + ex.Message + "（不影响清单写入。）");
            }
        }

        /// <summary>
        /// 页面脚本错误桥：模块加载失败/运行期异常在 DevTools 关闭时看不见，
        /// 统一回传 pageError 进日志并降级到 WPF 渲染。
        /// </summary>
        private const string PageErrorBridgeScript =
            "window.addEventListener('error',function(e){try{window.chrome.webview.postMessage("
            + "{type:'pageError',schemaVersion:1,message:(e.message||'')+' @'+(e.filename||'')+':'+(e.lineno||0)}});}catch(_){}});"
            + "window.addEventListener('unhandledrejection',function(e){try{window.chrome.webview.postMessage("
            + "{type:'pageError',schemaVersion:1,message:String(e.reason)});}catch(_){}});";

        private void ConfigureBrowser(CoreWebView2 core)
        {
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsScriptEnabled = true;
            core.Settings.IsWebMessageEnabled = true;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.NewWindowRequested += (sender, args) => args.Handled = true;
            core.DownloadStarting += (sender, args) => args.Cancel = true;
            core.PermissionRequested += (sender, args) =>
                args.State = CoreWebView2PermissionState.Deny;
            core.ProcessFailed += (sender, args) =>
            {
                SharedWebView2Environment.Invalidate();
                ShowFatalError("3D 渲染进程异常终止，已改用内置渲染："
                    + args.ProcessFailedKind + "（不影响清单写入。）");
            };
            core.WebMessageReceived += OnWebMessageReceived;
            // 虚拟主机根映射到 Web 父目录：页面在 /QuickLineOrbit/，three.js
            // 用本页 vendor（同主机内，不出网）。
            core.SetVirtualHostNameToFolderMapping(VirtualHostName,
                Path.GetDirectoryName(_webRoot),
                CoreWebView2HostResourceAccessKind.Deny);
            _webView.Source = new Uri("https://" + VirtualHostName
                + "/QuickLineOrbit/index.html");
        }

        private void OnNavigationStarting(object sender,
            CoreWebView2NavigationStartingEventArgs e)
        {
            Log.Info("[U1U] 3D 预览导航开始: " + e.Uri);
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, VirtualHostName,
                    StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                ShowFatalError("3D 预览已阻止非本地页面导航，已改用内置渲染。");
            }
        }

        private void OnNavigationCompleted(object sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            Log.Info("[U1U] 3D 预览导航完成: success=" + e.IsSuccess
                + " status=" + e.WebErrorStatus);
            if (!e.IsSuccess)
            {
                ShowFatalError("3D 预览页面加载失败（" + e.WebErrorStatus
                    + "），已改用内置渲染。");
                return;
            }
        }

        private void OnWebMessageReceived(object sender,
            CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (_pageFatal || _closed) return;
            try
            {
                var message = new JavaScriptSerializer().DeserializeObject(
                    e.WebMessageAsJson) as Dictionary<string, object>;
                if (message == null
                    || !message.TryGetValue("type", out object rawType)) return;
                string type = Convert.ToString(rawType);
                if (string.Equals(type, "ready", StringComparison.Ordinal))
                {
                    _pageReady = true;
                    Log.Info("[U1U] 3D 预览页面就绪，发送场景信封。");
                    DeliverEnvelope();
                    ApplyLayerVisibility();
                }
                else if (string.Equals(type, "pageError",
                    StringComparison.Ordinal))
                {
                    string detail = message.TryGetValue("message",
                        out object rawError) ? Convert.ToString(rawError) : "";
                    Log.Warn("U1U 3D 预览页面脚本错误: " + detail);
                    ShowFatalError("3D 预览页面脚本错误，已改用内置渲染："
                        + detail + "（不影响清单写入。）");
                }
                else if (string.Equals(type, "rendered",
                    StringComparison.Ordinal))
                {
                    PageRendered = true;
                    RenderedSegmentCount = message.TryGetValue("segments",
                        out object rawSegments) && rawSegments != null
                        ? Convert.ToInt32(rawSegments) : 0;
                    Log.Info("[U1U] 3D 预览页面首帧完成：段 " + RenderedSegmentCount);
                }
                else if (string.Equals(type, "renderError",
                    StringComparison.Ordinal))
                {
                    string detail = message.TryGetValue("message",
                        out object rawDetail) ? Convert.ToString(rawDetail) : "";
                    Log.Warn("U1U 3D 预览页面渲染失败: " + detail);
                    ShowFatalError("3D 预览页面渲染失败，已改用内置渲染："
                        + detail + "（不影响清单写入。）");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("U1U 3D 预览页面消息读取失败: " + ex.Message);
            }
        }

        private void DeliverEnvelope()
        {
            if (_pendingJson == null) return;
            string json = _pendingJson;
            _pendingJson = null;
            PostJson(json);
        }

        /// <summary>
        /// 页面就绪后铺满（盖住 WPF 层）。没有场景时也保持铺满——页面自己显示
        /// 「没有可预览的路由」提示；不来回收起/展开浏览器（隐藏再显示会白一帧）。
        /// 只有页面降级时才收起（见 <see cref="ShowFatalError"/>）。
        /// </summary>
        private void ApplyLayerVisibility()
        {
            if (_pageFatal || _closed || !_pageReady) return;
            _webView.Width = double.NaN;
            _webView.Height = double.NaN;
            _webView.HorizontalAlignment = HorizontalAlignment.Stretch;
            _webView.VerticalAlignment = VerticalAlignment.Stretch;
            _webView.Visibility = Visibility.Visible;
        }

        private void PostJson(string json)
        {
            if (_closed || _pageFatal || !_pageReady
                || _webView.CoreWebView2 == null) return;
            try
            {
                _webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                Log.Warn("U1U 3D 预览消息发送失败: " + ex.Message);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            _closed = true;
            if (_webView.CoreWebView2 != null)
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _webView.Dispose();
        }

        /// <summary>页面目录：安装形态在程序集旁 Web/QuickLineOrbit。</summary>
        private string ResolveWebRoot()
        {
            string webRoot = WebRootOverride;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                string assemblyDirectory = Path.GetDirectoryName(
                    typeof(QuickLineOrbitWebHost).Assembly.Location);
                webRoot = Path.Combine(assemblyDirectory ?? "",
                    "Web", "QuickLineOrbit");
            }
            if (!Directory.Exists(webRoot)
                || !File.Exists(Path.Combine(webRoot, "index.html")))
                throw new DirectoryNotFoundException(
                    "找不到 U1U 3D 预览页面资源：" + webRoot);
            return webRoot;
        }

        private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
