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
        public LicenseSnapshot License { get; set; }

        public static AboutInfo Current()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo file = FileVersionInfo.GetVersionInfo(assembly.Location);
            LicenseSnapshot license = ProductMetadata.CurrentLicense();
            return new AboutInfo
            {
                ProductName = ProductMetadata.ProductName,
                Subtitle = ProductMetadata.ProductSubtitle,
                CompanyName = ProductMetadata.CompanyName,
                Website = ProductMetadata.Website,
                WebsiteUrl = ProductMetadata.WebsiteUrl,
                Copyright = ProductMetadata.Copyright,
                Version = ProductMetadata.VersionLabel,
                BuildTime = ParseBuildTime(file.ProductVersion),
                UpdatedOn = ProductMetadata.ReleaseDateUtc,
                Authorization = license.StatusText,
                License = license
            };
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
