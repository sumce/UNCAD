using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace UNCAD.Infra
{
    public sealed class AboutInfo
    {
        public string Version { get; set; }
        public string BuildTime { get; set; }
        public string UpdatedOn { get; set; }
        public string Authorization { get; set; }

        public static AboutInfo Current()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo file = FileVersionInfo.GetVersionInfo(assembly.Location);
            return new AboutInfo
            {
                Version = file.FileVersion ?? assembly.GetName().Version?.ToString() ?? "未知",
                BuildTime = ParseBuildTime(file.ProductVersion),
                UpdatedOn = Branding.ReleaseUpdatedOn,
                Authorization = Branding.Edition
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
