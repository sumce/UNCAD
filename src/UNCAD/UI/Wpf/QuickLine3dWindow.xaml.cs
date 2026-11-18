using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 本地只读 U13D 场景宿主（3.0 WPF 外壳，D-038 P5；WebView2 内容按 D-025 维持）。
    /// 行为与迁移前的 WinForms 预览窗逐条对齐：虚拟主机只映射本地 Web/QuickLine3D
    /// 目录，非本地页面导航一律取消；不写 CAD、不刷新工作簿、不访问网络。
    /// </summary>
    public sealed partial class QuickLine3dWindow : FluentWindow
    {
        private const string VirtualHostName = "u1l3d.uncad.invalid";
        private readonly string _webRoot;
        private readonly string _sceneJson;
        private readonly WebView2 _webView = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(11, 15, 20)
        };
        private bool _initializationStarted;
        private bool _fatal;

        /// <summary>
        /// 无头测试用：false 时不自动初始化 WebView2（避免无头环境装运行时/真开页面）。
        /// 真机调用方永远不要设置。
        /// </summary>
        internal bool AutoInitializeWebView { get; set; } = true;

        /// <summary>等价于旧 Form 的 IsDisposed：Closed 之后异步回调一律退出。</summary>
        private bool _closed;

        public QuickLine3dWindow(IDictionary<string, object> scene)
            : this(scene, ResolveDefaultWebRoot())
        {
        }

        internal QuickLine3dWindow(IDictionary<string, object> scene, string webRoot)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            _sceneJson = new JavaScriptSerializer
            {
                MaxJsonLength = 16 * 1024 * 1024,
                RecursionLimit = 64
            }.Serialize(scene);
            _webRoot = ValidateWebRoot(webRoot);

            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            BrowserHost.Children.Add(_webView);
            Loaded += (sender, args) => BeginInitialize();
            Closed += OnClosed;
        }

        private async void BeginInitialize()
        {
            if (_initializationStarted) return;
            _initializationStarted = true;
            if (!AutoInitializeWebView) return;
            try
            {
                // 3.3.7.2：环境+控制器统一入口（超时判定数据目录被僵尸锁住，
                // 本会话自动换独立目录）。
                await SharedWebView2Environment.EnsureControllerAsync(_webView);
                if (_closed) return;
                ConfigureBrowser(_webView.CoreWebView2);
            }
            catch (Exception ex)
            {
                ShowFatalError("3D 预览启动失败：" + ex.Message);
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
            // 3.3.8：three.js vendor 已随 U1L3D 摘除搬到 QuickLineOrbit——
            // 主机根映射到 Web 父目录（与 U1U 预览同法），页面从兄弟目录引库。
            core.SetVirtualHostNameToFolderMapping(VirtualHostName,
                System.IO.Path.GetDirectoryName(_webRoot),
                CoreWebView2HostResourceAccessKind.Deny);
            _webView.Source = new Uri("https://" + VirtualHostName
                + "/QuickLine3D/index.html");
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
            if (_fatal) return;
            try
            {
                var message = new JavaScriptSerializer().DeserializeObject(e.WebMessageAsJson)
                    as Dictionary<string, object>;
                if (message == null || !message.TryGetValue("type", out object rawType)) return;
                string type = Convert.ToString(rawType);
                if (string.Equals(type, "ready", StringComparison.Ordinal))
                    _webView.CoreWebView2.PostWebMessageAsJson(_sceneJson);
                else if (string.Equals(type, "close", StringComparison.Ordinal))
                    Close();
                else if (string.Equals(type, "renderError", StringComparison.Ordinal))
                    Log.Warn("U13D 页面渲染失败: " + Convert.ToString(
                        message.TryGetValue("message", out object detail) ? detail : ""));
            }
            catch (Exception ex)
            {
                Log.Warn("U13D 页面消息读取失败: " + ex.Message);
            }
        }

        /// <summary>致命错误路径：WebView2 整块替换为错误说明（无头测试直接驱动验证）。</summary>
        internal void ShowFatalError(string message)
        {
            if (_fatal || _closed) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    (Action<string>)ShowFatalError, DispatcherPriority.Normal, message);
                return;
            }
            _fatal = true;
            ErrorMessage.Text = message;
            _webView.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            System.Windows.Controls.Panel.SetZIndex(ErrorPanel, 1);
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _closed = true;
            if (_webView.CoreWebView2 != null)
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _webView.Dispose();
        }

        private static string ResolveDefaultWebRoot()
        {
            string assemblyDirectory = Path.GetDirectoryName(
                typeof(QuickLine3dWindow).Assembly.Location);
            return Path.Combine(assemblyDirectory ?? "", "Web", "QuickLine3D");
        }

        private static string ValidateWebRoot(string webRoot)
        {
            if (string.IsNullOrWhiteSpace(webRoot))
                throw new ArgumentException("3D 页面目录不能为空。", nameof(webRoot));
            string fullPath = Path.GetFullPath(webRoot);
            if (!Directory.Exists(fullPath)
                || !File.Exists(Path.Combine(fullPath, "index.html")))
                throw new DirectoryNotFoundException("找不到 U1L3D 页面资源：" + fullPath);
            return fullPath;
        }
    }
}
