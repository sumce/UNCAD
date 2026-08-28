using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UNCAD.Core.Text
{
    public static class ConduitDiameter
    {
        public const double MaximumMillimeters = 1000.0;
        private static readonly Regex Pattern = new Regex(
            @"^\s*(?:DN|[⌀ØΦ])?\s*([0-9]+(?:\.[0-9]+)?)\s*(?:MM|线管|软管)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool TryNormalize(string value, out string diameter)
        {
            Match match = Pattern.Match(value ?? "");
            if (!match.Success || !double.TryParse(match.Groups[1].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out double millimeters)
                || millimeters <= 0 || millimeters > MaximumMillimeters
                || double.IsNaN(millimeters) || double.IsInfinity(millimeters))
            {
                diameter = "";
                return false;
            }
            diameter = TextFormatter.FormatNum(millimeters);
            return true;
        }

        public static string NormalizeOrEmpty(string value)
            => TryNormalize(value, out string diameter) ? diameter : "";

        public static string NormalizeOrDefault(string value, string fallback)
            => TryNormalize(value, out string diameter) ? diameter : fallback;
    }
}
