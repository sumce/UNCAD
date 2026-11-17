using System;
using System.Globalization;

namespace UNCAD.Core.Text
{
    /// <summary>
    /// U1B 软管标注默认文字（3.3.3，D-054 Amendment）。格式**沿用**软管链路既有
    /// 写法（RuanguanLabelFormatter：`100mm软管:2500mm`）——U1C/U1F/U1U 写进
    /// Ruanguan 块属性的就是这个格式，RuanguanLengthParser 读的也是它；
    /// U1B 不发明新格式。
    ///
    /// 与求和统计的关系（现实，见测试 HoseAnnotationDefaultsTests）：
    /// - 求和引擎（TextParser）没有软管文字类别，软管米数只从 Ruanguan 块属性
    ///   链路读取——U1B 的独立 DBText 不参与软管求和；
    /// - 该格式整行不匹配电缆（`^\d+0mm$`）、桥架、线管判据，也匹配不上 D-043
    ///   设备预留标（`^\d{1,6}0mm$` 整行）——绝不泄漏进电缆和/被误吸收；
    /// - 长度取整到 10（三类长度统计共同的尾 0 约定），用户可在提示里改。
    /// </summary>
    public static class HoseAnnotationDefaults
    {
        /// <summary>直径规范化失败（超域/非法）时的回退管径。</summary>
        public const string FallbackDiameter = "100";

        /// <summary>
        /// 默认标注文字：直径 + 中心线长度（取整到 10，向上合成不小于 10）。
        /// </summary>
        public static string BuildDefaultText(double diameter, double centerlineLength)
        {
            if (double.IsNaN(diameter) || double.IsInfinity(diameter)) diameter = 100.0;
            string normalized = ConduitDiameter.NormalizeOrEmpty(
                diameter.ToString("0.##", CultureInfo.InvariantCulture));
            if (normalized.Length == 0) normalized = FallbackDiameter;
            if (double.IsNaN(centerlineLength) || double.IsInfinity(centerlineLength)
                || centerlineLength <= 0)
                centerlineLength = 10.0;
            double rounded = Math.Round(centerlineLength / 10.0,
                MidpointRounding.AwayFromZero) * 10.0;
            if (rounded < 10.0) rounded = 10.0;
            return RuanguanLabelFormatter.Build(normalized, rounded);
        }
    }
}
