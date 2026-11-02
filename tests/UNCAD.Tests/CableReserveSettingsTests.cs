using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 2.10.0 可配置盘内/箱内预留：默认 3400/1000/500，U1SET 可改；
    /// 非法值回退默认，避免一个错误配置把电缆总长算飞。
    /// </summary>
    public sealed class CableReserveSettingsTests
    {
        [Fact]
        public void Default_UsesFieldValues()
        {
            Assert.Equal(3400d, CableReserveSettings.Default.ILinePanelMillimeters);
            Assert.Equal(1000d, CableReserveSettings.Default.SocketPanelMillimeters);
            Assert.Equal(500d, CableReserveSettings.Default.BusbarConnectorMillimeters);
        }

        [Fact]
        public void PanelReserve_UsesConfiguredValues()
        {
            var settings = new CableReserveSettings(5000d, 1200d, 700d);

            Assert.Equal(5000d, CableReservePolicy.PanelReserveMillimeters("I-Line盘", settings));
            Assert.Equal(1200d, CableReservePolicy.PanelReserveMillimeters("插座盘", settings));
            Assert.Equal(700d, CableReservePolicy.PanelReserveMillimeters("母线插接箱", settings));
            Assert.Equal(0d, CableReservePolicy.PanelReserveMillimeters("普通设备", settings));
        }

        [Fact]
        public void Apply_UsesConfiguredPanelReserveInTotal()
        {
            var settings = new CableReserveSettings(3400d, 1000d, 500d);
            var statistics = new CableStatResult { CableSum = 10d };

            CableReserveInfo reserve = CableReservePolicy.Apply(statistics, "插座盘", 0d, 0d,
                settings);

            Assert.Equal(1000d, reserve.PanelReserveMillimeters);
            Assert.Equal(11d, reserve.TotalMeters, 3);
        }

        [Theory]
        [InlineData(-1d)]
        [InlineData(double.NaN)]
        [InlineData(1000000d)]
        public void Constructor_InvalidValuesFallBackToDefaults(double invalid)
        {
            var settings = new CableReserveSettings(invalid, invalid, invalid);

            Assert.Equal(3400d, settings.ILinePanelMillimeters);
            Assert.Equal(1000d, settings.SocketPanelMillimeters);
            Assert.Equal(500d, settings.BusbarConnectorMillimeters);
        }

        [Fact]
        public void FillRuntimeOptions_ExposesConfiguredReserveSettings()
        {
            var settings = new CableReserveSettings(4000d, 1100d, 600d);
            FillRuntimeOptions options = FillRuntimeOptions.Create("m.xlsx", 1, 11, 500d, 250d,
                FillPlanningOptions.Default, null, 20d, 1000d, true, true, 3000d, settings);

            Assert.Same(settings, options.CableReserve);
        }
    }
}