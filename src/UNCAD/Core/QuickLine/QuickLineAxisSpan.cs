using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// 3.3.0（D-054）：U1U 预览用的单个轴位解析结果。容错格式：
    /// 「48/V」「48-V」「48 V」「48」「V」；解析失败不猜（TryParse=false，
    /// 展示层显示原文）。与 QuickLineAxisGridPlanner 的台账轴位格式互通
    /// （那里只收「数字/字母」完整形式，这里额外容忍分隔符差异与半边值）。
    /// </summary>
    public sealed class QuickLineAxisReference
    {
        internal QuickLineAxisReference(int? number, string letter, long letterIndex)
        {
            Number = number;
            Letter = letter ?? string.Empty;
            LetterIndex = letterIndex;
        }

        /// <summary>数字轴（如 48）；纯字母输入时为 null。</summary>
        public int? Number { get; }
        /// <summary>字母轴大写形式（如 V）；纯数字输入时为空串。</summary>
        public string Letter { get; }
        internal long LetterIndex { get; }

        public bool HasNumber => Number.HasValue;
        public bool HasLetter => Letter.Length > 0;

        public override string ToString()
        {
            if (HasNumber && HasLetter) return Number.Value.ToString(
                CultureInfo.InvariantCulture) + "/" + Letter;
            if (HasNumber) return Number.Value.ToString(CultureInfo.InvariantCulture);
            return Letter;
        }
    }

    /// <summary>
    /// 3.3.0（D-054）：上下游轴位跨度解析与展示文案。一个柱位间距按
    /// QuickLineAxisGridPlanner 的 4800 mm 口径；展示形如
    /// 「上游 48/V ↔ 下游 37/V · Δ11 跨 × 4.8m ≈ 52.8m」。任一侧解析失败时
    /// 显示原文、不算跨度（不猜测）。
    /// </summary>
    public static class QuickLineAxisSpan
    {
        // 容错口径：数字+可选分隔符(空格 / -)+字母（48/V、48-V、48 V、48）、
        // 纯数字（48）、纯字母（V、ABC）；其余（48V 无分隔、轴A、48/V/x）不猜。
        private static readonly Regex Pattern = new Regex(
            @"^\s*(?:(?<number>[0-9]{1,6})(?:[\s/\-]+(?<letter>[A-Za-z]{1,4})?)?|(?<letter>[A-Za-z]{1,4}))\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool TryParse(string value, out QuickLineAxisReference axis)
        {
            axis = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            Match match = Pattern.Match(value.Trim());
            if (!match.Success) return false;
            string numberText = match.Groups["number"].Value;
            string letterText = match.Groups["letter"].Value;
            int? number = null;
            if (numberText.Length > 0)
            {
                if (!int.TryParse(numberText, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int parsed)) return false;
                number = parsed;
            }
            string letter = letterText.ToUpperInvariant();
            long letterIndex = 0;
            foreach (char item in letter)
            {
                long digit = item - 'A' + 1;
                if (letterIndex > (long.MaxValue - digit) / 26) return false;
                letterIndex = letterIndex * 26 + digit;
            }
            if (number == null && letter.Length == 0) return false;
            axis = new QuickLineAxisReference(number, letter, letterIndex);
            return true;
        }

        /// <summary>
        /// 生成预览摘要。两端都解析成功且能算出跨度（数字轴优先，两端都有
        /// 数字才用数字轴；否则两端都有字母才用字母轴）时输出「· Δn 跨 ×
        /// x.xm ≈ y.ym」；解析失败/无跨度只显示「上游 … ↔ 下游 …」原文。
        /// </summary>
        public static string Describe(string upstreamAxis, string downstreamAxis,
            double spacingMillimetres = QuickLineAxisGridPlanner
                .DefaultSpacingMillimetres)
        {
            if (double.IsNaN(spacingMillimetres)
                || double.IsInfinity(spacingMillimetres) || spacingMillimetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(spacingMillimetres));
            string upstream = (upstreamAxis ?? string.Empty).Trim();
            string downstream = (downstreamAxis ?? string.Empty).Trim();
            string core = "上游 " + (upstream.Length == 0 ? "（空）" : upstream)
                + " ↔ 下游 " + (downstream.Length == 0 ? "（空）" : downstream);
            long? bays = ComputeBays(upstream, downstream);
            if (!bays.HasValue) return core;
            double meters = bays.Value * spacingMillimetres / 1000.0;
            return core + " · Δ" + bays.Value.ToString(CultureInfo.InvariantCulture)
                + " 跨 × " + (spacingMillimetres / 1000.0)
                    .ToString("0.#", CultureInfo.InvariantCulture) + "m"
                + " ≈ " + meters.ToString("0.#", CultureInfo.InvariantCulture) + "m";
        }

        /// <summary>两端同侧轴（数字优先）的跨度（格数）；无法可靠计算返回 null。</summary>
        public static long? ComputeBays(string upstreamAxis, string downstreamAxis)
        {
            if (!TryParse(upstreamAxis, out QuickLineAxisReference upstream)
                || !TryParse(downstreamAxis, out QuickLineAxisReference downstream))
                return null;
            if (upstream.HasNumber && downstream.HasNumber)
                return Math.Abs((long)upstream.Number.Value
                    - downstream.Number.Value);
            if (upstream.HasLetter && downstream.HasLetter)
                return Math.Abs(upstream.LetterIndex - downstream.LetterIndex);
            return null;
        }
    }
}
