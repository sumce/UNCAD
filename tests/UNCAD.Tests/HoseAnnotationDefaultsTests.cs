using UNCAD.Core.Fill;
using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1B 标注默认文本（HoseAnnotationDefaults，3.3.3）与既有判据的兼容契约：
    /// - 格式沿用软管链路（RuanguanLabelFormatter 写 / RuanguanLengthParser 读，
    ///   即 U1C/U1F/U1U 写进 Ruanguan 块的同一写法）；
    /// - 不发明新格式、不进电缆/桥架/线管求和、不命中 D-043 预留标吸收。
    /// </summary>
    public sealed class HoseAnnotationDefaultsTests
    {
        [Fact]
        public void DefaultText_FollowsTheRuanguanHoseFormat()
        {
            // 100 管径 + 2501.4 中心线长 → "100mm软管:2500mm"（取整到 10）。
            Assert.Equal("100mm软管:2500mm",
                HoseAnnotationDefaults.BuildDefaultText(100.0, 2501.4));
            Assert.Equal("100mm软管:2500mm",
                HoseAnnotationDefaults.BuildDefaultText(100.0, 2495.0));
            Assert.Equal("20mm软管:2000mm",
                HoseAnnotationDefaults.BuildDefaultText(20.0, 2000.0));
        }

        [Fact]
        public void DefaultText_IsParseableByTheExistingHoseReader()
        {
            // 现有软管判据（RuanguanLengthParser，Ruanguan 块属性链路）必须能
            // 读回默认文本里的长度——沿用同一格式，不发明新格式。
            string text = HoseAnnotationDefaults.BuildDefaultText(100.0, 2501.4);
            Assert.True(RuanguanLengthParser.TryParseMillimetres(text,
                out double millimetres));
            Assert.Equal(2500, millimetres);
        }

        [Fact]
        public void DefaultText_NeverLeaksIntoCableOrConduitOrBridgeSums()
        {
            // 求和引擎的三类文字判据全部整行锚定——软管文本一个都不命中，
            // 绝不污染 U1U/U1F 统计（电缆 `^\d+0mm$` / 线管 `⌀20线管 2000mm` /
            // 桥架 `桥架200*100 2500mm`）。
            foreach (double length in new[] { 1234.0, 2500.0, 10000.0 })
            {
                string text = HoseAnnotationDefaults.BuildDefaultText(100.0, length);
                Assert.Null(TextParser.ExtractCableLength(text));
                Assert.Null(TextParser.ExtractConduitLength(text));
                Assert.Null(TextParser.ExtractConduitSpec(text));
                Assert.Null(TextParser.ExtractBridgeSpec(text));
                Assert.False(TextParser.TryExtractBridgeLabel(text,
                    out _, out _));
            }
        }

        [Fact]
        public void DefaultText_IsNotADeviceReserveMarker()
        {
            // D-043 设备预留标判据（`^\d{1,6}0mm$` 整行）不得命中——否则 U1B 文本
            // 会被 U1F/U1U 吸收删除。
            string text = HoseAnnotationDefaults.BuildDefaultText(100.0, 2500.0);
            Assert.False(DeviceReserveMarker.TryParseMillimeters(text, out _));
        }

        [Fact]
        public void InvalidInputs_FallBackInsteadOfThrowing()
        {
            // 非法直径回退 100、非法/非正长度回退 10，绝不抛异常。
            Assert.Equal("100mm软管:2500mm",
                HoseAnnotationDefaults.BuildDefaultText(double.NaN, 2500.0));
            Assert.Equal("100mm软管:10mm",
                HoseAnnotationDefaults.BuildDefaultText(100.0, double.PositiveInfinity));
            Assert.Equal("100mm软管:10mm",
                HoseAnnotationDefaults.BuildDefaultText(100.0, 0.0));
            // 超域直径（>1000）规范化失败 → 回退 100。
            Assert.Equal("100mm软管:100mm",
                HoseAnnotationDefaults.BuildDefaultText(5000.0, 100.0));
            // 极短长度 → 不小于 10。
            Assert.Equal("100mm软管:10mm",
                HoseAnnotationDefaults.BuildDefaultText(100.0, 2.0));
        }
    }
}
