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
        public string CustomerCode { get; set; }
        public string Company { get; set; }
        public string Licensee { get; set; }
        public int? ExpectedAuthorizationYears { get; set; }
        public string MessageTitle { get; set; }
        public string MessageLevel { get; set; }
        public string Message { get; set; }
        public string FailureReason { get; set; }
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
    }

    internal static class OnlineLicenseMonitor
    {
        internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
        /// <summary>
        /// 上一次在线验证成功后的宽限期：期间轮询失败（抖动/断网/服务器维护）
        /// 不禁用命令，超过宽限期才按不可用处理。水位线由 TrustedClock 背书，
        /// 回拨系统时间无法延长。仅适用于"没拿到服务器结论"的场景
        /// （pending/unavailable）；服务器明确返回过期/吊销仍立即停用。
        /// </summary>
        internal static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(30);
        private const int TimeoutMilliseconds = 8000;
        private const int MaximumBytes = 64 * 1024;
        private static readonly string LicenseHost = DecodeHost();
        private static readonly object Sync = new object();
        private static Timer _timer;
        private static int _refreshing;
        private static OnlineLicenseState _current = Pending("正在读取授权码。");
        private static OnlineLicenseNotice _pendingNotice;
        private static string _pendingActivationReason;
        private static string _pendingActivationKey;
        private static string _lastActivationKey;
        private static string _lastNoticeKey;

        internal static OnlineLicenseState Current
        {
            get { lock (Sync) return _current; }
        }

        internal static string StoredAuthorizationCode
            => (Settings.Get(ConfigKeys.OnlineAuthorizationCode, "") ?? "").Trim();

        internal static void Start()
        {
            if (!ProductMetadata.RequiresOnlineLicense) return;
            lock (Sync)
            {
                if (_timer != null) return;
                _current = Pending("正在验证在线授权。");
                _pendingNotice = null;
                _pendingActivationReason = null;
                _pendingActivationKey = null;
                _lastActivationKey = null;
                _lastNoticeKey = null;
                _timer = new Timer(Refresh, null, TimeSpan.Zero, RefreshInterval);
            }
            Log.Info("在线授权监控已启动，每 5 分钟刷新一次");
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

        internal static bool TryTakeNotice(out OnlineLicenseNotice notice)
        {
            lock (Sync)
            {
                notice = _pendingNotice;
                if (notice == null) return false;
                _pendingNotice = null;
                _lastNoticeKey = NoticeKey(notice);
                return true;
            }
        }

        /// <summary>
        /// 上次在线验证成功距今是否仍在宽限期内。时间基准取
        /// TrustedClock（水位线钳制），回拨系统时间无法借此延长授权。
        /// </summary>
        internal static bool IsWithinGraceWindow()
        {
            if (StoredAuthorizationCode.Length == 0) return false;
            string stamp = Settings.Get(ConfigKeys.LicenseLastValidatedUtc, "");
            if (!DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset last)) return false;
            DateTime now = TrustedClock.NowUtc(out bool clockTampered);
            if (clockTampered) return false;
            return now - last.UtcDateTime <= GracePeriod;
        }

        private static void WriteLastValidatedStamp()
        {
            try
            {
                Settings.Set(ConfigKeys.LicenseLastValidatedUtc,
                    TrustedClock.NowUtc(out _).ToString("o", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Log.Warn("记录授权验证水位线失败: " + ex.Message);
            }
        }

        internal static OnlineLicenseState ValidateAndSave(string authorizationCode)
        {
            if (Interlocked.Exchange(ref _refreshing, 1) != 0)
                throw new InvalidOperationException("后台授权验证正在进行，请稍后重试。");
            try
            {
                string code = NormalizeCode(authorizationCode);
                OnlineLicenseState validated = Download(code);
                if (!validated.IsActive)
                    throw new InvalidOperationException(validated.FailureReason);
                Settings.Set(ConfigKeys.OnlineAuthorizationCode, code);
                lock (Sync) Publish(validated, code);
                Log.Info("在线授权码已验证并保存");
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
            string code = StoredAuthorizationCode;
            try
            {
                if (code.Length == 0)
                {
                    lock (Sync)
                    {
                        OnlineLicenseState missing = Pending("尚未设置授权码。");
                        missing.Status = "missing";
                        Publish(missing, code);
                    }
                    return;
                }
                OnlineLicenseState refreshed = Download(code);
                lock (Sync) Publish(refreshed, code);
                Log.Info("在线授权刷新成功: " + refreshed.Status);
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
                        FailureReason = "无法验证授权码：" + ex.Message
                    }, code);
                }
                Log.Warn("在线授权刷新失败: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        private static void Publish(OnlineLicenseState state, string authorizationCode)
        {
            _current = state;
            QueueNotice(state);
            if (state.IsActive) WriteLastValidatedStamp();
            if (state.IsActive)
            {
                _pendingActivationReason = null;
                _pendingActivationKey = null;
                _lastActivationKey = null;
                return;
            }
            string reason = string.IsNullOrWhiteSpace(state.FailureReason)
                ? "当前授权码不可用。" : state.FailureReason;
            string key = (authorizationCode ?? "") + "\n" + reason;
            if (key == _lastActivationKey || key == _pendingActivationKey) return;
            _pendingActivationReason = reason;
            _pendingActivationKey = key;
        }

        private static OnlineLicenseState Download(string authorizationCode)
        {
            string code = NormalizeCode(authorizationCode);
            Uri endpoint = BuildEndpoint(code);
            var requestUri = new UriBuilder(endpoint)
            {
                Query = "_=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)
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
                    ReadServerTime(response), code);
            }
        }

        internal static Uri BuildEndpoint(string authorizationCode)
        {
            string code = NormalizeCode(authorizationCode);
            var uri = new Uri("https://" + LicenseHost + "/d/key/"
                + Uri.EscapeDataString(code) + "/pro.key", UriKind.Absolute);
            if (!IsExpectedEndpoint(uri))
                throw new InvalidDataException("授权服务器地址不受信任。");
            return uri;
        }

        internal static string NormalizeCode(string value)
        {
            string code = (value ?? "").Trim();
            if (code.Length == 0) throw new ArgumentException("请输入授权码。", nameof(value));
            if (code.Length > 128) throw new ArgumentException("授权码不能超过 128 个字符。", nameof(value));
            foreach (char character in code)
                if (!(character >= 'a' && character <= 'z')
                    && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9')
                    && character != '-' && character != '_')
                    throw new ArgumentException("授权码只能包含字母、数字、连字符和下划线。",
                        nameof(value));
            return code;
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
            byte[] encoded = { 57, 54, 53, 47, 62, 116, 59, 53, 41, 59, 116, 55, 63 };
            var characters = new char[encoded.Length];
            for (int index = 0; index < encoded.Length; index++)
                characters[index] = (char)(encoded[index] ^ 0x5A);
            return new string(characters);
        }

        internal static OnlineLicenseState ParseResponse(string json,
            DateTimeOffset serverNow, string expectedAuthorizationCode)
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
            if (!string.Equals(document.AuthorizationCode, expectedAuthorizationCode,
                StringComparison.Ordinal))
                throw new InvalidDataException("授权文件中的授权码与输入不一致。");
            if (string.IsNullOrWhiteSpace(document.CustomerCode))
                throw new InvalidDataException("授权文件缺少客户代码。");
            if (!Enum.TryParse(document.LicenseMode, true, out LicenseMode licenseMode))
                throw new InvalidDataException("授权文件授权模式无效。");

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
            string reason = active ? "" : status != "active"
                ? "服务器授权状态为“" + (status.Length == 0 ? "unknown" : status) + "”。"
                : "服务器授权已于 " + FormatExpiry(expiresAt.Value) + " 到期。";
            return new OnlineLicenseState
            {
                HasResponse = true,
                IsActive = active,
                Status = status.Length == 0 ? "unknown" : status,
                LicenseMode = licenseMode,
                ExpiresAt = expiresAt,
                CustomerCode = document.CustomerCode.Trim(),
                Company = document.Company ?? "",
                Licensee = document.Licensee ?? "",
                ExpectedAuthorizationYears = document.ExpectedAuthorizationYears,
                MessageTitle = string.IsNullOrWhiteSpace(document.MessageTitle)
                    ? "UNCAD 通知" : document.MessageTitle.Trim(),
                MessageLevel = (document.MessageLevel ?? "warning").Trim().ToLowerInvariant(),
                Message = (document.Message ?? "").Trim(),
                FailureReason = reason
            };
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
