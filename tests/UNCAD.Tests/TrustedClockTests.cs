using System;
using System.Collections.Generic;
using System.IO;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class TrustedClockTests
    {
        private static readonly DateTime Build =
            new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Evaluate_NormalClock_ReturnsNowWithoutTamperFlag()
        {
            DateTime now = Evaluate(Build, out bool tampered);
            Assert.False(tampered);
            Assert.Equal(Build, now);
        }

        [Fact]
        public void Evaluate_RollbackBeforeBuild_IsTampered()
        {
            var rollback = Build.AddDays(-2);
            DateTime effective = Evaluate(rollback, out bool tampered,
                markers: new List<DateTime>());
            Assert.True(tampered);
            // 有效时间被推回构建时间,回拨无法延长试用期。
            Assert.Equal(Build, effective);
        }

        [Fact]
        public void Evaluate_RollbackBeforeStoredWatermark_IsTamperedAndClamped()
        {
            var watermark = Build.AddDays(3);
            var rollback = watermark.AddDays(-2);
            DateTime effective = Evaluate(rollback, out bool tampered,
                markers: new List<DateTime> { watermark });
            Assert.True(tampered);
            Assert.Equal(watermark, effective);
        }

        [Fact]
        public void Evaluate_SmallClockJitter_WithinToleranceIsNotTampered()
        {
            var watermark = Build.AddDays(3);
            var slightlyBehind = watermark.AddMinutes(-89);
            DateTime effective = Evaluate(slightlyBehind, out bool tampered,
                markers: new List<DateTime> { watermark });
            Assert.False(tampered);
            // 容差内不判定回拨,但有效时间仍不早于水位线。
            Assert.Equal(watermark, effective);
        }

        [Fact]
        public void Evaluate_ForwardClock_AdvancesWatermark()
        {
            var future = Build.AddDays(5);
            DateTime effective = Evaluate(future, out bool tampered,
                markers: new List<DateTime> { Build });
            Assert.False(tampered);
            Assert.Equal(future, effective);
        }

        // 2026-11-04:NTP 联网已按"绘图命令永不联网"硬约束移除,
        // 依赖 networkTimeUtc 重载的两个用例(Evaluate_NetworkTime_*)随之删除。

        [Fact]
        public void Evaluate_OfflineWithClearedMarkers_FallsBackToBuildFloor()
        {
            // 全部水位线被删且离线:构建时间下限仍兜底。
            var faked = Build.AddHours(-48);
            DateTime effective = TrustedClock.Evaluate(faked, Build,
                new List<DateTime>(), out bool tampered);
            Assert.True(tampered);
            Assert.Equal(Build, effective);
        }

        [Fact]
        public void Evaluate_IsPureAndDoesNotPersistTestWatermark()
        {
            // Evaluation must not write a future marker into the user's real profile.
            DateTime effective = Evaluate(Build.AddDays(5), out bool tampered,
                markers: new List<DateTime> { Build });
            Assert.False(tampered);
            Assert.Equal(Build.AddDays(5), effective);
        }

        [Fact]
        public void TrustedClock_StaysOffline_NetworkMembersMustNotReturn()
        {
            // 硬约束（AGENTS.md）：绘图命令永不联网，账号/许可校验是唯一网络功能。
            // TrustedClock 在命令路径上被调用，绝不允许重新长出 NTP/HTTP 等
            // 联网成员（2026-11-04 已摘除 SNTP 校时；到期判定只信许可服务器
            // 响应的 Date 头，不依赖本地联网）。
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "UNCAD", "Infra", "TrustedClock.cs"));
            string source = File.ReadAllText(path);
            Assert.DoesNotContain("Socket", source);
            Assert.DoesNotContain("Dns", source);
            Assert.DoesNotContain("UdpClient", source);
            Assert.DoesNotContain("TcpClient", source);
            Assert.DoesNotContain("HttpWebRequest", source);
            Assert.DoesNotContain("WebClient", source);
            Assert.DoesNotContain("HttpClient", source);
            Assert.DoesNotContain("ThreadPool", source);
            Assert.DoesNotContain("NetworkStream", source);
        }

        private static DateTime Evaluate(DateTime now, out bool tampered,
            List<DateTime> markers = null)
            => TrustedClock.Evaluate(now, Build, markers, out tampered);
    }
}
