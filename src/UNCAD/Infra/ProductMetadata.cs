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
        public const string ReleaseDateUtc = "2026-08-30";
        public const string VersionLabel = "2.1.6";

        // Temporary builds use a separate configuration and package; Release remains perpetual.
#if UNCAD_TEMPORARY_LICENSE
        public static readonly string BuildConfiguration = "Temporary";
        public static readonly LicenseMode BuildLicenseMode = LicenseMode.Trial;
        public const string BuildLicenseExpiresUtc = "2026-09-08T23:59:59+08:00";
#else
        public static readonly string BuildConfiguration = "Release";
        public static readonly LicenseMode BuildLicenseMode = LicenseMode.Perpetual;
        public const string BuildLicenseExpiresUtc = "";
#endif

        public static string Version
        {
            get { return VersionLabel; }
        }

        public static LicenseSnapshot CurrentLicense()
            => EvaluateLicense(BuildLicenseMode, BuildLicenseExpiresUtc, DateTime.UtcNow);

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
