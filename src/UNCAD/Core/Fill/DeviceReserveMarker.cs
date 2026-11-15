using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UNCAD.Core.Fill
{
    /// <summary>一处候选标记：来自哪条文字（实体句柄文本）、识别出的毫米值。</summary>
    public sealed class DeviceReserveMarkerHit
    {
        public DeviceReserveMarkerHit(string handle, string text, double millimeters)
        {
            Handle = handle ?? "";
            Text = text ?? "";
            Millimeters = millimeters;
        }

        public string Handle { get; }
        public string Text { get; }
        public double Millimeters { get; }
    }

    /// <summary>
    /// 老版设备内预留标记（现场在设备旁横排手写的毫米数文字）的识别与吸收决策
    /// （D-043，2026-11-03）。纯逻辑、可单测；实体读取在 Features 层。
    ///
    /// 识别口径：内容是一个毫米数（纯数字或数字+mm/毫米，0–100000），且文字水平
    /// （旋转接近 0°——斜排的是长度标注，不是预留标）。吸收口径（保守优先）：
    /// 本次范围内**恰好一个**候选 → 自动吸收并删除该标注；**多个**候选 → 一律不吸收
    /// （无法分辨预留与轴号/序号，宁可不取也不取错），交由人工确认。
    /// </summary>
    public static class DeviceReserveMarker
    {
        public const double MinimumMillimeters = 0d;
        public const double MaximumMillimeters = 100000d;
        /// <summary>水平判定容差（弧度）：约 1° 以内视为横排。</summary>
        public const double HorizontalToleranceRadians = 0.0175;

        // 3.2.0.4（用户规格）：候选=水平·单行·末尾"0mm"——整数以 0 结尾
        // （10 的倍数）且必须带 mm 后缀（"3000mm" ✓；"3000"、"550mm"、
        // "3000.5mm"、"0mm"、多行文字 ✗）。末尾 0 收窄误吸收轴号/序号；
        // 单行排除多行清单文字；无后缀的裸数字留给电缆长度统计那套判据。
        private static readonly Regex MillimetersPattern = new Regex(
            @"^\s*(?<value>\d{1,6}0)\s*(?:mm|MM)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

        /// <summary>文本是否是一个设备预留毫米标；解析出毫米数。</summary>
        public static bool TryParseMillimeters(string text, out double millimeters)
        {
            millimeters = 0d;
            string candidate = text ?? "";
            if (candidate.IndexOf('\n') >= 0 || candidate.IndexOf('\r') >= 0)
                return false; // 单行判据：多行文字不是预留标。
            Match match = MillimetersPattern.Match(candidate);
            if (!match.Success) return false;
            if (!double.TryParse(match.Groups["value"].Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return false;
            if (value < MinimumMillimeters || value > MaximumMillimeters) return false;
            millimeters = value;
            return true;
        }

        /// <summary>文字旋转角（弧度）是否水平（约 1° 容差，含接近 360° 的绕回）。</summary>
        public static bool IsHorizontal(double rotationRadians)
        {
            double normalized = Math.Abs(rotationRadians) % (2d * Math.PI);
            if (normalized > Math.PI) normalized = 2d * Math.PI - normalized;
            return normalized <= HorizontalToleranceRadians;
        }

        /// <summary>
        /// 吸收决策：返回本次应采用的设备内预留（毫米），及应删除的候选下标集合。
        /// 恰好一个候选 → 采用它（图纸标注是现场最新意图，覆盖 JSON 旧值）；
        /// 无候选 → 沿用 existing（可能为 null = 交给现有询问/CABLE_INFO 恢复流程）；
        /// 多个候选 → 一律不吸收（返回 existing 且 absorbed 为空），由调用方提示人工确认。
        /// </summary>
        public static double? Decide(bool enabled,
            System.Collections.Generic.List<DeviceReserveMarkerHit> candidates,
            double? existingJsonMillimeters,
            out System.Collections.Generic.List<int> absorbedIndexes)
        {
            absorbedIndexes = new System.Collections.Generic.List<int>();
            if (!enabled || candidates == null || candidates.Count != 1)
                return existingJsonMillimeters;
            absorbedIndexes.Add(0);
            return candidates[0].Millimeters;
        }
    }
}
