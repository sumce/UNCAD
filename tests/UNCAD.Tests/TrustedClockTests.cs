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

        private static DateTime Evaluate(DateTime now, out bool tampered,
            List<DateTime> markers = null)
            => TrustedClock.Evaluate(now, Build, markers, out tampered);
    }
}
