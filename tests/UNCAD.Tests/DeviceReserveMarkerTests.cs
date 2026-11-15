using System;
using System.Collections.Generic;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 老版设备内预留横排毫米标的识别与吸收决策（D-043，2026-11-03）。
    /// 识别：纯数字或数字+mm/毫米，0–100000；水平判定 ~1° 容差；
    /// 吸收：恰好一处→取图纸值（覆盖 JSON 旧值）；多处/关闭→不取。
    /// </summary>
    public sealed class DeviceReserveMarkerTests
    {
        [Theory]
        [InlineData("2000mm", 2000)]
        [InlineData("2000MM", 2000)]
        [InlineData(" 3000mm ", 3000)]
        [InlineData("100000mm", 100000)]
        public void TryParse_AcceptsPlainHorizontalMillimeterText(
            string text, double expected)
        {
            Assert.True(DeviceReserveMarker.TryParseMillimeters(text, out double value));
            Assert.Equal(expected, value, 2);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("电缆1000+700")]
        [InlineData("（预留）")]
        [InlineData("2000（设备内）")]
        [InlineData("AP1")]
        [InlineData("236 个回路")]
        [InlineData("abc")]
        [InlineData("-500")]
        [InlineData("120000")]
        [InlineData("1000.123")]
        // 3.2.0.4 判据收紧：必须整数以 0 结尾 + mm 后缀 + 单行。
        [InlineData("2000")]
        [InlineData("950 毫米")]
        [InlineData("555mm")]
        [InlineData("1500.5mm")]
        [InlineData("0")]
        [InlineData("0mm")]
        [InlineData("3000mm\n2000mm")]
        public void TryParse_RejectsNonMarkerText(string text)
        {
            Assert.False(DeviceReserveMarker.TryParseMillimeters(text, out _));
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(0.01, true)]
        [InlineData(6.283185, true)]
        [InlineData(-0.0174, true)]
        [InlineData(0.02, false)]
        [InlineData(Math.PI / 2, false)]
        [InlineData(3.14159, false)]
        public void IsHorizontal_FiltersOutAngledLengthLabels(
            double rotation, bool expected)
        {
            Assert.Equal(expected, DeviceReserveMarker.IsHorizontal(rotation));
        }

        [Fact]
        public void Decide_AbsorbsExactlyOneMarkerAndOverridesJson()
        {
            var candidates = new List<DeviceReserveMarkerHit>
            {
                new DeviceReserveMarkerHit("1A2", "2000", 2000)
            };

            // JSON 旧值 950：图纸标注是现场最新意图，覆盖之。
            double? decided = DeviceReserveMarker.Decide(true, candidates, 950,
                out List<int> absorbed);

            Assert.Equal(new[] { 0 }, absorbed);   // 吸收下标 0（唯一候选）
            Assert.Equal(2000, decided.Value);
        }

        [Fact]
        public void Decide_IgnoresMultipleCandidatesAndKeepsExisting()
        {
            var candidates = new List<DeviceReserveMarkerHit>
            {
                new DeviceReserveMarkerHit("1A2", "2000", 2000),
                new DeviceReserveMarkerHit("1A3", "3000", 3000)
            };

            double? decided = DeviceReserveMarker.Decide(true, candidates, 950,
                out List<int> absorbed);

            Assert.Empty(absorbed);
            Assert.Equal(950, decided.Value);
        }

        [Fact]
        public void Decide_DisabledOrNoMarkerFallsBackToExisting()
        {
            var candidates = new List<DeviceReserveMarkerHit>
            {
                new DeviceReserveMarkerHit("1A2", "2000", 2000)
            };

            Assert.Null(DeviceReserveMarker.Decide(false, candidates, null, out _));
            Assert.Null(DeviceReserveMarker.Decide(true,
                new List<DeviceReserveMarkerHit>(), null, out List<int> none));
            Assert.Empty(none);
        }
    }
}
