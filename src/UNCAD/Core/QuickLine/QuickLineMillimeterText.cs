using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// Parser/formatter for the standalone millimeter labels created by U1L.
    /// Unlike the reporting parser, U1LX accepts arbitrary positive decimal
    /// distances because the user may enter a measured value such as 1234.5.
    /// </summary>
    public static class QuickLineMillimeterText
    {
        private static readonly Regex MillimeterRegex = new Regex(
            @"^\s*([0-9]+(?:\.[0-9]+)?|\.[0-9]+)\s*mm\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MillimeterTokenRegex = new Regex(
            @"(?<![0-9.])([0-9]+(?:\.[0-9]+)?|\.[0-9]+)\s*mm(?![A-Za-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BareNumberRegex = new Regex(
            @"^\s*([0-9]+(?:\.[0-9]+)?|\.[0-9]+)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex NumericRegex = new Regex(
            @"^\s*([0-9]+(?:[.,][0-9]+)?|[.,][0-9]+)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // AutoCAD MTEXT/DIMENSION strings can contain font, paragraph,
        // stacked-fraction and Unicode escape codes.  They are presentation
        // syntax, not part of the measured value.
        private static readonly Regex UnicodeEscapeRegex = new Regex(
            @"\\U\+([0-9A-Fa-f]{4,6})",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex FormattingCodeRegex = new Regex(
            @"\\(?:f[^;]*;|[A-Za-z][^;]*;|[A-Za-z])",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool TryParse(string text, out double millimeters)
        {
            millimeters = 0;
            Match match = MillimeterRegex.Match(text ?? "");
            if (!match.Success) return false;

            double value;
            if (!double.TryParse(match.Groups[1].Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value)) return false;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                return false;

            millimeters = value;
            return true;
        }

        /// <summary>
        /// Parses a value read from an AutoCAD text-bearing entity.  Unlike
        /// <see cref="TryParse"/>, this removes harmless MTEXT formatting
        /// codes before applying the same strict standalone-mm rule.
        /// </summary>
        public static bool TryParseCad(string text, out double millimeters)
            => TryParse(NormalizeCadText(text), out millimeters);

        public static double? Parse(string text)
        {
            double value;
            return TryParse(text, out value) ? (double?)value : null;
        }

        /// <summary>
        /// Extracts one millimetre token from an associated descriptive label.
        /// Ambiguous labels containing more than one token are rejected.
        /// </summary>
        public static bool TryExtractSingle(string text, out double millimeters)
        {
            millimeters = 0.0;
            MatchCollection matches = MillimeterTokenRegex.Matches(text ?? string.Empty);
            if (matches.Count != 1) return false;
            return TryParseToken(matches[0], out millimeters);
        }

        /// <summary>
        /// Parses a numeric-only annotation (for example "1250.5").  This is
        /// intentionally separate from <see cref="TryParse"/>: bare numbers
        /// are safe only when the CAD object already carries an explicit U1L
        /// association or is a native Dimension whose geometry identifies the
        /// measured segment.  Treating every numeric DBText as a distance
        /// would incorrectly consume machine IDs and other drawing notes.
        /// </summary>
        public static bool TryParseBareNumber(string text, out double millimeters)
        {
            millimeters = 0.0;
            Match match = BareNumberRegex.Match(text ?? string.Empty);
            if (!match.Success) return false;
            return TryParseToken(match, out millimeters);
        }

        /// <summary>Extracts one mm token after removing AutoCAD formatting.</summary>
        public static bool TryExtractSingleCad(string text, out double millimeters)
            => TryExtractSingle(NormalizeCadText(text), out millimeters);

        /// <summary>
        /// Parses a numeric-only value.  This is intentionally separate from
        /// <see cref="TryParse"/> so ordinary numeric drawing text is never
        /// silently treated as a distance unless the caller has stronger
        /// context (for example a native DIMENSION entity).
        /// </summary>
        public static bool TryParseNumber(string text, out double millimeters)
        {
            millimeters = 0.0;
            string normalized = NormalizeCadText(text);
            Match match = NumericRegex.Match(normalized);
            if (!match.Success) return false;

            string token = match.Groups[1].Value;
            if (token.IndexOf(',') >= 0 && token.IndexOf('.') < 0)
                token = token.Replace(',', '.');
            if (!double.TryParse(token, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out double value)) return false;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
                return false;
            millimeters = value;
            return true;
        }

        /// <summary>
        /// Resolves the value displayed by a linear AutoCAD dimension. An
        /// explicit text override is the user's intended distance and takes
        /// precedence over the geometric measurement. Empty overrides and
        /// overrides containing the standard &lt;&gt; measurement token retain
        /// the measured value.
        /// </summary>
        public static bool TryResolveDimensionValue(string dimensionText,
            double measurement, out double millimeters)
        {
            millimeters = 0.0;
            string normalized = NormalizeCadText(dimensionText).Trim();
            bool usesMeasurement = normalized.Length == 0
                || normalized.IndexOf("<>", StringComparison.Ordinal) >= 0;
            if (!usesMeasurement)
            {
                if (TryParse(normalized, out millimeters)
                    || TryExtractSingle(normalized, out millimeters)
                    || TryParseNumber(normalized, out millimeters))
                    return true;
            }

            if (double.IsNaN(measurement) || double.IsInfinity(measurement)
                || measurement < 0.0)
                return false;
            millimeters = measurement;
            return true;
        }

        /// <summary>Replaces the unique mm token while preserving surrounding text.</summary>
        public static bool TryReplaceSingle(string text, double millimeters,
            out string replacement)
        {
            replacement = text ?? string.Empty;
            if (double.IsNaN(millimeters) || double.IsInfinity(millimeters)
                || millimeters < 0.0) return false;

            MatchCollection matches = MillimeterTokenRegex.Matches(replacement);
            if (matches.Count != 1) return false;
            Match match = matches[0];
            replacement = replacement.Substring(0, match.Index)
                + Format(millimeters)
                + replacement.Substring(match.Index + match.Length);
            return true;
        }

        /// <summary>
        /// Replaces one mm token in a CAD string.  If formatting codes prevent
        /// a direct replacement, a normalized equivalent is returned; the
        /// measured value is preserved and the caller may choose whether to
        /// keep or discard presentation-only formatting.
        /// </summary>
        public static bool TryReplaceSingleCad(string text, double millimeters,
            out string replacement)
        {
            if (TryReplaceSingle(text, millimeters, out replacement)) return true;
            string normalized = NormalizeCadText(text);
            return TryReplaceSingle(normalized, millimeters, out replacement);
        }

        public static string Format(double millimeters)
        {
            if (double.IsNaN(millimeters) || double.IsInfinity(millimeters)
                || millimeters < 0)
                throw new ArgumentOutOfRangeException("millimeters");
            return millimeters.ToString("0.##", CultureInfo.InvariantCulture) + "mm";
        }

        /// <summary>Removes AutoCAD presentation escapes and normalizes Unicode digits.</summary>
        public static string NormalizeCadText(string text)
        {
            string value = text ?? string.Empty;
            value = UnicodeEscapeRegex.Replace(value, match =>
            {
                if (!int.TryParse(match.Groups[1].Value,
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out int codePoint)) return string.Empty;
                try
                {
                    return char.ConvertFromUtf32(codePoint);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return string.Empty;
                }
            });
            value = value.Replace("\\P", " ")
                .Replace("\\p", " ")
                .Replace("\\~", " ")
                .Replace("%%c", "")
                .Replace("%%C", "");
            value = FormattingCodeRegex.Replace(value, " ")
                .Replace("{", "")
                .Replace("}", "");

            var chars = value.ToCharArray();
            for (int index = 0; index < chars.Length; index++)
            {
                char current = chars[index];
                if (current >= '\uFF10' && current <= '\uFF19')
                    chars[index] = (char)('0' + current - '\uFF10');
                else if (current == '\uFF0E')
                    chars[index] = '.';
                else if (current == '\uFF0C')
                    chars[index] = ',';
                else if (current == '\uFF2D' || current == '\uFF4D')
                    chars[index] = 'm';
            }
            return new string(chars);
        }

        private static bool TryParseToken(Match match, out double millimeters)
        {
            millimeters = 0.0;
            if (match == null || !match.Success) return false;
            if (!double.TryParse(match.Groups[1].Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out double value)) return false;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
                return false;
            millimeters = value;
            return true;
        }
    }
}
