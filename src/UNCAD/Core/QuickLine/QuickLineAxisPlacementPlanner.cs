using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// Input used to place one selected frame in a shared U13D coordinate system.
    /// The downstream axis is the frame anchor; the upstream value is validated
    /// for diagnostics but does not override the anchor.
    /// </summary>
    public sealed class QuickLineAxisPlacementInput
    {
        public QuickLineAxisPlacementInput(string id, string downstreamAxis,
            string upstreamAxis)
        {
            Id = id ?? string.Empty;
            DownstreamAxis = (downstreamAxis ?? string.Empty).Trim();
            UpstreamAxis = (upstreamAxis ?? string.Empty).Trim();
        }

        public string Id { get; }
        public string DownstreamAxis { get; }
        public string UpstreamAxis { get; }
    }

    /// <summary>One frame's deterministic offset in the shared 3D preview.</summary>
    public sealed class QuickLineAxisPlacement
    {
        internal QuickLineAxisPlacement(string id, double offsetX,
            double offsetY, bool resolved, IList<string> diagnostics)
        {
            Id = id ?? string.Empty;
            OffsetXMillimetres = offsetX;
            OffsetYMillimetres = offsetY;
            Resolved = resolved;
            Diagnostics = (diagnostics ?? new List<string>()).ToArray();
        }

        public string Id { get; }
        public double OffsetXMillimetres { get; }
        public double OffsetYMillimetres { get; }
        public bool Resolved { get; }
        public IReadOnlyList<string> Diagnostics { get; }
    }

    /// <summary>
    /// Aligns multiple frame previews by workbook axes. Invalid or missing
    /// axes receive a visible deterministic fallback instead of overlapping
    /// silently with a valid frame.
    /// </summary>
    public static class QuickLineAxisPlacementPlanner
    {
        public static IReadOnlyList<QuickLineAxisPlacement> Plan(
            IEnumerable<QuickLineAxisPlacementInput> inputs,
            double spacingMillimetres = QuickLineAxisGridPlanner.DefaultSpacingMillimetres)
        {
            if (double.IsNaN(spacingMillimetres)
                || double.IsInfinity(spacingMillimetres)
                || spacingMillimetres <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(spacingMillimetres));

            List<QuickLineAxisPlacementInput> values = (inputs
                ?? Enumerable.Empty<QuickLineAxisPlacementInput>()).ToList();
            var parsed = new Dictionary<string, QuickLineAxisCoordinate>(
                StringComparer.OrdinalIgnoreCase);
            int minNumber = int.MaxValue;
            long minLetter = long.MaxValue;
            int maxNumber = int.MinValue;
            foreach (QuickLineAxisPlacementInput input in values)
            {
                if (input == null || string.IsNullOrWhiteSpace(input.Id)
                    || !QuickLineAxisGridPlanner.TryParseAxis(input.DownstreamAxis,
                        out QuickLineAxisCoordinate axis))
                    continue;
                if (parsed.ContainsKey(input.Id)) continue;
                parsed[input.Id] = axis;
                minNumber = Math.Min(minNumber, axis.Number);
                minLetter = Math.Min(minLetter, axis.LetterIndex);
                maxNumber = Math.Max(maxNumber, axis.Number);
            }

            var result = new List<QuickLineAxisPlacement>();
            int fallbackIndex = 0;
            foreach (QuickLineAxisPlacementInput input in values)
            {
                if (input == null) continue;
                var diagnostics = new List<string>();
                if (parsed.TryGetValue(input.Id, out QuickLineAxisCoordinate axis))
                {
                    if (!QuickLineAxisGridPlanner.TryParseAxis(input.UpstreamAxis,
                        out _))
                        diagnostics.Add("上游轴位“" + input.UpstreamAxis
                            + "”无法解析，仅按下游轴位对齐。");
                    result.Add(new QuickLineAxisPlacement(input.Id,
                        (axis.Number - minNumber) * spacingMillimetres,
                        (axis.LetterIndex - minLetter) * spacingMillimetres,
                        true, diagnostics));
                    continue;
                }

                diagnostics.Add("下游轴位“" + input.DownstreamAxis
                    + "”无法解析，已使用临时排列位置。");
                double fallbackX = maxNumber == int.MinValue
                    ? fallbackIndex * spacingMillimetres * 2.0
                    : (maxNumber - minNumber + 1 + fallbackIndex)
                        * spacingMillimetres;
                result.Add(new QuickLineAxisPlacement(input.Id,
                    fallbackX, 0.0,
                    false, diagnostics));
                fallbackIndex++;
            }
            return result;
        }
    }
}
