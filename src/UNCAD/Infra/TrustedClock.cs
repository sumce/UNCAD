using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace UNCAD.Infra
{
    /// <summary>
    /// 单调不减的本地可信时间 + 时钟回拨迹象。
    ///
    /// 定位变化（取消离线支持后）：授权到期判定**只信服务器响应的 Date 头**，
    /// 本地时间不再参与任何"延长可用期"的判定，因此这里**不承担离线宽限职责**。
    /// 它现在只做两件事：给本地时间一个单调基准、并在检测到回拨时提供一句
    /// 明确的提示文案（见 <c>ProductMetadata.EvaluateOnlineLicense</c>）。
    ///
    /// 实现：维护"最后见过的最大时间"水位线，持久化到设置、注册表和本地标记
    /// 文件三处；当前时间明显早于任一水位线或程序构建时间时判定为回拨。
    ///
    /// 2026-11-04 移除 NTP 联网校时，恢复"绘图命令永不联网"硬约束；回拨判定
    /// 只依据水位线（设置+注册表+标记文件三处）+ 构建时间兜底。到期判定不受
    /// 影响——它只信许可服务器响应的 Date 头。
    /// </summary>
    public static class TrustedClock
    {
        private const string RegistryPath = "SOFTWARE\\UNSIAO\\UNCAD";
        private const string RegistryValueName = "ClockWatermarkUtc";
        private const string MarkerFormat = "yyyyMMddHHmmss";
        /// <summary>容差:正常的小幅时钟偏差不触发回拨判定。</summary>
        private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(90);

        /// <summary>读取当前时间并更新水位线;clockTampered 表示检测到回拨。</summary>
        public static DateTime NowUtc(out bool clockTampered)
        {
            DateTime now = DateTime.UtcNow;
            DateTime? build = BuildTimestampUtc();
            DateTime effective = Evaluate(now, build, ReadMarkers(), out clockTampered);
            // Only a real clock read advances the persistent watermark. Evaluate is also
            // the deterministic calculation API used by tests and must not mutate user state.
            WriteMarkers(effective);
            return effective;
        }

        /// <summary>
        /// 纯计算核心:回拨判定只依据水位线(设置+注册表+标记文件三处)+ 构建
        /// 时间兜底;有效时间取 max(本地, 构建, 水位线),使"断网改时间"与
        /// "改了时间再断网"都无法延长试用期。
        /// 2026-11-04 移除 NTP 联网,恢复"绘图命令永不联网"硬约束;到期判定
        /// 不受影响——它只信许可服务器响应的 Date 头。
        /// </summary>
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
            // 水位线是防回拨的根基：任一来源写失败必须留痕，否则时钟校验
            // 会无痕降级成"只有内存值"。
            try { Settings.Set(ConfigKeys.ClockWatermarkUtc, text); }
            catch (Exception ex) { Log.Warn("时钟水位线写入设置失败: " + ex.Message); }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                    key?.SetValue(RegistryValueName, text);
            }
            catch (Exception ex) { Log.Warn("时钟水位线写入注册表失败: " + ex.Message); }
            try
            {
                string file = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UNSIAO", "UNCAD", "clock.marker");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, text);
            }
            catch (Exception ex) { Log.Warn("时钟水位线写入标记文件失败: " + ex.Message); }
        }

        private static bool TryParseMarker(string value, out DateTime marker)
            => DateTime.TryParseExact(value ?? "", MarkerFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal
                | DateTimeStyles.AdjustToUniversal, out marker)
                && marker > new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>从程序集 InformationalVersion(2.2.3+build.yyyyMMddHHmmss)解析构建时间。</summary>
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
            catch (Exception ex)
            {
                Log.Warn("解析程序集构建时间失败: " + ex.Message);
            }
            return null;
        }
    }
}
