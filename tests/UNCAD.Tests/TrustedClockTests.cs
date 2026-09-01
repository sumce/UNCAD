using System;
using System.Collections.Generic;
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

        [Fact]
        public void Evaluate_NetworkTime_ClampsLocalRollback()
        {
            // 断网改时间再上线:网络时间远大于被回拨的本地时间。
            var network = Build.AddDays(4);
            var rolledBack = Build.AddDays(-1);
            DateTime effective = TrustedClock.Evaluate(rolledBack, Build,
                new List<DateTime>(), out bool tampered, network);
            Assert.True(tampered);
            Assert.Equal(network, effective);
        }

        [Fact]
        public void Evaluate_NetworkTime_AuthoritativeEvenWithoutMarkers()
        {
            // 用户清空了所有水位线:只要在线,真实时间仍然是权威。
            var network = Build.AddDays(9);
            var faked = Build.AddDays(2);
            DateTime effective = TrustedClock.Evaluate(faked, Build,
                new List<DateTime>(), out bool tampered, network);
            Assert.True(tampered); // 本地落后网络 7 天 → 判回拨
            Assert.Equal(network, effective);
        }

        [Fact]
        public void Evaluate_OfflineWithClearedMarkers_FallsBackToBuildFloor()
        {
            // 全部水位线被删且离线:构建时间下限仍兜底。
            var faked = Build.AddHours(-48);
            DateTime effective = TrustedClock.Evaluate(faked, Build,
                new List<DateTime>(), out bool tampered, null);
            Assert.True(tampered);
            Assert.Equal(Build, effective);
        }

        private static DateTime Evaluate(DateTime now, out bool tampered,
            List<DateTime> markers = null)
            => TrustedClock.Evaluate(now, Build, markers, out tampered);
    }
}
