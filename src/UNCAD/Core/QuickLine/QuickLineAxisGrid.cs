using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// Read-only grid marks used by the U1L3D preview.  The downstream axis is
    /// the origin; adjacent numeric or alphabetic marks are 4800 mm apart.
    /// </summary>
    public sealed class QuickLineAxisGrid
    {
        internal QuickLineAxisGrid(string upstreamAxis, string downstreamAxis,
            IList<QuickLineAxisMark> numericMarks,
            IList<QuickLineAxisMark> alphabeticMarks,
            IList<string> diagnostics, double spacingMillimetres)
        {
            UpstreamAxis = upstreamAxis ?? string.Empty;
            DownstreamAxis = downstreamAxis ?? string.Empty;
            NumericMarks = new ReadOnlyCollection<QuickLineAxisMark>(numericMarks
                ?? new List<QuickLineAxisMark>());
            AlphabeticMarks = new ReadOnlyCollection<QuickLineAxisMark>(alphabeticMarks
                ?? new List<QuickLineAxisMark>());
            Diagnostics = new ReadOnlyCollection<string>(diagnostics
                ?? new List<string>());
            SpacingMillimetres = spacingMillimetres;
        }

        public string UpstreamAxis { get; }
        public string DownstreamAxis { get; }
        public double SpacingMillimetres { get; }
        public IReadOnlyList<QuickLineAxisMark> NumericMarks { get; }
        public IReadOnlyList<QuickLineAxisMark> AlphabeticMarks { get; }
        public IReadOnlyList<string> Diagnostics { get; }
    }

    /// <summary>One numeric or alphabetic grid line relative to downstream.</summary>
    public sealed class QuickLineAxisMark
    {
        internal QuickLineAxisMark(string label, double offsetMillimetres)
        {
            Label = label ?? string.Empty;
            OffsetMillimetres = offsetMillimetres;
        }

        public string Label { get; }
        public double OffsetMillimetres { get; }
    }

    /// <summary>Plans the two grid directions without inventing coordinates for bad data.</summary>
    public static class QuickLineAxisGridPlanner
    {
        public const double DefaultSpacingMillimetres = 4800.0;
        private const int MaximumMarksPerAxis = 200;
        private static readonly Regex AxisPattern = new Regex(
            @"^\s*(?<number>[0-9]+)\s*/\s*(?<letter>[A-Za-z]+)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static QuickLineAxisGrid Plan(string upstreamAxis, string downstreamAxis,
            double spacingMillimetres = DefaultSpacingMillimetres)
        {
            if (double.IsNaN(spacingMillimetres) || double.IsInfinity(spacingMillimetres)
                || spacingMillimetres <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(spacingMillimetres));

            string upstream = (upstreamAxis ?? string.Empty).Trim();
            string downstream = (downstreamAxis ?? string.Empty).Trim();
            var diagnostics = new List<string>();
            var numeric = new List<QuickLineAxisMark>();
            var alphabetic = new List<QuickLineAxisMark>();
            bool upstreamParsed = TryParse(upstream, out AxisValue up);
            bool downstreamParsed = TryParse(downstream, out AxisValue down);

            if (!upstreamParsed)
                diagnostics.Add("上游轴位“" + upstream + "”不是 数字/字母 格式，未推算数字轴网。");
            if (!downstreamParsed)
                diagnostics.Add("下游轴位“" + downstream + "”不是 数字/字母 格式，未推算字母轴网。");

            if (upstreamParsed && downstreamParsed)
            {
                AddNumericRange(numeric, down.Number, up.Number,
                    spacingMillimetres, diagnostics);
                AddLetterRange(alphabetic, down, up, spacingMillimetres,
                    diagnostics);
            }
            else
            {
                // A partially valid value is still useful for a diagnostic marker,
                // but no synthetic range is emitted from an unknown endpoint.
                if (downstreamParsed)
                {
                    numeric.Add(new QuickLineAxisMark(down.Number.ToString(), 0.0));
                    alphabetic.Add(new QuickLineAxisMark(down.Letter, 0.0));
                }
                if (upstreamParsed)
                {
                    if (!numeric.Any(item => string.Equals(item.Label,
                            up.Number.ToString(), StringComparison.OrdinalIgnoreCase)))
                        numeric.Add(new QuickLineAxisMark(up.Number.ToString(), 0.0));
                    if (!alphabetic.Any(item => string.Equals(item.Label,
                            up.Letter, StringComparison.OrdinalIgnoreCase)))
                        alphabetic.Add(new QuickLineAxisMark(up.Letter, 0.0));
                }
            }

            return new QuickLineAxisGrid(upstream, downstream, numeric, alphabetic,
                diagnostics, spacingMillimetres);
        }

        private static void AddNumericRange(ICollection<QuickLineAxisMark> target,
            int downstream, int upstream, double spacing,
            ICollection<string> diagnostics)
        {
            int step = downstream <= upstream ? 1 : -1;
            long count = Math.Abs((long)upstream - downstream);
            if (count >= MaximumMarksPerAxis)
            {
                diagnostics.Add("数字轴位范围超过 " + MaximumMarksPerAxis
                    + " 格，只显示上下游端点。");
                target.Add(new QuickLineAxisMark(downstream.ToString(), 0.0));
                target.Add(new QuickLineAxisMark(upstream.ToString(),
                    count * step * spacing));
                return;
            }
            for (int index = 0; index <= count; index++)
            {
                int value = downstream + index * step;
                target.Add(new QuickLineAxisMark(value.ToString(),
                    index * step * spacing));
            }
        }

        private static void AddLetterRange(ICollection<QuickLineAxisMark> target,
            AxisValue downstream, AxisValue upstream, double spacing,
            ICollection<string> diagnostics)
        {
            long difference = upstream.LetterIndex - downstream.LetterIndex;
            long count = Math.Abs(difference);
            if (count >= MaximumMarksPerAxis)
            {
                diagnostics.Add("字母轴位范围超过 " + MaximumMarksPerAxis
                    + " 格，只显示上下游端点。");
                target.Add(new QuickLineAxisMark(downstream.Letter, 0.0));
                target.Add(new QuickLineAxisMark(upstream.Letter,
                    difference * spacing));
                return;
            }

            long step = difference < 0 ? -1 : 1;
            for (int index = 0; index <= count; index++)
            {
                target.Add(new QuickLineAxisMark(ToLetters(
                    downstream.LetterIndex + index * step), index * step * spacing));
            }
        }

        private static string ToLetters(long index)
        {
            var letters = new System.Text.StringBuilder();
            while (index > 0)
            {
                index--;
                letters.Insert(0, (char)('A' + index % 26));
                index /= 26;
            }
            return letters.ToString();
        }

        private static bool TryParse(string value, out AxisValue axis)
        {
            axis = default(AxisValue);
            Match match = AxisPattern.Match(value ?? string.Empty);
            if (!match.Success || !int.TryParse(match.Groups["number"].Value,
                    out int number)) return false;
            string letter = match.Groups["letter"].Value.ToUpperInvariant();
            if (letter.Length == 0) return false;
            long letterIndex = 0;
            foreach (char item in letter)
            {
                long digit = item - 'A' + 1;
                if (letterIndex > (long.MaxValue - digit) / 26) return false;
                letterIndex = letterIndex * 26 + digit;
            }
            axis = new AxisValue(number, letter, letterIndex);
            return true;
        }

        private readonly struct AxisValue
        {
            public AxisValue(int number, string letter, long letterIndex)
            {
                Number = number;
                Letter = letter;
                LetterIndex = letterIndex;
            }

            public int Number { get; }
            public string Letter { get; }
            public long LetterIndex { get; }
        }
    }
}
