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
        public const string ReleaseDateUtc = "2026-09-02";
        public const string VersionLabel = "2.2.1";

        public static string Version
        {
            get { return VersionLabel; }
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

        public static LicenseSnapshot CurrentLicense()
        {
            // 防回拨时钟:有效时间取水位线最大值,检测到回拨立即视为过期。
            DateTime now = TrustedClock.NowUtc(out bool clockTampered);
            OnlineLicenseState online = OnlineLicenseMonitor.Current;
            LicenseMode mode = online.HasResponse
                ? online.LicenseMode : LicenseMode.Project;
            string expiresAt = online.ExpiresAt.HasValue
                ? online.ExpiresAt.Value.ToString("o", CultureInfo.InvariantCulture)
                : "";
            LicenseSnapshot license = EvaluateLicense(mode, expiresAt, now);
            string edition = mode == LicenseMode.Perpetual
                ? "正式版" : "项目授权版";
            // 宽限期：仅在"没拿到服务器结论"（pending/unavailable）时生效。
            // 服务器明确返回过期/吊销仍立即停用；时钟回拨同样不享受宽限。
            bool grace = !clockTampered && !online.IsActive
                && (online.Status == "pending" || online.Status == "unavailable")
                && OnlineLicenseMonitor.IsWithinGraceWindow();
            license.StatusText = online.IsActive && !license.IsExpired
                ? edition + " · 在线有效" : grace
                    ? edition + " · 离线宽限中（等待服务器验证）" : online.Status == "pending"
                        ? "UNCAD Pro · 在线验证中" : edition + " · 在线不可用";
            if (!online.HasResponse || (!online.IsActive && !grace))
            {
                license.IsExpired = true;
                license.BlockReason = online.FailureReason;
            }
            if (clockTampered && !license.IsExpired)
            {
                license.IsExpired = true;
                license.StatusText = "已过期 · 检测到系统时间被回拨";
                license.BlockReason = "检测到系统时间被回拨，授权验证失败。";
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

        public static void EnsureCommandAllowed(string command)
        {
            LicenseSnapshot license = CurrentLicense();
            if (license.IsExpired)
                throw new InvalidOperationException((string.IsNullOrWhiteSpace(license.BlockReason)
                    ? "授权已于 " + license.ExpiryText + " 到期。" : license.BlockReason)
                    + " 命令“" + (command ?? "") + "”已停止执行。请联系 " + CompanyName
                    + "（" + Website + "）获取有效授权。");
        }

        private static DateTimeOffset? ParseExpiry(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset parsed) ? parsed : null;
        }
    }
}
