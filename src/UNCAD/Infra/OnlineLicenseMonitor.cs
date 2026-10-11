using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace UNCAD.Infra
{
    internal sealed class OnlineLicenseState
    {
        public bool HasResponse { get; set; }
        public bool IsActive { get; set; }
        public string Status { get; set; }
        public LicenseMode LicenseMode { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public DateTimeOffset? ServerTime { get; set; }
        public string CustomerCode { get; set; }
        public string Company { get; set; }
        public string Licensee { get; set; }
        public int? ExpectedAuthorizationYears { get; set; }
        public string MessageTitle { get; set; }
        public string MessageLevel { get; set; }
        public string Message { get; set; }
        public string FailureReason { get; set; }

        // ---- v2：账号 + 单设备在线 ----
        /// <summary>服务端回显的请求设备 ID。</summary>
        public string DeviceId { get; set; }
        /// <summary>当前在线设备的机器名（被顶掉时用于提示"已被谁登录"）。</summary>
        public string ActiveDeviceName { get; set; }
        /// <summary>当前在线设备的上线时间（原样保留服务端 ISO 文本）。</summary>
        public string SignedInAt { get; set; }
        /// <summary>账号允许的并发设备数（当前恒为 1）。</summary>
        public int? DeviceLimit { get; set; }
    }

    internal sealed class OnlineLicenseNotice
    {
        public string Title { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }

    internal sealed class OnlineLicenseDocument
    {
        public int SchemaVersion { get; set; }
        public string Product { get; set; }
        public string LicenseMode { get; set; }
        /// <summary>v1 为授权码；v2 起回显<b>账号名</b>（保留字段名以维持路径一致性校验）。</summary>
        public string AuthorizationCode { get; set; }
        public string CustomerCode { get; set; }
        public string Company { get; set; }
        public string Licensee { get; set; }
        public string Status { get; set; }
        public string ExpiresAt { get; set; }
        public int? ExpectedAuthorizationYears { get; set; }
        public string MessageTitle { get; set; }
        public string MessageLevel { get; set; }
        public string Message { get; set; }
        public string DeviceId { get; set; }
        public string ActiveDeviceName { get; set; }
        public string SignedInAt { get; set; }
        public int? DeviceLimit { get; set; }
    }

    internal static class OnlineLicenseMonitor
    {
        /// <summary>
        /// 心跳间隔：3 分钟。被顶掉的设备最迟一个周期就会拿到 device-kicked 并被
        /// 立即停用；本次登录/抢回会立刻触发一次心跳，无需等轮询。
        ///
        /// **本版本不支持离线使用，没有宽限期**：拿不到服务器明确结论
        /// （pending / unavailable）时，ProductMetadata 会立即停用全部命令，
        /// 只保留明确的中文原因。历史上的 <c>UNC_LICENSE_LAST_OK</c> 水位线
        /// 键名仍然保留（升级兼容），但已不再读写、也不再参与判定。
        /// </summary>
        internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(3);
        private const int TimeoutMilliseconds = 8000;
        private const int MaximumBytes = 64 * 1024;
        /// <summary>v2 请求头：本机登录凭证（未登录时不发送）。</summary>
        internal const string TokenHeaderName = "X-Uncad-Token";
        /// <summary>v2 查询参数：本机设备 ID（始终携带）。</summary>
        internal const string DeviceQueryName = "device";
        private static readonly string LicenseHost = DecodeHost();
        private static readonly object Sync = new object();
        private static Timer _timer;
        private static int _refreshing;
        private static OnlineLicenseState _current = Pending("正在读取账号授权。");
        private static OnlineLicenseNotice _pendingNotice;
        private static string _pendingActivationReason;
        private static string _pendingActivationKey;
        private static string _lastActivationKey;
        private static string _lastNoticeKey;

        internal static OnlineLicenseState Current
        {
            get { lock (Sync) return _current; }
        }

        /// <summary>本机保存的账号名（v2：路径 /d/key/{account}/pro.key 的 {account}）。</summary>
        internal static string StoredAccount
            => (Settings.Get(ConfigKeys.OnlineAuthorizationCode, "") ?? "").Trim();

        /// <summary>兼容旧命名：v1 存授权码，v2 存账号名，同一个注册表键。</summary>
        internal static string StoredAuthorizationCode => StoredAccount;

        /// <summary>本机登录凭证（v2）；未登录时为空，请求不带 X-Uncad-Token。</summary>
        internal static string StoredToken => DeviceIdentity.GetToken();

        internal static void Start()
        {
            if (!ProductMetadata.RequiresOnlineLicense) return;
            lock (Sync)
            {
                if (_timer != null) return;
                _current = Pending("正在验证账号授权。");
                _pendingNotice = null;
                _pendingActivationReason = null;
                _pendingActivationKey = null;
                _lastActivationKey = null;
                _lastNoticeKey = null;
                _timer = new Timer(Refresh, null, TimeSpan.Zero, RefreshInterval);
            }
            Log.Info("在线授权监控已启动，每 3 分钟刷新一次");
        }

        internal static void Stop()
        {
            Timer timer;
            lock (Sync)
            {
                timer = _timer;
                _timer = null;
                _pendingNotice = null;
                _pendingActivationReason = null;
            }
            timer?.Dispose();
        }

        internal static bool TryTakeActivationRequest(out string reason)
        {
            lock (Sync)
            {
                reason = _pendingActivationReason;
                if (reason == null) return false;
                _pendingActivationReason = null;
                _lastActivationKey = _pendingActivationKey;
                _pendingActivationKey = null;
                return true;
            }
        }

        /// <summary>
        /// 取出待显示的通知；**取走即清空**（与 <see cref="TryTakeActivationRequest"/> 同一约定）。
        ///
        /// 这里曾经漏了清空 `_pendingNotice`，后果是**命令行无限刷屏**：
        /// 本方法由 `Bootstrap.OnLicenseIdle` 在每个 AutoCAD Idle 事件里调用，
        /// 而 Idle 会持续触发，于是同一条通知被反复取出并写进命令行
        /// （用户反馈"输入框一直刷屏这个"）。取走即清空才能保证"一条通知只播一次"。
        /// </summary>
        internal static bool TryTakeNotice(out OnlineLicenseNotice notice)
        {
            lock (Sync)
            {
                notice = _pendingNotice;
                if (notice == null) return false;
                _lastNoticeKey = NoticeKey(notice);
                _pendingNotice = null;
                return true;
            }
        }

        /// <summary>
        /// 用本机设备身份与凭证校验账号并保存。只有服务器返回 active 才写入账号；
        /// 失败抛出 DTO 里带中文原因的异常（被踢 / 凭证失效 / 未绑定等）。
        /// </summary>
        internal static OnlineLicenseState ValidateAndSave(string account)
        {
            if (Interlocked.Exchange(ref _refreshing, 1) != 0)
                throw new InvalidOperationException("后台授权验证正在进行，请稍后重试。");
            try
            {
                string normalized = NormalizeAccount(account);
                OnlineLicenseState validated = Download(normalized);
                if (!validated.IsActive)
                    throw new InvalidOperationException(validated.FailureReason);
                Settings.Set(ConfigKeys.OnlineAuthorizationCode, normalized);
                lock (Sync) Publish(validated, normalized);
                Log.Info("在线授权已验证并保存，设备 " + validated.DeviceId);
                return validated;
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        private static void Refresh(object state)
        {
            if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;
            string account = StoredAccount;
            try
            {
                if (account.Length == 0)
                {
                    lock (Sync)
                    {
                        OnlineLicenseState missing = Pending("尚未登录 UNCAD 账号。");
                        missing.Status = "missing";
                        Publish(missing, account);
                    }
                    return;
                }
                OnlineLicenseState refreshed = Download(account);
                lock (Sync) Publish(refreshed, account);
                Log.Info("账号授权刷新成功: " + refreshed.Status
                    + (string.IsNullOrEmpty(refreshed.ActiveDeviceName)
                        ? "" : "（在线设备 " + refreshed.ActiveDeviceName + "）"));
            }
            catch (Exception ex)
            {
                lock (Sync)
                {
                    Publish(new OnlineLicenseState
                    {
                        HasResponse = false,
                        IsActive = false,
                        Status = "unavailable",
                        FailureReason = "无法验证账号授权：" + ex.Message
                    }, account);
                }
                Log.Warn("账号授权刷新失败: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        /// <summary>
        /// 用户主动操作时请求打开登录窗口（执行被停用的 UNCAD 命令、或运行 UNC_LOGIN）。
        ///
        /// **只有用户主动触发才弹窗**：周期性心跳绝不能自己弹模态窗口——
        /// 应用程序模态窗口会让用户**连 AutoCAD 本身都用不了**，而用户明确要求
        /// "不登录不能用 UNCAD，但不能因此不让用 CAD"。
        ///
        /// 这里**刻意不做去重**：每次用户主动操作都要给出登录入口。
        /// 旧实现按 (账号, 原因) 去重，弹过一次之后登录窗口再也不出现，
        /// 用户会被永久卡在"只能看到一句错误、却没有地方重新登录"的状态。
        /// </summary>
        internal static void RequestUserLogin(string reason)
        {
            lock (Sync)
            {
                _pendingActivationReason = string.IsNullOrWhiteSpace(reason)
                    ? "当前账号授权不可用。" : reason;
            }
        }

        private static void Publish(OnlineLicenseState state, string account)
        {
            _current = state;
            QueueNotice(state);
            if (state.IsActive)
            {
                // 恢复有效：清掉待处理的登录提示与去重痕迹
                _pendingActivationReason = null;
                _pendingActivationKey = null;
                _lastActivationKey = null;
            }
            // 注意：这里**故意不**设置 _pendingActivationReason。
            // 周期性心跳的结果只通过 QueueNotice 在命令行提示（见 Bootstrap.OnLicenseIdle），
            // 绝不主动弹模态窗口。需要登录时由用户操作触发（见 RequestUserLogin）。
        }

        private static OnlineLicenseState Download(string account)
        {
            string normalized = NormalizeAccount(account);
            string deviceId = DeviceIdentity.GetOrCreateDeviceId();
            Uri endpoint = BuildRequestUri(normalized, deviceId);
            var requestUri = new UriBuilder(endpoint)
            {
                Query = (endpoint.Query.Length > 1 ? endpoint.Query.Substring(1) + "&" : "")
                    + "_=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)
            }.Uri;
            HttpWebRequest request = WebRequest.CreateHttp(requestUri);
            request.Method = "GET";
            request.UserAgent = "UNCAD/" + ProductMetadata.VersionLabel;
            request.Accept = "application/json,text/plain,*/*";
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip
                | DecompressionMethods.Deflate;
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            // 未登录时不带凭证：服务端随后返回 unbound / device-missing。
            string token = DeviceIdentity.GetToken();
            if (token.Length > 0) request.Headers[TokenHeaderName] = token;

            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new IOException("授权服务器返回 HTTP "
                        + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + "。");
                if (!IsExpectedEndpoint(response.ResponseUri))
                    throw new InvalidDataException("授权响应来自非预期域名。");
                if (response.ContentLength > MaximumBytes)
                    throw new InvalidDataException("授权文件超过 64 KB 限制。");

                return ParseResponse(ReadBody(response.GetResponseStream()),
                    ReadServerTime(response), normalized, deviceId);
            }
        }

        /// <summary>授权端点路径（不含 device / _ 查询参数）。</summary>
        internal static Uri BuildEndpoint(string account)
        {
            string normalized = NormalizeAccount(account);
            var uri = new Uri("https://" + LicenseHost + "/d/key/"
                + Uri.EscapeDataString(normalized) + "/pro.key", UriKind.Absolute);
            if (!IsExpectedEndpoint(uri))
                throw new InvalidDataException("授权服务器地址不受信任。");
            return uri;
        }

        /// <summary>v2 心跳请求地址：路径 + <c>?device={deviceId}</c>。</summary>
        internal static Uri BuildRequestUri(string account, string deviceId)
        {
            var builder = new UriBuilder(BuildEndpoint(account))
            {
                Query = DeviceQueryName + "=" + Uri.EscapeDataString((deviceId ?? "").Trim())
            };
            return builder.Uri;
        }

        /// <summary>
        /// 账号名规范化。服务端账号为 3-64 字符且不限制字符集（可能是中文），
        /// 因此这里只拒绝会破坏 URL 路径/查询的字符与空白，不再套用 v1 授权码的
        /// [A-Za-z0-9_-] 限制。
        /// </summary>
        internal static string NormalizeAccount(string value)
        {
            string account = (value ?? "").Trim();
            if (account.Length == 0) throw new ArgumentException("请输入账号。", nameof(value));
            if (account.Length > 128)
                throw new ArgumentException("账号不能超过 128 个字符。", nameof(value));
            foreach (char character in account)
            {
                if (char.IsControl(character) || char.IsWhiteSpace(character)
                    || character == '/' || character == '\\' || character == '?'
                    || character == '#' || character == '&' || character == '=')
                    throw new ArgumentException("账号包含不允许的字符（空白或 / \\ ? # & =）。",
                        nameof(value));
            }
            return account;
        }

        internal static bool IsExpectedEndpoint(Uri uri)
            => uri != null
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Host, LicenseHost,
                    StringComparison.OrdinalIgnoreCase)
                && uri.Port == 443;

        private static string DecodeHost()
        {
            // 主机名在这里按字节 XOR 0x5A 保存，避免明文域名被字符串扫描/替换。
            // uncad.aosa.me 的每个 ASCII 字节异或 0x5A：
            //   u 0x75^0x5A=0x2F=47   n 0x6E^0x5A=0x34=52   c 0x63^0x5A=0x39=57
            //   a 0x61^0x5A=0x3B=59   d 0x64^0x5A=0x3E=62   . 0x2E^0x5A=0x74=116
            //   a=59  o 0x6F^0x5A=0x35=53  s 0x73^0x5A=0x29=41  a=59
            //   . =116  m 0x6D^0x5A=0x37=55  e 0x65^0x5A=0x3F=63
            // 解码后必须等于 "uncad.aosa.me"，并与 IsExpectedEndpoint 的
            // https + 443 + 不回跳校验配套使用（该边界不得放宽）。
            byte[] encoded = { 47, 52, 57, 59, 62, 116, 59, 53, 41, 59, 116, 55, 63 };
            var characters = new char[encoded.Length];
            for (int index = 0; index < encoded.Length; index++)
                characters[index] = (char)(encoded[index] ^ 0x5A);
            return new string(characters);
        }

        internal static OnlineLicenseState ParseResponse(string json,
            DateTimeOffset serverNow, string expectedAccount)
            => ParseResponse(json, serverNow, expectedAccount, null);

        internal static OnlineLicenseState ParseResponse(string json,
            DateTimeOffset serverNow, string expectedAccount, string expectedDeviceId)
        {
            OnlineLicenseDocument document;
            try
            {
                document = new JavaScriptSerializer()
                    .Deserialize<OnlineLicenseDocument>((json ?? "").TrimStart('\uFEFF'));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("授权文件不是有效 JSON。", ex);
            }
            if (document == null || document.SchemaVersion != 1)
                throw new InvalidDataException("授权文件版本无效。");
            if (!string.Equals(document.Product, "UNCAD Pro", StringComparison.Ordinal))
                throw new InvalidDataException("授权文件产品无效。");
            if (!string.Equals(document.AuthorizationCode, expectedAccount,
                StringComparison.Ordinal))
                throw new InvalidDataException("授权文件中的账号与请求不一致。");
            if (string.IsNullOrWhiteSpace(document.CustomerCode))
                throw new InvalidDataException("授权文件缺少客户代码。");
            if (!Enum.TryParse(document.LicenseMode, true, out LicenseMode licenseMode))
                throw new InvalidDataException("授权文件授权模式无效。");
            // 服务端回显本机设备 ID；老服务端不返回该字段（为空）时容忍。
            string echoDevice = (document.DeviceId ?? "").Trim();
            if (echoDevice.Length > 0 && !string.IsNullOrEmpty(expectedDeviceId)
                && !string.Equals(echoDevice, expectedDeviceId, StringComparison.Ordinal))
                throw new InvalidDataException("授权响应中的设备标识与本机不一致。");

            DateTimeOffset? expiresAt = null;
            if (!string.IsNullOrWhiteSpace(document.ExpiresAt))
            {
                if (!DateTimeOffset.TryParse(document.ExpiresAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTimeOffset parsed))
                    throw new InvalidDataException("授权文件到期时间无效。");
                expiresAt = parsed;
            }
            if (licenseMode != LicenseMode.Perpetual && !expiresAt.HasValue)
                throw new InvalidDataException("授权文件缺少到期时间。");

            string status = (document.Status ?? "").Trim().ToLowerInvariant();
            bool expired = expiresAt.HasValue && expiresAt.Value <= serverNow;
            bool active = status == "active" && !expired;
            return new OnlineLicenseState
            {
                HasResponse = true,
                IsActive = active,
                Status = status.Length == 0 ? "unknown" : status,
                LicenseMode = licenseMode,
                ExpiresAt = expiresAt,
                ServerTime = serverNow,
                CustomerCode = document.CustomerCode.Trim(),
                Company = document.Company ?? "",
                Licensee = document.Licensee ?? "",
                ExpectedAuthorizationYears = document.ExpectedAuthorizationYears,
                MessageTitle = string.IsNullOrWhiteSpace(document.MessageTitle)
                    ? "UNCAD 通知" : document.MessageTitle.Trim(),
                MessageLevel = (document.MessageLevel ?? "warning").Trim().ToLowerInvariant(),
                Message = (document.Message ?? "").Trim(),
                DeviceId = echoDevice,
                ActiveDeviceName = (document.ActiveDeviceName ?? "").Trim(),
                SignedInAt = (document.SignedInAt ?? "").Trim(),
                DeviceLimit = document.DeviceLimit,
                FailureReason = DescribeFailure(status, document, expiresAt, expired)
            };
        }

        /// <summary>
        /// 拒绝原因（中文）。服务端已写好针对性 Message 时优先采用一手文案
        /// （例如被踢提示里的设备名与时间）；否则用客户端兜底文案，保证任何
        /// 非 active 状态都有明确、可展示的中文原因。
        /// </summary>
        private static string DescribeFailure(string status, OnlineLicenseDocument document,
            DateTimeOffset? expiresAt, bool expired)
        {
            string fallback;
            switch (status)
            {
                case "active":
                    fallback = expired && expiresAt.HasValue
                        ? "服务器授权已于 " + FormatExpiry(expiresAt.Value) + " 到期。"
                        : "";
                    break;
                case "device-kicked":
                    string device = string.IsNullOrWhiteSpace(document.ActiveDeviceName)
                        ? "其它设备" : document.ActiveDeviceName.Trim();
                    string signedIn = string.IsNullOrWhiteSpace(document.SignedInAt)
                        ? "未知时间" : document.SignedInAt.Trim();
                    fallback = "本账号已在设备「" + device + "」于 " + signedIn
                        + " 登录，本机授权已停止。如需在本机使用，请重新登录以在此设备上线。";
                    break;
                case "token-invalid":
                    fallback = "本机登录凭证已失效（可能已在其它设备重新登录），请重新登录 UNCAD 账号。";
                    break;
                case "unbound":
                    fallback = "该设备尚未绑定到本账号，请在本机登录以完成绑定。";
                    break;
                case "device-missing":
                    fallback = "缺少设备标识，请在本机重新登录 UNCAD 账号。";
                    break;
                case "unknown":
                    fallback = "账号不存在或已被删除，请联系 " + ProductMetadata.CompanyName + "。";
                    break;
                case "expired":
                    fallback = expiresAt.HasValue
                        ? "服务器授权已于 " + FormatExpiry(expiresAt.Value) + " 到期。"
                        : "服务器授权已到期。";
                    break;
                case "suspended":
                    fallback = "账号授权已被暂停，请联系 " + ProductMetadata.CompanyName + "。";
                    break;
                case "revoked":
                    fallback = "授权已被吊销或本设备已被管理员禁止使用，请联系 "
                        + ProductMetadata.CompanyName + "。";
                    break;
                default:
                    fallback = "服务器授权状态为“"
                        + (status.Length == 0 ? "unknown" : status) + "”。";
                    break;
            }
            if (string.Equals(status, "active", StringComparison.Ordinal)) return fallback;
            string message = (document.Message ?? "").Trim();
            return message.Length > 0 ? message : fallback;
        }

        private static void QueueNotice(OnlineLicenseState state)
        {
            if (string.IsNullOrWhiteSpace(state.Message))
            {
                _pendingNotice = null;
                _lastNoticeKey = null;
                return;
            }
            var notice = new OnlineLicenseNotice
            {
                Title = state.MessageTitle,
                Level = state.MessageLevel,
                Message = state.Message
            };
            string key = NoticeKey(notice);
            if (key != _lastNoticeKey
                && (_pendingNotice == null || key != NoticeKey(_pendingNotice)))
                _pendingNotice = notice;
        }

        private static string NoticeKey(OnlineLicenseNotice notice)
            => notice.Title + "\n" + notice.Level + "\n" + notice.Message;

        private static OnlineLicenseState Pending(string reason)
            => new OnlineLicenseState
            {
                HasResponse = false,
                IsActive = false,
                Status = "pending",
                FailureReason = reason
            };

        private static DateTimeOffset ReadServerTime(HttpWebResponse response)
        {
            // 到期判定只信服务器时间。Date 头缺失/不可解析时必须失败——
            // 退回本地时钟会让回拨的机器拿到"未到期"的假结论。
            string value = response.Headers[HttpResponseHeader.Date];
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
                throw new InvalidDataException("授权服务器响应缺少有效时间戳。");
            return parsed;
        }

        private static string ReadBody(Stream input)
        {
            if (input == null) throw new IOException("授权服务器没有返回内容。");
            using (input)
            using (var output = new MemoryStream())
            {
                var buffer = new byte[4096];
                int total = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > MaximumBytes)
                        throw new InvalidDataException("授权文件超过 64 KB 限制。");
                    output.Write(buffer, 0, read);
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private static string FormatExpiry(DateTimeOffset value)
            => value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'zzz", CultureInfo.InvariantCulture)
                .Replace("UTC+08:00", "UTC+8");
    }
}
