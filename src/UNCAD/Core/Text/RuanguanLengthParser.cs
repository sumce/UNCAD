using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UNCAD.Core.Text
{
    /// <summary>Parses a Ruanguan dynamic-block hose length into formatted meters.</summary>
    public static class RuanguanLengthParser
    {
        private static readonly Regex ExplicitLength = new Regex(
            @"软管(?:长度)?\s*[:：=]?\s*([0-9]+(?:[.,][0-9]+)?)\s*mm\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BareLength = new Regex(
            @"^\s*([0-9]+(?:[.,][0-9]+)?)\s*mm\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ParseMeters(string value)
        {
            string text = (value ?? "").Trim();
            Match match = ExplicitLength.Match(text);
            if (!match.Success) match = BareLength.Match(text);
            if (!match.Success) return "";

            string numeric = NormalizeNumeric(match.Groups[1].Value);
            if (!double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double millimeters) || double.IsNaN(millimeters)
                || double.IsInfinity(millimeters) || millimeters <= 0) return "";
            return TextFormatter.FormatNum(millimeters / 1000d);
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
