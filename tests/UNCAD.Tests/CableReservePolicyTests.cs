using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class CableReservePolicyTests
    {
        [Theory]
        [InlineData("I-Line盘", 3400d)]
        [InlineData("Iline盘", 3400d)]
        [InlineData("I-Line 盘", 3400d)]
        [InlineData("插座盘", 1200d)]
        [InlineData("母线插接口", 500d)]
        [InlineData("插接口", 500d)]
        [InlineData("busbar connector", 500d)]
        [InlineData("", 0d)]
        [InlineData("普通设备", 0d)]
        public void PanelReserveMillimeters_ResolvesByUpstreamType(string next, double expected)
        {
            Assert.Equal(expected, CableReservePolicy.PanelReserveMillimeters(next));
        }

        [Fact]
        public void Apply_AddsPanelAndDeviceReserveToMeasuredCable()
        {
            var statistics = new CableStatResult { CableSum = 10d };
            statistics.CableFormatted.Add("10");

            CableReserveInfo reserve = CableReservePolicy.Apply(statistics, "插座盘", 500d);

            Assert.Equal(10d, reserve.MeasuredMeters);
            Assert.Equal(1200d, reserve.PanelReserveMillimeters);
            Assert.Equal(500d, reserve.DeviceReserveMillimeters);
            Assert.Equal(11.7d, reserve.TotalMeters, 3);
            Assert.Equal(11.7d, statistics.CableSum, 3);
            Assert.Equal(10d, statistics.MeasuredCableSum, 3);
            Assert.True(statistics.CableReserveApplied);
            Assert.Single(statistics.CableFormatted);
        }

        [Fact]
        public void Apply_NoReserve_LeavesMeasuredValueUntouched()
        {
            var statistics = new CableStatResult { CableSum = 8.5d };
            statistics.CableFormatted.Add("8.5");

            CableReserveInfo reserve = CableReservePolicy.Apply(statistics, "普通设备", 0d);

            Assert.Equal(8.5d, reserve.TotalMeters, 3);
            Assert.Equal(8.5d, statistics.CableSum, 3);
            Assert.Single(statistics.CableFormatted);
        }

        [Fact]
        public void Apply_NegativeOrInvalidDeviceReserve_ClampsToZero()
        {
            var statistics = new CableStatResult { CableSum = 1d };
            CableReserveInfo negative = CableReservePolicy.Apply(statistics, "I-Line盘", -500d);
            Assert.Equal(0d, negative.DeviceReserveMillimeters);
            Assert.Equal(4.4d, negative.TotalMeters, 3);

            var second = new CableStatResult { CableSum = 1d };
            CableReserveInfo nan = CableReservePolicy.Apply(second, "I-Line盘", double.NaN);
            Assert.Equal(0d, nan.DeviceReserveMillimeters);
        }
    }
}
