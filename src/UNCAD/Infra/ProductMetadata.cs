using System;
using System.Globalization;

namespace UNCAD.Infra
{
    public enum LicenseMode
    {
        Perpetual,
        Project
    }

    public sealed class LicenseSnapshot
    {
        public LicenseMode Mode { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public bool IsExpired { get; set; }
        public int? DaysRemaining { get; set; }
        public string StatusText { get; set; }
        public string ExpiryText { get; set; }
        public string BlockReason { get; set; }
    }

    public static class ProductMetadata
    {
        public const string ProductName = "UNCAD";
        public const string ProductSubtitle = "AutoCAD Engineering Tools";
        public const string CompanyName = "UNSIAO.Ltd";
        public const string Website = "www.unsiao.com";
        public const string WebsiteUrl = "https://www.unsiao.com";
        public const string Copyright = "© 2026 UNSIAO.Ltd";
        public const string ReleaseDateUtc = "2026-10-11";
        public const string VersionLabel = "2.6.3";
        /// <summary>同版本内的功能修补标记(SP1/SP2/...),每加一个功能必须递增;空=无。</summary>
        public const string VersionSuffix = "";

        public static string VersionText
            => VersionLabel + (VersionSuffix.Length == 0 ? "" : " " + VersionSuffix);

        public static string Version
        {
            get { return VersionText; }
        }

        public static string DisplayName
        {
            get { return "UNCAD Pro"; }
        }

        public static bool RequiresOnlineLicense
        {
            get { return true; }
        }

        internal static string CurrentCustomerCode
        {
            get
            {
                OnlineLicenseState online = OnlineLicenseMonitor.Current;
                return online.HasResponse && !string.IsNullOrWhiteSpace(online.CustomerCode)
                    ? online.CustomerCode : "—";
            }
        }

        internal static string CurrentLicenseeCompany
        {
            get
            {
                OnlineLicenseState online = OnlineLicenseMonitor.Current;
                return online.HasResponse
                    && !string.IsNullOrWhiteSpace(online.Company)
                        ? online.Company : "";
            }
        }

        internal static string CurrentLicenseeName
        {
            get
            {
                OnlineLicenseState online = OnlineLicenseMonitor.Current;
                return online.HasResponse
                    && !string.IsNullOrWhiteSpace(online.Licensee)
                        ? online.Licensee : "";
            }
        }

        internal static int? CurrentExpectedAuthorizationYears
        {
            get
            {
                OnlineLicenseState online = OnlineLicenseMonitor.Current;
                return online.HasResponse
                    && online.ExpectedAuthorizationYears.HasValue
                        ? online.ExpectedAuthorizationYears : null;
            }
        }

        /// <summary>
        /// 在线有效性判定。**不支持离线使用**：
        /// 只有拿到服务器明确结论且为 active 才允许执行命令；
        /// 拿不到结论（未验证 / 网络不可达）一律停用，没有"离线宽限"这回事。
        /// </summary>
        public static LicenseSnapshot CurrentLicense()
        {
            DateTime now = TrustedClock.NowUtc(out bool clockTampered);
            OnlineLicenseState online = OnlineLicenseMonitor.Current;
            return EvaluateOnlineLicense(online, now, clockTampered);
        }

        internal static LicenseSnapshot EvaluateOnlineLicense(OnlineLicenseState online,
            DateTime trustedNow, bool clockTampered)
        {
            if (online == null) throw new ArgumentNullException(nameof(online));
            LicenseMode mode = online.HasResponse
                ? online.LicenseMode : LicenseMode.Project;
            string expiresAt = online.ExpiresAt.HasValue
                ? online.ExpiresAt.Value.ToString("o", CultureInfo.InvariantCulture)
                : "";
            DateTime evaluationTime = online.HasResponse && online.ServerTime.HasValue
                ? online.ServerTime.Value.UtcDateTime : trustedNow;
            LicenseSnapshot license = EvaluateLicense(mode, expiresAt, evaluationTime);
            string edition = mode == LicenseMode.Perpetual
                ? "正式版" : "项目授权版";
            // 必须"拿到服务器结论且 active"才放行——这是取消离线支持的核心。
            bool serverActive = online.HasResponse && online.IsActive;
            license.StatusText = serverActive
                ? edition + " · 在线有效"
                : online.Status == "pending"
                    ? "UNCAD Pro · 在线验证中"
                    : online.Status == "unavailable"
                        ? "UNCAD Pro · 无法连接授权服务器"
                        : edition + " · 在线不可用";
            if (serverActive)
            {
                license.IsExpired = false;
                license.BlockReason = "";
            }
            else
            {
                license.IsExpired = true;
                if (clockTampered)
                {
                    license.StatusText = "已过期 · 检测到系统时间被回拨";
                    license.BlockReason = "检测到系统时间被回拨，授权验证失败。";
                }
                else if (!online.HasResponse
                    && (online.Status == "pending" || online.Status == "unavailable"))
                {
                    // 明确告知用户是"连不上服务器"，而不是含糊的"授权不可用"。
                    license.BlockReason = "UNCAD 需要联网验证授权，当前无法连接授权服务器"
                        + (string.IsNullOrWhiteSpace(online.FailureReason)
                            ? "。" : "：" + online.FailureReason)
                        + " 本版本不支持离线使用，请检查网络后重试。";
                }
                else license.BlockReason = online.FailureReason;
            }
            return license;
        }

        public static LicenseSnapshot EvaluateLicense(
            LicenseMode mode, string expiresAt, DateTime utcNow)
        {
            DateTimeOffset? configuredExpiry = ParseExpiry(expiresAt);
            DateTime? expires = configuredExpiry?.UtcDateTime;
            DateTime now = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
            bool expired = mode != LicenseMode.Perpetual
                && (!expires.HasValue || expires.Value <= now);
            DateTimeOffset nowAtLicenseOffset = configuredExpiry.HasValue
                ? new DateTimeOffset(now).ToOffset(configuredExpiry.Value.Offset)
                : new DateTimeOffset(now);
            int? days = configuredExpiry.HasValue
                ? (int?)Math.Max(0,
                    (configuredExpiry.Value.Date - nowAtLicenseOffset.Date).Days)
                : null;
            return new LicenseSnapshot
            {
                Mode = mode,
                ExpiresUtc = expires,
                IsExpired = expired,
                DaysRemaining = days,
                StatusText = expired ? "已过期" : mode == LicenseMode.Perpetual
                    ? "正式版 · 无期限" : "项目授权版 · 有效",
                ExpiryText = mode == LicenseMode.Perpetual
                    ? "无期限" : configuredExpiry.HasValue
                        ? configuredExpiry.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'zzz", CultureInfo.InvariantCulture)
                            .Replace("UTC+08:00", "UTC+8")
                        : "授权日期无效"
            };
        }

        /// <summary>
        /// 被停用命令的提示文案。
        /// 抽成独立方法是为了让测试能直接断言文案内容（文案本身就是行为契约的一部分：
        /// 必须给出 UNC_LOGIN 出口，且必须说明不影响 AutoCAD 本身）。
        /// </summary>
        internal static string BuildBlockedCommandMessage(string reason, string command)
        {
            string detail = string.IsNullOrWhiteSpace(reason) ? "当前账号授权不可用。" : reason;
            return detail
                + "\n请在 AutoCAD 中运行 UNC_LOGIN 登录或注册账号，然后重新执行命令“"
                + (command ?? "") + "”。"
                + "\n（授权问题只停用 UNCAD 命令，不影响 AutoCAD 本身绘图与保存。）";
        }

        public static void EnsureCommandAllowed(string command)
        {
            LicenseSnapshot license = CurrentLicense();
            if (!license.IsExpired) return;

            string reason = string.IsNullOrWhiteSpace(license.BlockReason)
                ? "授权已于 " + license.ExpiryText + " 到期。" : license.BlockReason;

            // 用户主动执行了 UNCAD 命令 → 给出**可操作的**出口并唤起登录窗口。
            // 这里弹窗不会打断用户（他本来就在用 UNCAD）；周期性心跳不会弹（见 OnlineLicenseMonitor）。
            OnlineLicenseMonitor.RequestUserLogin(reason);

            throw new InvalidOperationException(BuildBlockedCommandMessage(reason, command));
        }

        private static DateTimeOffset? ParseExpiry(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset parsed) ? parsed : null;
        }
    }
}
