using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UNCAD.Core.Text
{
    /// <summary>Parses a Ruanguan dynamic-block hose length into formatted meters.</summary>
    public static class RuanguanLengthParser
    {
        private static readonly Regex ExplicitLength = new Regex(
            @"软管(?:长度)?\s*[:：=]?\s*([0-9]+(?:[.,][0-9]+)?)\s*(mm|m)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BareLength = new Regex(
            @"^\s*([0-9]+(?:[.,][0-9]+)?)\s*(mm|m)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeadingDiameter = new Regex(
            @"^\s*([0-9]+(?:[.,][0-9]+)?)\s*mm\s*软管",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// D-058：从软管标注文字（「100mm软管:2500mm」，RuanguanLabelFormatter
        /// 的写法）取软管口径（mm）。不是这个写法时返回 false，不猜。
        /// </summary>
        public static bool TryParseDiameterMillimetres(string value,
            out double millimetres)
        {
            millimetres = 0;
            Match match = LeadingDiameter.Match(value ?? "");
            if (!match.Success
                || !double.TryParse(NormalizeNumeric(match.Groups[1].Value),
                    NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double parsed)
                || double.IsNaN(parsed) || double.IsInfinity(parsed)
                || parsed <= 0)
                return false;
            millimetres = parsed;
            return true;
        }

        public static string ParseMeters(string value)
        {
            return TryParseMillimetres(value, out double millimetres)
                ? TextFormatter.FormatNum(millimetres / 1000d) : "";
        }

        public static bool TryParseMillimetres(string value, out double millimetres)
        {
            millimetres = 0;
            string text = (value ?? "").Trim();
            Match match = ExplicitLength.Match(text);
            if (!match.Success) match = BareLength.Match(text);
            if (!match.Success) return false;

            string numeric = NormalizeNumeric(match.Groups[1].Value);
            if (!double.TryParse(numeric, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double parsed)
                || double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed <= 0)
                return false;
            millimetres = string.Equals(match.Groups[2].Value, "m",
                StringComparison.OrdinalIgnoreCase) ? parsed * 1000d : parsed;
            return !double.IsNaN(millimetres) && !double.IsInfinity(millimetres)
                && millimetres > 0;
        }

        private static string NormalizeNumeric(string value)
        {
            string text = value ?? "";
            if (text.IndexOf(',') >= 0 && text.IndexOf('.') < 0)
            {
                int separator = text.IndexOf(',');
                if (text.Length - separator - 1 == 3)
                    return text.Replace(",", "");
                return text.Replace(',', '.');
            }
            return text.Replace(",", "");
        }
    }
}
