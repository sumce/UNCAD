using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
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
        private static readonly string[] NtpHosts =
        {
            "ntp.aliyun.com", "ntp.tencent.com", "time.windows.com", "pool.ntp.org"
        };
        /// <summary>每个进程最多每 30 分钟联网同步一次;其余时间用本地+水位线。</summary>
        private static readonly TimeSpan NetworkSyncInterval = TimeSpan.FromMinutes(30);
        private static DateTime _lastNetworkAttemptUtc = DateTime.MinValue;
        private static DateTime? _lastNetworkTimeUtc;
        private static readonly object NetworkSync = new object();
        private static bool _networkSyncRunning;

        /// <summary>读取当前时间并更新水位线;clockTampered 表示检测到回拨。</summary>
        public static DateTime NowUtc(out bool clockTampered)
        {
            DateTime now = DateTime.UtcNow;
            DateTime? build = BuildTimestampUtc();
            DateTime? network = CachedNetworkTimeAndStartRefresh();
            DateTime effective = Evaluate(now, build, ReadMarkers(), out clockTampered, network);
            // Only a real clock read advances the persistent watermark. Evaluate is also
            // the deterministic calculation API used by tests and must not mutate user state.
            WriteMarkers(effective);
            return effective;
        }

        /// <summary>纯计算核心,供测试:输入当前时间/构建时间/已有水位线。</summary>
        public static DateTime Evaluate(DateTime nowUtc, DateTime? buildUtcUtc,
            IEnumerable<DateTime> storedMarkers, out bool clockTampered)
            => Evaluate(nowUtc, buildUtcUtc, storedMarkers, out clockTampered, null);

        /// <summary>
        /// 完整计算:网络时间(若在线)作为权威上限 — 本地时间落后网络时间
        /// 超过容差即为回拨;有效时间取 max(本地, 构建, 水位线, 网络时间),
        /// 使"断网改时间"与"改了时间再断网"都无法延长试用期。
        /// </summary>
        public static DateTime Evaluate(DateTime nowUtc, DateTime? buildUtcUtc,
            IEnumerable<DateTime> storedMarkers, out bool clockTampered,
            DateTime? networkTimeUtc)
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
            if (networkTimeUtc.HasValue)
            {
                // 网络时间是权威值:本地明显落后即回拨;有效时间不早于网络时间。
                if (nowUtc < networkTimeUtc.Value - Tolerance
                    || effective < networkTimeUtc.Value - Tolerance)
                    clockTampered = true;
                if (networkTimeUtc.Value > effective) effective = networkTimeUtc.Value;
            }
            return effective;
        }

        /// <summary>联网获取权威时间;失败(离线)返回 null,退回本地+水位线。</summary>
        private static DateTime? CachedNetworkTimeAndStartRefresh()
        {
            DateTime now = DateTime.UtcNow;
            DateTime? cached;
            bool startRefresh = false;
            lock (NetworkSync)
            {
                cached = _lastNetworkTimeUtc;
                if (!_networkSyncRunning
                    && now - _lastNetworkAttemptUtc >= NetworkSyncInterval)
                {
                    _lastNetworkAttemptUtc = now;
                    _networkSyncRunning = true;
                    startRefresh = true;
                }
            }
            if (startRefresh)
                ThreadPool.QueueUserWorkItem(_ => RefreshNetworkTime());
            return cached;
        }

        private static void RefreshNetworkTime()
        {
            try
            {
                DateTime? network = TryGetNetworkTimeUtc();
                if (!network.HasValue) return;
                lock (NetworkSync) _lastNetworkTimeUtc = network;
                DateTime effective = Evaluate(DateTime.UtcNow, BuildTimestampUtc(),
                    ReadMarkers(), out _, network);
                WriteMarkers(effective);
            }
            catch (Exception ex)
            {
                Log.Warn("后台网络校时失败: " + ex.Message);
            }
            finally
            {
                lock (NetworkSync) _networkSyncRunning = false;
            }
        }

        private static DateTime? TryGetNetworkTimeUtc()
        {
            foreach (string host in NtpHosts)
            {
                try
                {
                    DateTime? time = QueryNtp(host, TimeSpan.FromSeconds(1.5));
                    if (time.HasValue) return time;
                }
                catch { /* 单个 NTP 服务器失败则尝试下一个 */ }
            }
            return null;
        }

        /// <summary>SNTP 客户端:UDP 123 端口,解析服务器 TransmitTimestamp。</summary>
        private static DateTime? QueryNtp(string host, TimeSpan timeout)
        {
            // LI=0,VN=4,Mode=3(客户端);48 字节标准 NTP 报文。
            var packet = new byte[48];
            packet[0] = 0x1B;
            using (var socket = new System.Net.Sockets.Socket(
                AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.ReceiveTimeout = (int)timeout.TotalMilliseconds;
                socket.SendTimeout = (int)timeout.TotalMilliseconds;
                var addresses = System.Net.Dns.GetHostAddresses(host);
                var endpoint = new System.Net.IPEndPoint(
                    addresses.FirstOrDefault(address =>
                        address.AddressFamily == AddressFamily.InterNetwork)
                    ?? addresses[0], 123);
                socket.SendTo(packet, endpoint);
                var buffer = new byte[48];
                System.Net.EndPoint remote = endpoint;
                int received = socket.ReceiveFrom(buffer, ref remote);
                if (received < 48) return null;
                // TransmitTimestamp 在第 40-47 字节:前 4 字节秒,后 4 字节分数。
                uint seconds = BitConverter.ToUInt32(
                    new[] { buffer[43], buffer[42], buffer[41], buffer[40] }, 0);
                if (seconds == 0) return null;
                var ntpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return ntpEpoch.AddSeconds(seconds - 2208988800L);
            }
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

        /// <summary>从程序集 InformationalVersion(2.2.1+build.yyyyMMddHHmmss)解析构建时间。</summary>
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
