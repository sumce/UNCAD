using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UNCAD.Core.Text
{
    /// <summary>Normalizes U1Q labels to the bridge-specification plus millimetre format.</summary>
    public static class BridgeLabelFormatter
    {
        private static readonly Regex GridLabel = new Regex(
            @"^桥架\s*([0-9]+(?:\.[0-9]+)?)\s*[\*xX×]\s*([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*格$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex MillimetreLabel = new Regex(
            @"^桥架\s*([0-9]+(?:\.[0-9]+)?)\s*[\*xX×]\s*([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*mm$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string NormalizeOrDefault(string value, double mmPerGrid,
            string fallback = "桥架200*100 2500mm")
        {
            if (TryNormalize(value, mmPerGrid, out string normalized)) return normalized;
            if (TryNormalize(fallback, mmPerGrid, out normalized)) return normalized;
            return "桥架200*100 2500mm";
        }

        public static bool TryNormalize(string value, double mmPerGrid,
            out string normalized)
        {
            normalized = "";
            if (TryMigrateLegacyGrid(value, mmPerGrid, out normalized)) return true;

            string text = (value ?? "").Trim();
            Match millimetre = MillimetreLabel.Match(text);
            if (!millimetre.Success || !TryNumber(millimetre, 1, out double width)
                || !TryNumber(millimetre, 2, out double height)
                || !TryNumber(millimetre, 3, out double totalMillimetres)
                || width <= 0 || height <= 0 || totalMillimetres <= 0) return false;
            normalized = "桥架" + Format(width) + "*" + Format(height)
                + " " + Format(totalMillimetres) + "mm";
            return true;
        }

        /// <summary>
        /// Converts only the legacy grid-count form. Current millimetre labels
        /// deliberately return false so U1F/U1U can migrate old CAD text without
        /// rewriting already-current annotations.
        /// </summary>
        public static bool TryMigrateLegacyGrid(string value, double mmPerGrid,
            out string normalized)
        {
            normalized = "";
            Match grid = GridLabel.Match((value ?? "").Trim());
            if (!grid.Success || !TryNumber(grid, 1, out double width)
                || !TryNumber(grid, 2, out double height)
                || !TryNumber(grid, 3, out double grids)
                || width <= 0 || height <= 0 || grids <= 0
                || !IsValidScale(mmPerGrid)) return false;
            normalized = "桥架" + Format(width) + "*" + Format(height)
                + " " + Format(grids * mmPerGrid) + "mm";
            return true;
        }

        private static bool IsValidScale(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

        private static bool TryNumber(Match match, int group, out double value)
            => double.TryParse(match.Groups[group].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static string Format(double value)
            => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
