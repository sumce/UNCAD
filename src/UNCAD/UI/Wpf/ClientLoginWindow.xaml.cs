using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// WebView2 在线登录（v2：账号 + 单设备在线）。
    /// 3.0 WPF 版（D-038，P4 聚焦输入），行为与旧 ClientLoginForm 逐条对齐。
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
    public sealed partial class ClientLoginWindow : FluentWindow
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

        /// <summary>登录完成且本机上线成功为 true；关闭/取消/失败一律 false。</summary>
        public bool Succeeded { get; private set; }

        /// <summary>
        /// 无头测试用：false 时不自动开始 WebView2 初始化与导航
        /// （避免测试环境真连授权服务器）。真机调用方永远不要设置。
        /// </summary>
        internal bool AutoBeginLogin { get; set; } = true;

        /// <summary>等价于旧 Form 的 IsDisposed：Closed 之后所有异步回调一律退出。</summary>
        private bool _closed;

        internal ClientLoginWindow()
        {
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            Account.Text = OnlineLicenseMonitor.StoredAccount;
            Loaded += (sender, args) =>
            {
                if (AutoBeginLogin) BeginLogin();
            };
            Closing += (sender, args) =>
            {
                if (_validating) args.Cancel = true;
            };
            Closed += OnClosed;
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
                // 3.3.7.2：环境+控制器统一入口（超时判定数据目录被僵尸锁住，
                // 本会话自动换独立目录）。
                await SharedWebView2Environment.EnsureControllerAsync(_webView);
                if (_closed) return;

                CoreWebView2 core = _webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.WebMessageReceived += OnWebMessageReceived;
                core.ProcessFailed += OnWebProcessFailed;
                core.NewWindowRequested += OnNewWindowRequested;
                _webView.NavigationStarting += OnNavigationStarting;
                _webView.NavigationCompleted += OnNavigationCompleted;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(LicenseBridgeScript);
                if (_closed) return;
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
                if (_closed) return;
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
                    DefaultBackgroundColor = System.Drawing.Color.White
                };
                BrowserHost.Children.Add(_webView);
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
            if (_closed || _webView?.CoreWebView2 == null) return;
            try
            {
                // 每次登录/重试都换一个新的一次性 nonce；抢回必须重新登录（服务端
                // 不支持免密抢回，重新登录会轮换凭证并把本机设为在线设备）。
                _state = CreateStateNonce();
                Uri url = BuildLoginUrl(_state, DeviceIdentity.GetOrCreateDeviceId(),
                    DeviceIdentity.DeviceName);
                _webView.CoreWebView2.Navigate(url.AbsoluteUri);
                SetStatus("请在下方页面登录 UNCAD 账号，登录成功后会自动在本机上线。",
                    InfoBarSeverity.Informational);
                ReloadButton.IsEnabled = false;
                UpdateActivateState();
            }
            catch (Exception ex)
            {
                Log.Warn("在线登录页面导航失败: " + ex.Message);
                SetStatus("无法打开登录页面：" + ex.Message, InfoBarSeverity.Error);
                ReloadButton.IsEnabled = true;
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
                    InfoBarSeverity.Warning);
                ReloadButton.IsEnabled = true;
            }
        }

        private void OnNavigationCompleted(object sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_completed || _closed) return;
            // 被安全策略取消的导航不报告加载失败，否则会覆盖已写好的状态。
            if (_canceledNavigations.Remove(args.NavigationId)) return;
            if (args.IsSuccess)
            {
                if (!_validating) ReloadButton.IsEnabled = true;
                return;
            }
            SetStatus("登录页面加载失败：" + args.WebErrorStatus + "。请检查网络后重试。",
                InfoBarSeverity.Error);
            ReloadButton.IsEnabled = true;
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
                Log.Info("忽略与回调账号不一致的兜底识别结果: " + AccountMask.Mask(account));
                return;
            }
            if (string.Equals((Account.Text ?? "").Trim(), account,
                StringComparison.Ordinal)) return;
            // 账号来自"刚完成登录的那个会话"，比输入框里的旧值更可信。
            Account.Text = account;
            Log.Info("在线登录已自动识别账号: " + AccountMask.Mask(account));
            if (_tokenSaved) ActivateOnThisDevice();
        }

        /// <summary>
        /// 登录流程不需要任何弹窗：window.open / target=_blank 一律拦截
        /// （D-037 安全加固），防止授权页被替换后把用户钓到外部浏览器。
        /// </summary>
        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (_completed || _closed) return;
            e.Handled = true;
            Log.Info("已阻止在线登录新开窗口: " + (e.Uri ?? ""));
        }

        private void OnWebProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs args)
        {
            if (_completed || _closed) return;
            Log.Warn("在线登录 WebView2 进程异常: " + args.ProcessFailedKind);
            SetStatus("登录页面进程异常（" + args.ProcessFailedKind + "），请点击“重新登录”。",
                InfoBarSeverity.Error);
            ReloadButton.IsEnabled = true;
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
                SetStatus("登录凭证无法写入本机：" + ex.Message, InfoBarSeverity.Error);
                return;
            }
            _state = "";   // nonce 一次性使用
            string fromCallback = (callbackAccount ?? "").Trim();
            if (fromCallback.Length > 0)
            {
                // 回调账号是权威值：直接采用，且此后忽略兜底识别消息。
                _accountFromCallback = fromCallback;
                if (!string.Equals((Account.Text ?? "").Trim(), fromCallback,
                    StringComparison.Ordinal))
                    Account.Text = fromCallback;
            }
            string account = ResolveAccount(fromCallback, Account.Text);
            if (account.Length > 0)
            {
                if (!string.Equals((Account.Text ?? "").Trim(), account,
                    StringComparison.Ordinal))
                    Account.Text = account;
                SetStatus(prefix + "，正在验证账号授权...", InfoBarSeverity.Informational);
                ActivateOnThisDevice();
            }
            else
            {
                SetStatus(prefix + "。正在识别账号，若未自动填入请在“UNCAD 账号”中填写后点击“在此设备上线”。",
                    InfoBarSeverity.Informational);
            }
            UpdateActivateState();
        }

        /// <summary>立刻用本机凭证心跳一次；active 才算上线成功。</summary>
        private async void ActivateOnThisDevice()
        {
            if (_validating || _completed) return;
            string account = (Account.Text ?? "").Trim();
            if (account.Length == 0)
            {
                SetStatus("请先填写 UNCAD 账号（登录成功后会自动填入）。",
                    InfoBarSeverity.Warning);
                Account.Focus();
                return;
            }
            _validating = true;
            ActivateButton.IsEnabled = false;
            ReloadButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            SetStatus("正在向授权服务器验证本机在线状态...", InfoBarSeverity.Informational);
            try
            {
                await Task.Run(() => OnlineLicenseMonitor.ValidateAndSave(account));
                if (_closed) return;
                _validating = false;
                _completed = true;
                SetStatus("本机已上线，授权验证成功。", InfoBarSeverity.Success);
                Succeeded = true;
                Close();
            }
            catch (Exception ex)
            {
                if (_closed) return;
                _validating = false;
                CancelButton.IsEnabled = true;
                ReloadButton.IsEnabled = true;
                Log.Warn("在线登录上线失败: " + ex.Message);
                SetStatus("未能在此设备上线：" + ex.Message
                    + " 请点击“重新登录”后重试。", InfoBarSeverity.Error);
                UpdateActivateState();
            }
        }

        private void RejectCallback(string error)
        {
            _state = "";   // 作废 nonce，任何后续回调都必须重新登录
            Log.Warn("在线登录回调被拒绝: " + error);
            SetStatus(error + " 请点击“重新登录”重试。", InfoBarSeverity.Error);
            ReloadButton.IsEnabled = true;
        }

        private void OnManualCode(object sender, RoutedEventArgs e)
        {
            UseManualCode();
        }

        private void UseManualCode()
        {
            // WebView2 不可用时的兜底：沿用既有手动窗（账号 + 本机凭证心跳）。
            // 两个窗都是 WPF：直接挂 Owner 保持模态层级。
            var window = new OnlineLicenseWindow(OnlineLicenseMonitor.Current.FailureReason)
            {
                Owner = this
            };
            window.ShowDialog();
            if (!window.Saved) return;
            _completed = true;
            Succeeded = true;
            Close();
        }

        /// <summary>WebView2 不可用的降级路径（无头测试也直接驱动它验证）。</summary>
        internal void ShowWebUnavailable(string message)
        {
            _webViewUnavailable = true;
            ReloadButton.IsEnabled = false;
            ManualButton.Visibility = Visibility.Visible;
            UnavailableMessage.Text = message ?? "";
            UnavailablePanel.Visibility = Visibility.Visible;
            if (_webView != null) _webView.Visibility = Visibility.Collapsed;
            SetStatus(message, InfoBarSeverity.Warning);
            Log.Warn("在线登录不可用: " + message);
            UpdateActivateState();
        }

        private void UpdateActivateState()
        {
            if (_closed) return;
            ActivateButton.IsEnabled = !_validating && !_completed
                && (Account.Text ?? "").Trim().Length > 0;
        }

        private void SetStatus(string message, InfoBarSeverity severity)
        {
            if (_closed) return;
            LoginStatus.Severity = severity;
            LoginStatus.Message = message ?? "";
        }

        private void OnAccountTextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateActivateState();
        }

        private void OnReloadLogin(object sender, RoutedEventArgs e)
        {
            NavigateToLogin();
        }

        private void OnActivateOnThisDevice(object sender, RoutedEventArgs e)
        {
            ActivateOnThisDevice();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Succeeded = false;
            Close();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _closed = true;
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
