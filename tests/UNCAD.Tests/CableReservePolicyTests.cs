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
        [InlineData("插座盘", 1000d)]
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
            Assert.Equal(1000d, reserve.PanelReserveMillimeters);
            Assert.Equal("盘内", reserve.PanelReserveLabel);
            Assert.Equal(500d, reserve.DeviceReserveMillimeters);
            // 业主统一预留默认 3000mm。
            Assert.Equal(3000d, reserve.OwnerReserveMillimeters);
            Assert.Equal(14.5d, reserve.TotalMeters, 3);
            Assert.Equal(14.5d, statistics.CableSum, 3);
            Assert.Equal(10d, statistics.MeasuredCableSum, 3);
            Assert.True(statistics.CableReserveApplied);
            Assert.Single(statistics.CableFormatted);
        }

        [Fact]
        public void Apply_NoReserve_LeavesMeasuredValueUntouched()
        {
            var statistics = new CableStatResult { CableSum = 8.5d };
            statistics.CableFormatted.Add("8.5");

            // 显式 owner=0 表示三类预留都不加，总长保持实测值。
            CableReserveInfo reserve = CableReservePolicy.Apply(statistics, "普通设备", 0d, 0d);

            Assert.Equal(8.5d, reserve.TotalMeters, 3);
            Assert.Equal(8.5d, statistics.CableSum, 3);
            Assert.Single(statistics.CableFormatted);
        }

        [Theory]
        [InlineData("I-Line盘", "盘内", 3400d)]
        [InlineData("插座盘", "盘内", 1000d)]
        [InlineData("母线插接箱", "箱内", 500d)]
        [InlineData("插接口", "箱内", 500d)]
        [InlineData("普通设备", "", 0d)]
        public void PanelReserve_ReturnsAmountAndLabelByUpstreamType(string next,
            string expectedLabel, double expectedMm)
        {
            Assert.Equal(expectedMm, CableReservePolicy.PanelReserveMillimeters(next));
            Assert.Equal(expectedLabel, CableReservePolicy.PanelReserveLabel(next));
        }

        [Fact]
        public void Apply_AddsOwnerReserveThreeThousandByDefault()
        {
            var statistics = new CableStatResult { CableSum = 2d };
            CableReserveInfo reserve = CableReservePolicy.Apply(statistics, "普通设备", 0d);

            Assert.Equal(3000d, reserve.OwnerReserveMillimeters);
            Assert.Equal(5d, reserve.TotalMeters, 3);
            Assert.Equal(5d, statistics.CableSum, 3);
        }

        [Fact]
        public void Apply_NegativeOrInvalidDeviceReserve_ClampsToZero()
        {
            var statistics = new CableStatResult { CableSum = 1d };
            CableReserveInfo negative = CableReservePolicy.Apply(statistics, "I-Line盘", -500d);
            Assert.Equal(0d, negative.DeviceReserveMillimeters);
            Assert.Equal(7.4d, negative.TotalMeters, 3);

            var second = new CableStatResult { CableSum = 1d };
            CableReserveInfo nan = CableReservePolicy.Apply(second, "I-Line盘", double.NaN);
            Assert.Equal(0d, nan.DeviceReserveMillimeters);
        }
    }
}
