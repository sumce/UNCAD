using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UNCAD.Core.QuickLine;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 3.3.5（D-054 Amendment）：U1U 确认窗左侧的 3D 路由预览——渲染内核从
    /// HelixToolkit.Wpf 换回 WebView2 + three.js（用户看过 3.3.2/3.3.4 的 Helix
    /// 预览后现场反馈「这生成的 3D 有严重问题」，D-054 3.3.2 选型记录作废）。
    /// 本控件是**哑宿主**：场景语义（柱网规划/通道双线偏移/标签锚点）全部由
    /// Core 的 QuickLineOrbitPreviewEnvelope 算好序列化成 JSON 喂给本地页面
    /// （Web/QuickLineOrbit，复用 QuickLine3D 已 vendor 的 three.js）——分层
    /// 铁律不变：窗体不重建场景。WebView2 接线与安全口径逐条对齐
    /// U1L3DPreviewWindow（D-025）：虚拟主机只映射本地 Web 目录、非本地导航
    /// 一律取消、无宿主对象/下载/权限、渲染进程异常走降级文案。
    ///
    /// 对宿主窗的 API 与 3.3.2 Helix 控件同形（SetModel/Model/柱网开关/复位），
    /// 数据级计数器改为**取自信封**（SetModel 时同步算好，不依赖 WebView2
    /// 初始化与 GPU——无头测试宿主照常断言）。WebView2 惰性初始化：首次
    /// SetModel(非 null) 才启动；初始化失败/页面异常整块降级为错误文案，
    /// 绝不炸确认窗（预览是展示件——3.3.0.1 降级口径）。
    /// </summary>
    public sealed class QuickLineWebPreviewControl : Grid
    {
        private const string VirtualHostName = "u1orbit.uncad.invalid";

        private static readonly Brush HintBrush = CreateFrozenBrush(0x8A, 0x8A, 0x8A);

        private readonly TextBlock _hintText;
        private readonly WebView2 _webView;
        private QuickLineOrbitPreviewModel _model;
        private QuickLineOrbitPreviewEnvelope _envelope;
        private string _pendingJson;
        private bool _initializationStarted;
        private bool _pageReady;
        private bool _fatal;
        private bool _closed;
        private bool _columnGridEnabled = true;
        private string _webRoot;

        /// <summary>
        /// 无头测试用：false 时不自动初始化 WebView2（避免测试宿主装运行时/
        /// 真开页面）；真机调用方永远不要设置。按新实例取值（测试集合开头置
        /// false 一次即可）。
        /// </summary>
        internal static bool DefaultAutoInitialize { get; set; } = true;

        /// <summary>
        /// 页面目录覆盖（测试宿主专用：程序集输出目录没有 Web/，指向源码树
        /// 的 Web 目录即可跑真页面；生产不设置，按安装布局解析）。
        /// </summary>
        internal string WebRootOverride { get; set; }

        /// <summary>当前预览的场景（宿主/测试可读；为 null 时显示提示）。</summary>
        public QuickLineOrbitPreviewModel Model => _model;

        // ---- 数据级计数器（取自信封；口径与 3.3.2 Helix 控件一致） --------

        internal int BuiltSegmentLineCount { get; private set; }
        internal int BuiltLabelCount { get; private set; }
        internal int BuiltColumnGridLineCount =>
            _columnGridEnabled ? _plannedColumnGridLineCount : 0;
        internal int BuiltAxisTickCount =>
            _columnGridEnabled ? _plannedAxisTickCount : 0;
        internal int BuiltLetterLabelCount =>
            _columnGridEnabled ? _plannedLetterLabelCount : 0;
        internal int BuiltEndpointPinCount { get; private set; }
        internal int BuiltTrayChannelCount { get; private set; }
        internal int BuiltConduitChannelCount { get; private set; }

        internal bool ColumnGridEnabled => _columnGridEnabled;

        private int _plannedColumnGridLineCount;
        private int _plannedAxisTickCount;
        private int _plannedLetterLabelCount;

        public QuickLineWebPreviewControl()
        {
            ClipToBounds = true;

            _hintText = new TextBlock
            {
                Text = "该图框没有可预览的带毫米标注 U1L 路由",
                Foreground = HintBrush,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            // 3.3.6.1：空态/错误态居中醒目——左上角一行小灰字在整舱留白下
            // 看起来就是"空白"（现场反馈）；居中+可换行，错误文案带指引。
            _hintText.SetValue(MarginProperty, new Thickness(24));
            _hintText.SetValue(HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            _hintText.SetValue(VerticalAlignmentProperty,
                VerticalAlignment.Center);
            Children.Add(_hintText);

            // 3.3.6.1：绝不能以 Collapsed 状态经历 Loaded——WebView2 WPF 的
            // 视觉绑定在 Loaded 时进行，Collapsed 起步的控件白屏（对照实验：
            // 同宿主里从头 Visible 的 U1L3DPreviewWindow 正常渲染深色页面）。
            // 空态/致命态用深色 DefaultBackgroundColor 兜底 + 居中提示文字。
            _webView = new WebView2
            {
                DefaultBackgroundColor =
                    System.Drawing.Color.FromArgb(0x0D, 0x13, 0x1A)
            };
            Children.Add(_webView);

            Unloaded += OnUnloaded;
        }

        /// <summary>切换场景（含 null=隐藏为提示文字）。非 null 首次调用时启动页面。</summary>
        public void SetModel(QuickLineOrbitPreviewModel model)
        {
            _model = model;
            // 3.3.6：参照层清单来自 U1SET（标签=毫米；空=不画）——UI 层读设置、
            // Core 解析、信封携带，页面只画。
            string rawLevels = Settings.Get(ConfigKeys.PreviewReferenceLevels,
                PreviewReferenceLevels.DefaultList);
            _envelope = QuickLineOrbitPreviewEnvelope.Build(model,
                PreviewReferenceLevels.Parse(rawLevels));
            RefreshDataCounters();
            _hintText.Visibility = _envelope == null
                ? Visibility.Visible
                : Visibility.Collapsed;
            var payload = new System.Collections.Generic.Dictionary<string, object>
            {
                { "type", "initialize" },
                { "schemaVersion", 1 },
                { "envelope", _envelope }
            };
            _pendingJson = new JavaScriptSerializer
            {
                MaxJsonLength = 16 * 1024 * 1024,
                RecursionLimit = 64
            }.Serialize(payload);
            if (_pageReady) DeliverEnvelope();
            else BeginInitialize();
        }

        /// <summary>复位视角（页面消息驱动；页面未就绪时静默——页面自身有复位按钮/双击）。</summary>
        internal void ResetCameraView()
        {
            PostJson(new JavaScriptSerializer().Serialize(
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { "type", "resetCamera" }, { "schemaVersion", 1 }
                }));
        }

        /// <summary>柱网显示开关（数据级计数器随动；页面侧同步）。</summary>
        internal void ToggleColumnGrid()
        {
            _columnGridEnabled = !_columnGridEnabled;
            PostJson(new JavaScriptSerializer().Serialize(
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { "type", "setColumnGrid" },
                    { "schemaVersion", 1 },
                    { "enabled", _columnGridEnabled }
                }));
        }

        // ---- 数据级计数器 -------------------------------------------------------

        private void RefreshDataCounters()
        {
            BuiltSegmentLineCount = 0;
            BuiltLabelCount = 0;
            _plannedColumnGridLineCount = 0;
            _plannedAxisTickCount = 0;
            _plannedLetterLabelCount = 0;
            BuiltEndpointPinCount = 0;
            BuiltTrayChannelCount = 0;
            BuiltConduitChannelCount = 0;
            QuickLineOrbitPreviewEnvelope envelope = _envelope;
            if (envelope == null) return;
            BuiltSegmentLineCount = envelope.Segments.Count;
            BuiltLabelCount = envelope.SegmentLabels.Count;
            _plannedColumnGridLineCount = envelope.ColumnGridLines.Count / 2;
            _plannedAxisTickCount = envelope.NumericTicks.Count;
            _plannedLetterLabelCount = envelope.LetterLabels.Count;
            BuiltEndpointPinCount = envelope.EndpointPins.Count;
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeSegment segment
                in envelope.Segments)
            {
                if (segment.Channel == null) continue;
                if (string.Equals(segment.Channel.Kind, "tray",
                        StringComparison.OrdinalIgnoreCase))
                    BuiltTrayChannelCount++;
                else if (string.Equals(segment.Channel.Kind, "conduit",
                        StringComparison.OrdinalIgnoreCase))
                    BuiltConduitChannelCount++;
            }
        }

        // ---- WebView2 初始化与消息（口径对齐 U1L3DPreviewWindow） --------------

        private async void BeginInitialize()
        {
            if (_initializationStarted || _closed || !DefaultAutoInitialize) return;
            _initializationStarted = true;
            try
            {
                _webRoot = ResolveWebRoot();
                Log.Info("[U1U] 3D 预览页面目录: " + _webRoot);
                // 3.3.6.2：进程级共享环境——每窗新建时与上一轮退场进程抢同一
                // 用户数据目录，现场实测内核就绪要 6~10s，用户确认前 3D 永远
                // 起不来；共享常驻进程 0.5s 级（2026-11-18 日志）。
                CoreWebView2Environment environment =
                    await SharedWebView2Environment.GetAsync();
                if (_closed) return;
                await _webView.EnsureCoreWebView2Async(environment);
                if (_closed) return;
                ConfigureBrowser(_webView.CoreWebView2);
                Log.Info("[U1U] 3D 预览 WebView2 内核就绪。");
            }
            catch (Exception ex)
            {
                // 窗口已关时初始化收尾摸到已销毁内核是正常竞态（现场日志
                // 曾以「启动失败」WARN 误报）：无人再看，静默退出。
                if (_closed) return;
                Log.Warn("[U1U] 3D 预览启动失败: " + ex);
                ShowFatalError("3D 预览启动失败：" + ex.Message
                    + "\n（不影响清单写入；可用 U1S/U1F 正常出清单。）");
            }
        }

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
                ShowFatalError("3D 渲染进程异常终止：" + args.ProcessFailedKind);
            };
            core.WebMessageReceived += OnWebMessageReceived;
            // 虚拟主机根映射到 Web 父目录：页面在 /QuickLineOrbit/，three.js
            // 从 ../QuickLine3D/vendor/ 引用（同主机内，不出网）。
            core.SetVirtualHostNameToFolderMapping(VirtualHostName,
                Path.GetDirectoryName(_webRoot),
                CoreWebView2HostResourceAccessKind.Deny);
            _webView.Source = new Uri("https://" + VirtualHostName
                + "/QuickLineOrbit/index.html");
        }

        private void OnNavigationStarting(object sender,
            CoreWebView2NavigationStartingEventArgs e)
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, VirtualHostName,
                    StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                ShowFatalError("3D 预览已阻止非本地页面导航。");
            }
        }

        private void OnNavigationCompleted(object sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) ShowFatalError("3D 预览页面加载失败：" + e.WebErrorStatus);
        }

        private void OnWebMessageReceived(object sender,
            CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (_fatal || _closed) return;
            try
            {
                var message = new JavaScriptSerializer().DeserializeObject(
                        e.WebMessageAsJson) as System.Collections.Generic
                    .Dictionary<string, object>;
                if (message == null
                    || !message.TryGetValue("type", out object rawType)) return;
                string type = Convert.ToString(rawType);
                if (string.Equals(type, "ready", StringComparison.Ordinal))
                {
                    _pageReady = true;
                    Log.Info("[U1U] 3D 预览页面就绪，发送场景信封。");
                    DeliverEnvelope();
                }
                else if (string.Equals(type, "renderError",
                    StringComparison.Ordinal))
                {
                    Log.Warn("U1U 3D 预览页面渲染失败: " + Convert.ToString(
                        message.TryGetValue("message", out object detail)
                            ? detail : ""));
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

        private void PostJson(string json)
        {
            if (_closed || _fatal || !_pageReady
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

        /// <summary>致命错误路径：WebView2 隐藏为提示文字（计数器不受影响）。</summary>
        internal void ShowFatalError(string message)
        {
            if (_fatal || _closed) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    (Action<string>)ShowFatalError, DispatcherPriority.Normal,
                    message);
                return;
            }
            _fatal = true;
            _hintText.Text = message;
            _hintText.Visibility = Visibility.Visible;
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
                    typeof(QuickLineWebPreviewControl).Assembly.Location);
                webRoot = Path.Combine(assemblyDirectory ?? "",
                    "Web", "QuickLineOrbit");
            }
            if (!Directory.Exists(webRoot)
                || !File.Exists(Path.Combine(webRoot, "index.html")))
                throw new DirectoryNotFoundException(
                    "找不到 U1U 3D 预览页面资源：" + webRoot);
            return webRoot;
        }

        private static Brush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
