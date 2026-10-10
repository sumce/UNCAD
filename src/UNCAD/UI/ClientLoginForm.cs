using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// WebView2 在线登录（v2：账号 + 单设备在线）。
    ///
    /// 流程：打开 <c>/client/login?state=&amp;device=&amp;name=</c> → 用户登录 →
    /// <c>/client/callback?token=..&amp;device=..&amp;state=..</c> → 校验 state 与 device →
    /// 写入本机凭证 → 立刻心跳一次，active 才算上线成功。
    ///
    /// 安全边界（不得放宽）：
    /// 1. 只接受 <c>https://uncad.aosa.me:443</c>（复用 OnlineLicenseMonitor.IsExpectedEndpoint
    ///    的 scheme + host + port 校验），其它主机/协议一律拒绝；
    /// 2. 回调的 <c>state</c> 必须与本机本次登录生成的一次性 nonce **逐字符相等（Ordinal）**；
    /// 3. 回调的 <c>device</c> 必须与本机设备 ID 相等——凭证不能是发给别的设备的；
    /// 4. nonce 一次性使用：被消费或被拒后立即作废，重试必须重新登录（服务端不支持免密抢回）。
    /// </summary>
    internal sealed class ClientLoginForm : DpiAwareForm
    {
        /// <summary>在线授权/登录主机，与 OnlineLicenseMonitor 的 XOR 主机常量必须一致。</summary>
        internal const string LicenseHost = "uncad.aosa.me";
        internal const string LoginPath = "/client/login";
        internal const string CallbackPath = "/client/callback";
        private const int StateNonceBytes = 16;
        private const string AccountMessageKind = "account";
        private const string LicenseMessageKind = "license";

        /// <summary>
        /// 兜底通道：回调页会把 {token, device, account, state} 放在
        /// <c>window.__UNCAD_LICENSE__</c>。账号由服务端回调直接给出（首选通道）；
        /// 只有在回调缺 account 时，才退回同源 <c>/api/account/profile</c> 识别。
        /// 两条消息都属于"尽力而为"，失败不影响主通道。
        /// </summary>
        private const string LicenseBridgeScript = @"
(function () {
  function post(payload) {
    try { chrome.webview.postMessage(JSON.stringify(payload)); } catch (error) { }
  }
  function reportAccount() {
    try {
      fetch('/api/account/profile', { credentials: 'same-origin' })
        .then(function (response) { return response.ok ? response.json() : null; })
        .then(function (profile) {
          if (profile && profile.username)
            post({ kind: 'account', account: String(profile.username) });
        })
        .catch(function () { });
    } catch (error) { }
  }
  function report() {
    var payload = null;
    try { payload = window.__UNCAD_LICENSE__; } catch (error) { }
    if (!payload || !payload.token) return;
    post({
      kind: 'license',
      token: String(payload.token),
      device: payload.device ? String(payload.device) : '',
      account: payload.account ? String(payload.account) : '',
      state: payload.state ? String(payload.state) : ''
    });
    // 服务端回调已带账号时不再猜测账号；缺失才退回同源识别。
    if (!payload.account) reportAccount();
  }
  report();
  document.addEventListener('DOMContentLoaded', report);
  window.addEventListener('load', report);
  setTimeout(report, 400);
  setTimeout(report, 1200);
  setTimeout(report, 2500);
})();";

        private readonly Panel _browserHost;
        private readonly TextBox _account;
        private readonly Label _status;
        private readonly Button _activate;
        private readonly Button _reload;
        private readonly Button _manual;
        private readonly Button _cancel;
        /// <summary>被安全策略取消的导航：其 NavigationCompleted 不能覆盖状态栏。</summary>
        private readonly HashSet<ulong> _canceledNavigations = new HashSet<ulong>();
        private WebView2 _webView;
        private string _state = "";
        private bool _validating;
        private bool _completed;
        private bool _webViewUnavailable;
        /// <summary>本次会话已取得并写入本机凭证（之后才允许自动上线）。</summary>
        private bool _tokenSaved;
        /// <summary>服务端回调直接给出的账号名（首选来源）；为空时才走兜底识别。</summary>
        private string _accountFromCallback = "";

        internal ClientLoginForm()
        {
            DialogLayout.Apply(this, "UNCAD Pro 在线登录",
                new Size(860, 660), new Size(700, 540));

            _browserHost = new Panel
            {
                Name = "LoginBrowserHost",
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0)
            };
            _account = new TextBox
            {
                Name = "Account",
                Dock = DockStyle.Fill,
                Text = OnlineLicenseMonitor.StoredAccount,
                Font = UiTheme.FontInput,
                Margin = new Padding(0)
            };
            UiTheme.StyleInput(_account);
            _account.TextChanged += (sender, args) => UpdateActivateState();
            _status = new Label
            {
                Name = "LoginStatus",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                ForeColor = UiTheme.TextSecondary,
                Text = "正在准备登录页面...",
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };

            _activate = UiTheme.PrimaryButton("在此设备上线");
            _activate.Click += (sender, args) => ActivateOnThisDevice();
            _reload = UiTheme.Button("重新登录");
            _reload.Click += (sender, args) => NavigateToLogin();
            _manual = UiTheme.Button("手动输入");
            _manual.Visible = false;
            _manual.Click += (sender, args) => UseManualCode();
            _cancel = UiTheme.Button("取消", DialogResult.Cancel);

            var commands = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, UiTheme.SpaceM, 0, 0)
            };
            commands.Controls.Add(_activate);
            commands.Controls.Add(_reload);
            commands.Controls.Add(_manual);
            commands.Controls.Add(_cancel);

            var accountRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            accountRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            accountRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            accountRow.Controls.Add(new Label
            {
                Text = "UNCAD 账号",
                Dock = DockStyle.Fill,
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, UiTheme.SpaceS, 0)
            }, 0, 0);
            accountRow.Controls.Add(_account, 1, 0);

            var card = UiTheme.Card(16);
            card.Name = "LoginCard";
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(24, 20, 24, 18);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.Controls.Add(new Label
            {
                Text = "在线登录",
                Dock = DockStyle.Fill,
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = "使用 UNCAD 账号登录并在此设备上线",
                Dock = DockStyle.Fill,
                Font = UiTheme.FontDisplay,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 1);
            layout.Controls.Add(accountRow, 0, 2);
            layout.Controls.Add(_status, 0, 3);
            layout.Controls.Add(_browserHost, 0, 4);
            layout.Controls.Add(commands, 0, 5);
            card.Controls.Add(layout);
            Controls.Add(card);

            CancelButton = _cancel;
            AcceptButton = _activate;
            Shown += (sender, args) => BeginLogin();
            FormClosing += (sender, args) =>
            {
                if (_validating) args.Cancel = true;
            };
            UpdateActivateState();
        }

        // ---- 纯逻辑：可被 tests/UNCAD.Tests/ClientLoginContractTests.cs 直接验证 ----

        /// <summary>生成一次性 state nonce：128 位随机数的 32 位小写十六进制。</summary>
        internal static string CreateStateNonce()
        {
            var bytes = new byte[StateNonceBytes];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
                generator.GetBytes(bytes);
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        /// <summary>登录地址必须落在受信任的授权主机上。</summary>
        internal static Uri BuildLoginUrl(string state, string deviceId, string deviceName)
        {
            string nonce = (state ?? "").Trim();
            if (nonce.Length == 0)
                throw new ArgumentException("登录 state 不能为空。", nameof(state));
            string device = (deviceId ?? "").Trim();
            if (device.Length == 0)
                throw new ArgumentException("设备标识不能为空。", nameof(deviceId));
            var uri = new Uri("https://" + LicenseHost + LoginPath
                + "?state=" + Uri.EscapeDataString(nonce)
                + "&" + OnlineLicenseMonitor.DeviceQueryName + "=" + Uri.EscapeDataString(device)
                + "&name=" + Uri.EscapeDataString((deviceName ?? "").Trim()), UriKind.Absolute);
            if (!OnlineLicenseMonitor.IsExpectedEndpoint(uri))
                throw new InvalidDataException("登录地址不受信任。");
            return uri;
        }

        /// <summary>
        /// 解析 <c>/client/callback?token=..&amp;device=..&amp;account=..&amp;state=..</c>。
        /// <paramref name="account"/> 是服务端直接给出的账号名（心跳路径
        /// <c>/d/key/{account}/pro.key</c> 需要它）；缺失时为空串，调用方走兜底识别。
        /// 返回 false 表示必须拒绝该回调，<paramref name="error"/> 是给用户看的中文原因。
        /// </summary>
        internal static bool TryParseCallback(Uri uri, string expectedState,
            string expectedDevice, out string token, out string device, out string state,
            out string account, out string error)
        {
            token = "";
            device = "";
            state = "";
            account = "";
            error = "";
            if (uri == null)
            {
                error = "登录回调地址无效。";
                return false;
            }
            if (!OnlineLicenseMonitor.IsExpectedEndpoint(uri))
            {
                error = "登录回调不是来自受信任的授权服务器，已拒绝。";
                return false;
            }
            if (!string.Equals(uri.AbsolutePath, CallbackPath, StringComparison.Ordinal))
            {
                error = "登录回调路径无效。";
                return false;
            }
            // device/state/account 非机密，失败时也回传便于诊断；token 只在通过校验后
            // 回传，避免调用方误用被拒回调里的凭证。
            device = ReadQueryValue(uri.Query, "device");
            state = ReadQueryValue(uri.Query, "state");
            account = ReadQueryValue(uri.Query, "account").Trim();
            string parsedToken = ReadQueryValue(uri.Query, "token");
            if (!AcceptCallback(parsedToken, device, state, expectedState, expectedDevice,
                out error)) return false;
            token = parsedToken.Trim();
            return true;
        }

        /// <summary>
        /// 账号来源优先级：**服务端回调 account（权威）** &gt; 输入框/同源识别兜底。
        /// 心跳路径需要账号名，这里保证"有回调账号就用回调账号"。
        /// </summary>
        internal static string ResolveAccount(string callbackAccount, string typedAccount)
        {
            string fromCallback = (callbackAccount ?? "").Trim();
            if (fromCallback.Length > 0) return fromCallback;
            return (typedAccount ?? "").Trim();
        }

        /// <summary>兜底通道：解析回调页注入的 window.__UNCAD_LICENSE__ 上报数据。</summary>
        internal static bool TryParseCallbackPayload(string json, string expectedState,
            string expectedDevice, out string token, out string device, out string account,
            out string error)
        {
            token = "";
            device = "";
            account = "";
            error = "";
            if (!TryParseMessage(json, out LicenseCallbackPayload payload, out error))
                return false;
            if (!AcceptCallback(payload.token, payload.device, payload.state,
                expectedState, expectedDevice, out error)) return false;
            token = (payload.token ?? "").Trim();
            device = (payload.device ?? "").Trim();
            account = (payload.account ?? "").Trim();
            return true;
        }

        /// <summary>解析回调页上报的 JSON 消息（token 通道与账号通道共用）。</summary>
        internal static bool TryParseMessage(string json,
            out LicenseCallbackPayload payload, out string error)
        {
            payload = null;
            error = "";
            try
            {
                payload = new JavaScriptSerializer()
                    .Deserialize<LicenseCallbackPayload>((json ?? "").TrimStart('\uFEFF'));
            }
            catch (Exception)
            {
                error = "登录页面返回的数据不是有效 JSON。";
                return false;
            }
            if (payload == null)
            {
                error = "登录页面没有返回登录凭证。";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 回调校验的唯一入口（URL 与脚本两条通道共用）：
        /// 凭证/设备/state 必须齐全，state 与 device 必须与本机完全一致（Ordinal）。
        /// 任一失败都不回传 token，避免调用方误用被拒的凭证。
        /// </summary>
        internal static bool AcceptCallback(string token, string device, string state,
            string expectedState, string expectedDevice, out string error)
        {
            error = "";
            if (!DeviceIdentity.IsPlausibleToken(token))
            {
                error = "登录服务未返回有效凭证。";
                return false;
            }
            string callbackDevice = (device ?? "").Trim();
            if (callbackDevice.Length == 0)
            {
                error = "登录回调缺少设备标识，无法确认凭证归属，已拒绝。";
                return false;
            }
            if (string.IsNullOrEmpty(expectedDevice)
                || !string.Equals(callbackDevice, expectedDevice, StringComparison.Ordinal))
            {
                error = "登录回调携带的设备标识与本机不一致，已拒绝该回调。";
                return false;
            }
            string callbackState = state ?? "";
            if (callbackState.Length == 0)
            {
                error = "登录回调缺少 state，无法确认回调来源，已拒绝。";
                return false;
            }
            if (string.IsNullOrEmpty(expectedState)
                || !string.Equals(callbackState, expectedState, StringComparison.Ordinal))
            {
                error = "登录回调的 state 与本机会话不一致，已拒绝该回调（可能被伪造）。";
                return false;
            }
            return true;
        }

        private static string ReadQueryValue(string query, string name)
        {
            string text = (query ?? "").TrimStart('?');
            if (text.Length == 0) return "";
            foreach (string pair in text.Split('&'))
            {
                if (pair.Length == 0) continue;
                int separator = pair.IndexOf('=');
                string key = separator < 0 ? pair : pair.Substring(0, separator);
                if (!string.Equals(Uri.UnescapeDataString(key.Replace('+', ' ')), name,
                    StringComparison.Ordinal)) continue;
                string value = separator < 0 ? "" : pair.Substring(separator + 1);
                return Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            return "";
        }

        // ---- WebView2 生命周期 ----

        private async void BeginLogin()
        {
            if (_completed) return;
            if (!TryCreateWebView()) return;
            try
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UNCAD", "WebView2");
                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                if (IsDisposed || Disposing) return;
                await _webView.EnsureCoreWebView2Async(environment);
                if (IsDisposed || Disposing) return;

                CoreWebView2 core = _webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.WebMessageReceived += OnWebMessageReceived;
                core.ProcessFailed += OnWebProcessFailed;
                _webView.NavigationStarting += OnNavigationStarting;
                _webView.NavigationCompleted += OnNavigationCompleted;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(LicenseBridgeScript);
                if (IsDisposed || Disposing) return;
                NavigateToLogin();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowWebUnavailable("未检测到 Microsoft Edge WebView2 运行时，无法打开在线登录页面。"
                    + "请先安装“Microsoft Edge WebView2 Runtime”（常青版），"
                    + "或改用手动输入账号。");
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing) return;
                Log.Warn("在线登录 WebView2 初始化失败: " + ex.Message);
                ShowWebUnavailable("在线登录页面初始化失败：" + ex.Message
                    + "。可改用手动输入账号。");
            }
        }

        private bool TryCreateWebView()
        {
            if (_webView != null) return true;
            if (_webViewUnavailable) return false;
            try
            {
                _webView = new WebView2
                {
                    Name = "LoginWebView",
                    Dock = DockStyle.Fill,
                    DefaultBackgroundColor = Color.White
                };
                _browserHost.Controls.Add(_webView);
                return true;
            }
            catch (Exception ex)
            {
                _webView = null;
                Log.Warn("在线登录 WebView2 控件创建失败: " + ex.Message);
                ShowWebUnavailable("无法创建在线登录页面控件：" + ex.Message
                    + "。可改用手动输入账号。");
                return false;
            }
        }

        private void NavigateToLogin()
        {
            if (_completed || _webViewUnavailable || _validating) return;
            if (IsDisposed || _webView?.CoreWebView2 == null) return;
            try
            {
                // 每次登录/重试都换一个新的一次性 nonce；抢回必须重新登录（服务端
                // 不支持免密抢回，重新登录会轮换凭证并把本机设为在线设备）。
                _state = CreateStateNonce();
                Uri url = BuildLoginUrl(_state, DeviceIdentity.GetOrCreateDeviceId(),
                    DeviceIdentity.DeviceName);
                _webView.CoreWebView2.Navigate(url.AbsoluteUri);
                SetStatus("请在下方页面登录 UNCAD 账号，登录成功后会自动在本机上线。",
                    UiTheme.TextSecondary);
                _reload.Enabled = false;
                UpdateActivateState();
            }
            catch (Exception ex)
            {
                Log.Warn("在线登录页面导航失败: " + ex.Message);
                SetStatus("无法打开登录页面：" + ex.Message, UiTheme.DangerFg);
                _reload.Enabled = true;
            }
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (_completed) return;
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri uri)) return;

            if (string.Equals(uri.AbsolutePath, CallbackPath, StringComparison.Ordinal)
                && OnlineLicenseMonitor.IsExpectedEndpoint(uri))
            {
                // 主通道：直接解析回调 URL，不等页面渲染完成。
                if (TryParseCallback(uri, _state, DeviceIdentity.GetOrCreateDeviceId(),
                    out string token, out _, out _, out string account, out string error))
                {
                    StoreToken(token, account, "已取回登录凭证");
                }
                else
                {
                    RejectCallback(error);
                }
                return;
            }

            if (!OnlineLicenseMonitor.IsExpectedEndpoint(uri))
            {
                args.Cancel = true;
                _canceledNavigations.Add(args.NavigationId);
                Log.Warn("已阻止在线登录跳转到非授权服务器: " + uri);
                SetStatus("已阻止跳转到非授权服务器地址（" + uri.Host + "）。",
                    UiTheme.WarningFg);
                _reload.Enabled = true;
            }
        }

        private void OnNavigationCompleted(object sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_completed || IsDisposed) return;
            // 被安全策略取消的导航不报告加载失败，否则会覆盖已写好的状态。
            if (_canceledNavigations.Remove(args.NavigationId)) return;
            if (args.IsSuccess)
            {
                if (!_validating) _reload.Enabled = true;
                return;
            }
            SetStatus("登录页面加载失败：" + args.WebErrorStatus + "。请检查网络后重试。",
                UiTheme.DangerFg);
            _reload.Enabled = true;
        }

        private void OnWebMessageReceived(object sender,
            CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (_completed) return;
            string message;
            try
            {
                message = args.TryGetWebMessageAsString();
            }
            catch (Exception ex)
            {
                Log.Warn("在线登录消息读取失败: " + ex.Message);
                return;
            }
            if (string.IsNullOrWhiteSpace(message)) return;
            // 消息来源只允许受信任主机；来源缺失时不额外放宽 state/device 校验。
            if (Uri.TryCreate(args.Source, UriKind.Absolute, out Uri source)
                && !OnlineLicenseMonitor.IsExpectedEndpoint(source)) return;

            if (!TryParseMessage(message, out LicenseCallbackPayload envelope, out _)) return;
            if (string.Equals((envelope.kind ?? "").Trim(), AccountMessageKind,
                StringComparison.Ordinal))
            {
                ApplyAccount((envelope.account ?? "").Trim());
                return;
            }
            if (_validating || _tokenSaved) return;
            // 只处理登录凭证消息（kind=license 或至少带 token），无关消息静默忽略。
            if (!string.Equals((envelope.kind ?? "").Trim(), LicenseMessageKind,
                    StringComparison.Ordinal)
                && (envelope.token ?? "").Length == 0) return;
            if (TryParseCallbackPayload(message, _state, DeviceIdentity.GetOrCreateDeviceId(),
                out string token, out _, out string account, out string error))
            {
                StoreToken(token, account, "已通过登录页面取回凭证");
            }
            else
            {
                RejectCallback(error);
            }
        }

        private void ApplyAccount(string account)
        {
            if (account.Length == 0) return;
            // 服务端回调已给出账号时，兜底识别不得覆盖它。
            if (_accountFromCallback.Length > 0
                && !string.Equals(_accountFromCallback, account, StringComparison.Ordinal))
            {
                Log.Info("忽略与回调账号不一致的兜底识别结果: " + account);
                return;
            }
            if (string.Equals(_account.Text.Trim(), account, StringComparison.Ordinal)) return;
            // 账号来自"刚完成登录的那个会话"，比输入框里的旧值更可信。
            _account.Text = account;
            Log.Info("在线登录已自动识别账号: " + account);
            if (_tokenSaved) ActivateOnThisDevice();
        }

        private void OnWebProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs args)
        {
            if (_completed || IsDisposed) return;
            Log.Warn("在线登录 WebView2 进程异常: " + args.ProcessFailedKind);
            SetStatus("登录页面进程异常（" + args.ProcessFailedKind + "），请点击“重新登录”。",
                UiTheme.DangerFg);
            _reload.Enabled = true;
        }

        /// <summary>
        /// 回调通过后写入本机凭证，并尝试立刻上线。
        /// <paramref name="callbackAccount"/> 是服务端回调直接给出的账号名（首选来源）；
        /// 为空时才保留输入框/兜底识别的结果。
        /// </summary>
        private void StoreToken(string token, string callbackAccount, string prefix)
        {
            if (_tokenSaved) return;
            try
            {
                DeviceIdentity.SaveToken(token);
                _tokenSaved = true;
            }
            catch (Exception ex)
            {
                Log.Warn("登录凭证写入失败: " + ex.Message);
                SetStatus("登录凭证无法写入本机：" + ex.Message, UiTheme.DangerFg);
                return;
            }
            _state = "";   // nonce 一次性使用
            string fromCallback = (callbackAccount ?? "").Trim();
            if (fromCallback.Length > 0)
            {
                // 回调账号是权威值：直接采用，且此后忽略兜底识别消息。
                _accountFromCallback = fromCallback;
                if (!string.Equals(_account.Text.Trim(), fromCallback, StringComparison.Ordinal))
                    _account.Text = fromCallback;
            }
            string account = ResolveAccount(fromCallback, _account.Text);
            if (account.Length > 0)
            {
                if (!string.Equals(_account.Text.Trim(), account, StringComparison.Ordinal))
                    _account.Text = account;
                SetStatus(prefix + "，正在验证账号授权...", UiTheme.TextSecondary);
                ActivateOnThisDevice();
            }
            else
            {
                SetStatus(prefix + "。正在识别账号，若未自动填入请在“UNCAD 账号”中填写后点击“在此设备上线”。",
                    UiTheme.TextSecondary);
            }
            UpdateActivateState();
        }

        /// <summary>立刻用本机凭证心跳一次；active 才算上线成功。</summary>
        private async void ActivateOnThisDevice()
        {
            if (_validating || _completed) return;
            string account = _account.Text.Trim();
            if (account.Length == 0)
            {
                SetStatus("请先填写 UNCAD 账号（登录成功后会自动填入）。", UiTheme.WarningFg);
                _account.Focus();
                return;
            }
            _validating = true;
            _activate.Enabled = false;
            _reload.Enabled = false;
            _cancel.Enabled = false;
            SetStatus("正在向授权服务器验证本机在线状态...", UiTheme.TextSecondary);
            try
            {
                await Task.Run(() => OnlineLicenseMonitor.ValidateAndSave(account));
                if (IsDisposed || Disposing) return;
                _validating = false;
                _completed = true;
                SetStatus("本机已上线，授权验证成功。", UiTheme.SuccessFg);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing) return;
                _validating = false;
                _cancel.Enabled = true;
                _reload.Enabled = true;
                Log.Warn("在线登录上线失败: " + ex.Message);
                SetStatus("未能在此设备上线：" + ex.Message
                    + " 请点击“重新登录”后重试。", UiTheme.DangerFg);
                UpdateActivateState();
            }
        }

        private void RejectCallback(string error)
        {
            _state = "";   // 作废 nonce，任何后续回调都必须重新登录
            Log.Warn("在线登录回调被拒绝: " + error);
            SetStatus(error + " 请点击“重新登录”重试。", UiTheme.DangerFg);
            _reload.Enabled = true;
        }

        private void UseManualCode()
        {
            // WebView2 不可用时的兜底：沿用既有手动窗（账号 + 本机凭证心跳）。
            using (var form = new OnlineLicenseForm(OnlineLicenseMonitor.Current.FailureReason))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                _completed = true;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void ShowWebUnavailable(string message)
        {
            _webViewUnavailable = true;
            _reload.Enabled = false;
            _manual.Visible = true;
            SetStatus(message, UiTheme.WarningFg);
            Log.Warn("在线登录不可用: " + message);
            UpdateActivateState();
        }

        private void UpdateActivateState()
        {
            if (IsDisposed) return;
            _activate.Enabled = !_validating && !_completed
                && _account.Text.Trim().Length > 0;
        }

        private void SetStatus(string message, Color color)
        {
            if (IsDisposed) return;
            _status.ForeColor = color;
            _status.Text = message ?? "";
        }

        protected override void OnFormClosed(FormClosedEventArgs args)
        {
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _webView.CoreWebView2.ProcessFailed -= OnWebProcessFailed;
            }
            if (_webView != null)
            {
                _webView.NavigationStarting -= OnNavigationStarting;
                _webView.NavigationCompleted -= OnNavigationCompleted;
                _webView.Dispose();
                _webView = null;
            }
            base.OnFormClosed(args);
        }
    }

    /// <summary>
    /// 回调页上报消息的形状：<c>{kind:"license", token, device, state}</c> 或
    /// <c>{kind:"account", account}</c>（账号通道来自同源 /api/account/profile）。
    /// </summary>
    internal sealed class LicenseCallbackPayload
    {
        public string kind { get; set; }
        public string token { get; set; }
        public string device { get; set; }
        public string state { get; set; }
        public string account { get; set; }
    }
}
