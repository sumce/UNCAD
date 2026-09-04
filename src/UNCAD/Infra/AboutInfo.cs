using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace UNCAD.Infra
{
    public sealed class AboutInfo
    {
        public string ProductName { get; set; }
        public string Subtitle { get; set; }
        public string CompanyName { get; set; }
        public string Website { get; set; }
        public string WebsiteUrl { get; set; }
        public string Copyright { get; set; }
        public string Version { get; set; }
        public string BuildTime { get; set; }
        public string UpdatedOn { get; set; }
        public string Authorization { get; set; }
        public string CustomerCode { get; set; }
        public string LicenseeCompany { get; set; }
        public string LicenseeName { get; set; }
        public int? ExpectedAuthorizationYears { get; set; }
        public LicenseSnapshot License { get; set; }

        public static AboutInfo Current()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo file = FileVersionInfo.GetVersionInfo(assembly.Location);
            LicenseSnapshot license = ProductMetadata.CurrentLicense();
            return new AboutInfo
            {
                ProductName = ProductMetadata.DisplayName,
                Subtitle = ProductMetadata.ProductSubtitle,
                CompanyName = ProductMetadata.CompanyName,
                Website = ProductMetadata.Website,
                WebsiteUrl = ProductMetadata.WebsiteUrl,
                Copyright = ProductMetadata.Copyright,
                Version = ProductMetadata.VersionText,
                BuildTime = ParseBuildTime(file.ProductVersion),
                UpdatedOn = BuildDateOrFallback(file.ProductVersion),
                Authorization = license.StatusText,
                CustomerCode = ProductMetadata.CurrentCustomerCode,
                LicenseeCompany = ProductMetadata.CurrentLicenseeCompany,
                LicenseeName = ProductMetadata.CurrentLicenseeName,
                ExpectedAuthorizationYears = ProductMetadata.CurrentExpectedAuthorizationYears,
                License = license
            };
        }

        /// <summary>更新日期跟随实际构建时间(UTC 日期),不再依赖手工常量。</summary>
        internal static string BuildDateOrFallback(string productVersion)
        {
            const string marker = "+build.";
            int markerIndex = (productVersion ?? "").IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0
                && DateTime.TryParseExact(
                    productVersion.Substring(markerIndex + marker.Length),
                    "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime utc))
                return utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return ProductMetadata.ReleaseDateUtc;
        }

        internal static string ParseBuildTime(string productVersion)
        {
            const string marker = "+build.";
            int markerIndex = (productVersion ?? "").IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0) return "未知";
            string value = productVersion.Substring(markerIndex + marker.Length);
            if (!DateTime.TryParseExact(value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime utc))
                return "未知";
            return utc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        }
    }
}
