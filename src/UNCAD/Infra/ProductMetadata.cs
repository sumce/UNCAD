using System;
using System.Globalization;

namespace UNCAD.Infra
{
    public enum LicenseMode
    {
        Perpetual,
        Trial,
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

#if UNCAD_JSWY_LICENSE
        public const string BuildCustomerCode = "UNCAD-JSWY";
        public const string LicenseeCompany = "江苏文炎建设工程有限公司";
        public const string LicenseeName = "李小亮";
        public static readonly int? ExpectedAuthorizationYears = 10;
#else
        public const string BuildCustomerCode = "UNCAD";
        public const string LicenseeCompany = "";
        public const string LicenseeName = "";
        public static readonly int? ExpectedAuthorizationYears = null;
#endif

        // Customer and temporary builds use separate configurations and packages; Release remains perpetual.
#if UNCAD_JSWY_LICENSE
        public static readonly string BuildConfiguration = "JSWY";
        public static readonly LicenseMode BuildLicenseMode = LicenseMode.Project;
        public const string BuildLicenseExpiresUtc = "2026-10-01T00:00:00+08:00";
#elif UNCAD_TEMPORARY_LICENSE
        public static readonly string BuildConfiguration = "Temporary";
        public static readonly LicenseMode BuildLicenseMode = LicenseMode.Trial;
        public const string BuildLicenseExpiresUtc = "2026-09-03T00:00:00+08:00";
#else
        public static readonly string BuildConfiguration = "Release";
        public static readonly LicenseMode BuildLicenseMode = LicenseMode.Perpetual;
        public const string BuildLicenseExpiresUtc = "";
#endif

        public static string Version
        {
            get { return VersionLabel; }
        }

        public static string DisplayName
        {
            get { return BuildCustomerCode == ProductName ? ProductName : BuildCustomerCode; }
        }

        public static LicenseSnapshot CurrentLicense()
        {
            // 防回拨时钟:有效时间取水位线最大值,检测到回拨立即视为过期。
            DateTime now = TrustedClock.NowUtc(out bool clockTampered);
            LicenseSnapshot license = EvaluateLicense(
                BuildLicenseMode, BuildLicenseExpiresUtc, now);
            if (clockTampered && !license.IsExpired)
            {
                license.IsExpired = true;
                license.StatusText = "已过期 · 检测到系统时间被回拨";
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
                    ? "正式版 · 无期限" : mode == LicenseMode.Trial
                        ? "试用版 · 有效" : "项目授权版 · 有效",
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
                throw new InvalidOperationException("授权已于 " + license.ExpiryText
                    + " 到期，命令“" + (command ?? "") + "”已停止执行。请联系 " + CompanyName
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
