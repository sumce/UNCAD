using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace UNCAD.Infra
{
    /// <summary>
    /// 防回拨时钟:维护"最后见过的最大时间"水位线,持久化到设置、注册表
    /// 和本地标记文件三处;当前时间明显早于任一水位线或程序构建时间时,
    /// 判定为时钟回拨。有效时间取 max(当前时间, 全部水位线, 构建时间),
    /// 使回拨无法延长试用期。
    /// </summary>
    public static class TrustedClock
    {
        private const string RegistryPath = "SOFTWARE\\UNSIAO\\UNCAD";
        private const string RegistryValueName = "ClockWatermarkUtc";
        private const string MarkerFormat = "yyyyMMddHHmmss";
        /// <summary>容差:正常的小幅时钟偏差/NTP 校正不触发回拨判定。</summary>
        private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(90);

        /// <summary>读取当前时间并更新水位线;clockTampered 表示检测到回拨。</summary>
        public static DateTime NowUtc(out bool clockTampered)
        {
            DateTime now = DateTime.UtcNow;
            DateTime? build = BuildTimestampUtc();
            return Evaluate(now, build, ReadMarkers(), out clockTampered);
        }

        /// <summary>纯计算核心,供测试:输入当前时间/构建时间/已有水位线。</summary>
        public static DateTime Evaluate(DateTime nowUtc, DateTime? buildUtcUtc,
            IEnumerable<DateTime> storedMarkers, out bool clockTampered)
        {
            clockTampered = false;
            DateTime effective = nowUtc;
            if (buildUtcUtc.HasValue)
            {
                if (nowUtc < buildUtcUtc.Value - Tolerance) clockTampered = true;
                if (buildUtcUtc.Value > effective) effective = buildUtcUtc.Value;
            }
            foreach (DateTime marker in storedMarkers ?? Array.Empty<DateTime>())
            {
                if (marker == DateTime.MinValue) continue;
                if (nowUtc < marker - Tolerance) clockTampered = true;
                if (marker > effective) effective = marker;
            }
            WriteMarkers(effective);
            return effective;
        }

        internal static IEnumerable<DateTime> ReadMarkers()
        {
            var markers = new List<DateTime>();
            string settings = Settings.Get(ConfigKeys.ClockWatermarkUtc, "");
            if (TryParseMarker(settings, out DateTime fromSettings))
                markers.Add(fromSettings);
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    string value = Convert.ToString(key?.GetValue(RegistryValueName)) ?? "";
                    if (TryParseMarker(value, out DateTime fromRegistry))
                        markers.Add(fromRegistry);
                }
            }
            catch { /* 注册表不可用时忽略该来源 */ }
            string file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UNSIAO", "UNCAD", "clock.marker");
            try
            {
                if (File.Exists(file)
                    && TryParseMarker(File.ReadAllText(file).Trim(), out DateTime fromFile))
                    markers.Add(fromFile);
            }
            catch { /* 文件不可读时忽略该来源 */ }
            return markers;
        }

        internal static void WriteMarkers(DateTime watermark)
        {
            string text = watermark.ToUniversalTime().ToString(MarkerFormat,
                CultureInfo.InvariantCulture);
            try { Settings.Set(ConfigKeys.ClockWatermarkUtc, text); } catch { }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                    key?.SetValue(RegistryValueName, text);
            }
            catch { }
            try
            {
                string file = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UNSIAO", "UNCAD", "clock.marker");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, text);
            }
            catch { }
        }

        private static bool TryParseMarker(string value, out DateTime marker)
            => DateTime.TryParseExact(value ?? "", MarkerFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal
                | DateTimeStyles.AdjustToUniversal, out marker)
                && marker > new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>从程序集 InformationalVersion(2.2.0+build.yyyyMMddHHmmss)解析构建时间。</summary>
        internal static DateTime? BuildTimestampUtc()
        {
            try
            {
                string informational = (typeof(TrustedClock).Assembly
                    .GetCustomAttributes(false))
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()?.InformationalVersion ?? "";
                int marker = informational.IndexOf("+build.", StringComparison.Ordinal);
                if (marker < 0) return null;
                string timestamp = informational.Substring(marker + "+build.".Length).Trim();
                if (timestamp.Length < 14) timestamp = timestamp.PadRight(14, '0');
                if (DateTime.TryParseExact(timestamp.Substring(0, 14), MarkerFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal
                    | DateTimeStyles.AdjustToUniversal, out DateTime build))
                    return build;
            }
            catch { }
            return null;
        }
    }
}
