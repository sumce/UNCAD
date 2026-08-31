using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using UNCAD.Core.QuickLine;

namespace UNCAD.UI
{
    /// <summary>
    /// Modal-ready Three.js editor host. The form only edits an in-memory scene;
    /// the caller remains responsible for showing it through AutoCAD and writing
    /// accepted distances back to the drawing.
    /// </summary>
    public sealed class QuickLine3dEditorForm : Form
    {
        private const string VirtualHostName = "quickline3d.uncad.invalid";
        private static readonly IReadOnlyDictionary<string, double> NoUpdates =
            new ReadOnlyDictionary<string, double>(
                new Dictionary<string, double>(StringComparer.Ordinal));

        private readonly QuickLine3dEditorProtocol _protocol;
        private readonly string _webRoot;
        private readonly WebView2 _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(237, 241, 244)
        };
        private readonly Label _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
            ForeColor = Color.FromArgb(70, 79, 87),
            BackColor = Color.FromArgb(237, 241, 244),
            Text = "正在启动 3D 编辑器..."
        };
        private readonly Panel _errorPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(246, 247, 249),
            Visible = false
        };
        private readonly Label _errorMessage = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(32),
            ForeColor = Color.FromArgb(145, 48, 45)
        };
        private bool _initializationStarted;
        private bool _fatal;

        public QuickLine3dEditorForm(QuickLineIsometricScene scene)
            : this(scene, ResolveDefaultWebRoot())
        {
        }

        public QuickLine3dEditorForm(QuickLineIsometricScene scene, string webRoot)
        {
            _protocol = new QuickLine3dEditorProtocol(scene);
            _webRoot = ValidateWebRoot(webRoot);
            Updates = NoUpdates;

            DialogLayout.Apply(this, "U1LX 3D 距离编辑",
                new Size(1180, 760), new Size(900, 600), resizable: true);
            Name = nameof(QuickLine3dEditorForm);
            ShowInTaskbar = false;

            var close = UiTheme.Button("关闭", DialogResult.Cancel);
            close.Anchor = AnchorStyles.None;
            close.Click += (sender, args) => Close();
            var errorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(24)
            };
            errorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            errorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            errorLayout.Controls.Add(_errorMessage, 0, 0);
            errorLayout.Controls.Add(close, 0, 1);
            _errorPanel.Controls.Add(errorLayout);

            Controls.Add(_webView);
            Controls.Add(_errorPanel);
            Controls.Add(_status);
        }

        public IReadOnlyDictionary<string, double> Updates { get; private set; }
        public int Revision { get; private set; }
        public string SessionId => _protocol.SessionId;

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_initializationStarted) return;
            _initializationStarted = true;

            try
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UNSIAO", "UNCAD", "WebView2");
                Directory.CreateDirectory(userDataFolder);
                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                if (IsDisposed || Disposing) return;
                await _webView.EnsureCoreWebView2Async(environment);
                if (IsDisposed || Disposing) return;
                ConfigureBrowser(_webView.CoreWebView2);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowFatalError("未检测到 Microsoft Edge WebView2 Runtime。请安装运行时后重新执行 U1LX。");
            }
            catch (UnauthorizedAccessException ex)
            {
                ShowFatalError("无法创建 3D 编辑器用户数据目录：" + ex.Message);
            }
            catch (Exception ex)
            {
                ShowFatalError("3D 编辑器启动失败：" + ex.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK)
            {
                DialogResult = DialogResult.Cancel;
                Updates = NoUpdates;
            }
            base.OnFormClosing(e);
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
            core.ProcessFailed += (sender, args) => BeginFatalError(
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
                BeginFatalError("3D 编辑器阻止了非本地页面导航。");
            }
        }

        private void OnNavigationCompleted(object sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                ShowFatalError("3D 编辑器页面加载失败：" + e.WebErrorStatus);
                return;
            }
            if (!_protocol.IsReady)
                _status.Text = "页面已加载，正在建立数据会话...";
        }

        private void OnWebMessageReceived(object sender,
            CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (_fatal) return;
            string json;
            try
            {
                json = e.WebMessageAsJson;
            }
            catch (Exception ex)
            {
                ShowFatalError("无法读取 3D 编辑器消息：" + ex.Message);
                return;
            }

            if (!_protocol.TryAccept(json,
                    out QuickLine3dEditorProtocolMessage message, out string error))
            {
                ShowFatalError("3D 编辑器消息校验失败：" + error);
                return;
            }

            switch (message.Kind)
            {
                case QuickLine3dEditorMessageKind.Ready:
                    _webView.CoreWebView2.PostWebMessageAsJson(
                        _protocol.InitializationJson);
                    _status.Text = "3D 场景已载入。";
                    break;
                case QuickLine3dEditorMessageKind.Commit:
                    Updates = message.Updates;
                    Revision = message.Revision;
                    DialogResult = DialogResult.OK;
                    Close();
                    break;
                case QuickLine3dEditorMessageKind.Cancel:
                    DialogResult = DialogResult.Cancel;
                    Close();
                    break;
                case QuickLine3dEditorMessageKind.RenderError:
                    ShowFatalError("3D 场景无法渲染：" + message.ErrorMessage);
                    break;
            }
        }

        private void BeginFatalError(string message)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(ShowFatalError), message);
                return;
            }
            ShowFatalError(message);
        }

        private void ShowFatalError(string message)
        {
            if (_fatal || IsDisposed || Disposing) return;
            _fatal = true;
            Updates = NoUpdates;
            _errorMessage.Text = message;
            _webView.Visible = false;
            _webView.Enabled = false;
            _status.Text = "3D 编辑器不可用，图纸未修改。";
            _errorPanel.Visible = true;
            _errorPanel.BringToFront();
        }

        private static string ResolveDefaultWebRoot()
        {
            string assemblyDirectory = Path.GetDirectoryName(
                typeof(QuickLine3dEditorForm).Assembly.Location);
            return Path.Combine(assemblyDirectory ?? "", "Web", "QuickLine3D");
        }

        private static string ValidateWebRoot(string webRoot)
        {
            if (string.IsNullOrWhiteSpace(webRoot))
                throw new ArgumentException("3D 页面目录不能为空。", nameof(webRoot));
            string fullPath = Path.GetFullPath(webRoot);
            if (!Directory.Exists(fullPath)
                || !File.Exists(Path.Combine(fullPath, "index.html")))
                throw new DirectoryNotFoundException("找不到 3D 页面资源：" + fullPath);
            return fullPath;
        }
    }
}
