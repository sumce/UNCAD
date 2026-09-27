using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>Hosts the local, read-only U1L3D scene in a restricted WebView2.</summary>
    internal sealed class U1L3DPreviewForm : DpiAwareForm
    {
        private const string VirtualHostName = "u1l3d.uncad.invalid";
        private readonly string _webRoot;
        private readonly string _sceneJson;
        private readonly WebView2 _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(11, 15, 20)
        };
        private readonly Panel _errorPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.WindowBg,
            Visible = false
        };
        private readonly Label _errorMessage = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(32),
            ForeColor = UiTheme.DangerFg
        };
        private bool _initializationStarted;
        private bool _fatal;

        public U1L3DPreviewForm(IDictionary<string, object> scene)
            : this(scene, ResolveDefaultWebRoot())
        {
        }

        internal U1L3DPreviewForm(IDictionary<string, object> scene, string webRoot)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            _sceneJson = new JavaScriptSerializer
            {
                MaxJsonLength = 16 * 1024 * 1024,
                RecursionLimit = 64
            }.Serialize(scene);
            _webRoot = ValidateWebRoot(webRoot);

            DialogLayout.Apply(this, "U1L3D 三维预览",
                new Size(1180, 760), new Size(900, 600), resizable: true);
            Name = nameof(U1L3DPreviewForm);
            ShowInTaskbar = false;

            var close = UiTheme.Button("关闭", DialogResult.Cancel);
            close.Anchor = AnchorStyles.None;
            close.Click += (sender, args) => Close();
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(24)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            layout.Controls.Add(_errorMessage, 0, 0);
            layout.Controls.Add(close, 0, 1);
            _errorPanel.Controls.Add(layout);

            Controls.Add(_webView);
            Controls.Add(_errorPanel);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_initializationStarted) return;
            _initializationStarted = true;
            try
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UNCAD", "WebView2");
                Directory.CreateDirectory(userDataFolder);
                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                if (IsDisposed || Disposing) return;
                await _webView.EnsureCoreWebView2Async(environment);
                if (IsDisposed || Disposing) return;
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
            core.ProcessFailed += (sender, args) => ShowFatalError(
                "3D 渲染进程异常终止：" + args.ProcessFailedKind);
            core.WebMessageReceived += OnWebMessageReceived;
            core.SetVirtualHostNameToFolderMapping(VirtualHostName, _webRoot,
                CoreWebView2HostResourceAccessKind.Deny);
            _webView.Source = new Uri("https://" + VirtualHostName + "/index.html");
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
                    Log.Warn("U1L3D 页面渲染失败: " + Convert.ToString(
                        message.TryGetValue("message", out object detail) ? detail : ""));
            }
            catch (Exception ex)
            {
                Log.Warn("U1L3D 页面消息读取失败: " + ex.Message);
            }
        }

        private void ShowFatalError(string message)
        {
            if (_fatal || IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(ShowFatalError), message);
                return;
            }
            _fatal = true;
            _errorMessage.Text = message;
            _webView.Visible = false;
            _errorPanel.Visible = true;
            _errorPanel.BringToFront();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_webView.CoreWebView2 != null)
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _webView.Dispose();
            base.OnFormClosed(e);
        }

        private static string ResolveDefaultWebRoot()
        {
            string assemblyDirectory = Path.GetDirectoryName(
                typeof(U1L3DPreviewForm).Assembly.Location);
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
