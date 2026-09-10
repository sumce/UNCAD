using System;
using System.Globalization;
using UNCAD.Core.QuickLine;

namespace UNCAD.Core.Text
{
    /// <summary>Builds one editable text line from an aligned dimension's displayed value.</summary>
    public static class DimensionTextFormatter
    {
        /// <summary>AutoCAD's conventional override for suppressing dimension text.</summary>
        public const string SuppressedDimensionText = " ";

        public static bool TryBuildSingleLine(string dimensionText, string dimPost,
            double measurement, out string text)
        {
            text = string.Empty;
            if (double.IsNaN(measurement) || double.IsInfinity(measurement)
                || measurement < 0.0 || IsSuppressed(dimensionText))
                return false;

            string number = measurement.ToString("0.##", CultureInfo.InvariantCulture);
            bool hasOverride = !string.IsNullOrWhiteSpace(dimensionText);
            string raw = hasOverride ? dimensionText : dimPost;
            if (string.IsNullOrWhiteSpace(raw))
                raw = number;
            else if (raw.IndexOf("<>", StringComparison.Ordinal) >= 0)
                raw = raw.Replace("<>", number);
            else if (!hasOverride)
                raw = number + raw;

            string cleaned = QuickLineMillimeterText.NormalizeCadText(raw);
            text = string.Join(" ", cleaned.Split((char[])null,
                StringSplitOptions.RemoveEmptyEntries));
            if (QuickLineMillimeterText.TryParseCad(text, out double millimetres)
                || QuickLineMillimeterText.TryParseNumber(text, out millimetres))
                text = QuickLineMillimeterText.Format(millimetres);
            return text.Length > 0;
        }

        public static bool IsSuppressed(string dimensionText)
            => dimensionText != null && dimensionText.Length > 0
                && dimensionText.Trim().Length == 0;
    }
}
