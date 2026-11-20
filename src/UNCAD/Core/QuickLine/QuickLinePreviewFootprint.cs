using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace UNCAD.Core.QuickLine
{
    /// <summary>Whole occupied column bays, expanded by one bay around each route.</summary>
    internal sealed class QuickLinePreviewFootprint
    {
        internal const int MaximumBays = 4096;
        private const double Epsilon = 1e-9;
        private const double Pitch = QuickLineColumnGridPlanner.DefaultSpacingMillimetres;

        private QuickLinePreviewFootprint(HashSet<(int X, int Y)> cells,
            double anchorX, double anchorY)
        {
            var ordered = cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToList();
            Bays = ordered.Select(cell => new Area(
                anchorX + cell.X * Pitch, anchorY + cell.Y * Pitch,
                anchorX + (cell.X + 1) * Pitch, anchorY + (cell.Y + 1) * Pitch))
                .ToList();

            // Merge equal horizontal runs on consecutive rows, without bridging holes.
            var regions = new List<Area>();
            var previous = new Dictionary<(int Start, int End), Area>();
            int previousY = int.MinValue;
            foreach (var row in ordered.GroupBy(cell => cell.Y))
            {
                var current = new Dictionary<(int Start, int End), Area>();
                foreach (var run in Runs(row.Select(cell => cell.X)))
                {
                    if (row.Key != previousY + 1
                        || !previous.TryGetValue(run, out Area area))
                    {
                        area = new Area(anchorX + run.Start * Pitch,
                            anchorY + row.Key * Pitch, anchorX + run.End * Pitch,
                            anchorY + (row.Key + 1) * Pitch);
                        regions.Add(area);
                    }
                    else area.MaxY = anchorY + (row.Key + 1) * Pitch;
                    current.Add(run, area);
                }
                previous = current;
                previousY = row.Key;
            }
            Regions = regions;

            var nodes = new HashSet<(int X, int Y)>();
            var edges = new HashSet<(bool ConstantX, int At, int Along)>();
            var boundary = new HashSet<(bool ConstantX, int At, int Along)>();
            foreach (var cell in ordered)
            {
                nodes.Add((cell.X, cell.Y));
                nodes.Add((cell.X + 1, cell.Y));
                nodes.Add((cell.X, cell.Y + 1));
                nodes.Add((cell.X + 1, cell.Y + 1));
                AddEdge(true, cell.X, cell.Y, !cells.Contains((cell.X - 1, cell.Y)));
                AddEdge(true, cell.X + 1, cell.Y, !cells.Contains((cell.X + 1, cell.Y)));
                AddEdge(false, cell.Y, cell.X, !cells.Contains((cell.X, cell.Y - 1)));
                AddEdge(false, cell.Y + 1, cell.X, !cells.Contains((cell.X, cell.Y + 1)));
            }
            Columns = nodes.OrderBy(node => node.Y).ThenBy(node => node.X)
                .Select(node => new QuickLineSpatialPoint(
                    anchorX + node.X * Pitch, anchorY + node.Y * Pitch, 0)).ToList();
            GridLines = MergeEdges(edges, anchorX, anchorY);
            BoundaryLines = MergeEdges(boundary, anchorX, anchorY);

            void AddEdge(bool constantX, int at, int along, bool outer)
            {
                edges.Add((constantX, at, along));
                if (outer) boundary.Add((constantX, at, along));
            }
        }

        internal IReadOnlyList<Area> Bays { get; }
        internal IReadOnlyList<Area> Regions { get; }
        internal IReadOnlyList<QuickLineSpatialPoint> Columns { get; }
        internal IReadOnlyList<QuickLineColumnGridLine> GridLines { get; }
        internal IReadOnlyList<QuickLineColumnGridLine> BoundaryLines { get; }

        internal IEnumerable<QuickLineOrbitPreviewEnvelope.EnvelopeLabel> AxisLabels(
            QuickLineOrbitPreviewEnvelope envelope)
        {
            if (!envelope.DirectionResolved
                || !QuickLineAxisGridPlanner.TryParseAxis(envelope.UpstreamAxisLabel, out var up)
                || !QuickLineAxisGridPlanner.TryParseAxis(envelope.DownstreamAxisLabel, out var down)
                || !QuickLineRouteDirectionPlanner.TryPlaceAxisRegions(
                    new QuickLineSpatialPoint(envelope.UpstreamEndpoint.X,
                        envelope.UpstreamEndpoint.Y, envelope.UpstreamEndpoint.Z),
                    new QuickLineSpatialPoint(envelope.DownstreamEndpoint.X,
                        envelope.DownstreamEndpoint.Y, envelope.DownstreamEndpoint.Z),
                    up, down, Pitch, out var anchor))
                yield break;
            foreach (QuickLineSpatialPoint column in Columns)
            {
                double number = up.Number - Math.Round((column.Y - anchor.Y) / Pitch);
                long letterIndex = up.LetterIndex + (long)Math.Round((column.X - anchor.X) / Pitch);
                string letter = QuickLineColumnGridPlanner.LettersFromIndex(letterIndex);
                if (number < 0 || number > int.MaxValue || letter == null) continue;
                yield return new QuickLineOrbitPreviewEnvelope.EnvelopeLabel(
                    number.ToString(CultureInfo.InvariantCulture) + "/" + letter,
                    column.X, column.Y, 0, envelope.Diagonal * 0.013);
            }
        }

        internal static QuickLinePreviewFootprint Plan(QuickLineOrbitPreviewEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            double anchorX = envelope.GridAnchor?.X ?? 0;
            double anchorY = envelope.GridAnchor?.Y ?? 0;
            var cells = new HashSet<(int X, int Y)>();
            foreach (var segment in envelope.Segments)
            {
                double ax = (segment.Start.X - anchorX) / Pitch;
                double ay = (segment.Start.Y - anchorY) / Pitch;
                double bx = (segment.End.X - anchorX) / Pitch;
                double by = (segment.End.Y - anchorY) / Pitch;
                double padX = 0, padY = 0;
                if (segment.Channel != null)
                {
                    IncludeWidth(segment.Channel.A1, segment.Start);
                    IncludeWidth(segment.Channel.B1, segment.Start);
                    IncludeWidth(segment.Channel.A2, segment.End);
                    IncludeWidth(segment.Channel.B2, segment.End);
                }
                // Scan only the X strips crossed by the segment; a diagonal never
                // visits its entire bounding rectangle. Closed edges cover both
                // adjacent bays when a route lies exactly on a grid line.
                if (ax > bx)
                {
                    (ax, bx) = (bx, ax);
                    (ay, by) = (by, ay);
                }
                int firstX = LowerCell(ax - padX);
                int lastX = UpperCell(bx + padX);
                CheckSpan(firstX, lastX);
                for (int x = firstX; x <= lastX; x++)
                {
                    double t0 = bx - ax < Epsilon ? 0
                        : Math.Max(0, (x - padX - ax) / (bx - ax));
                    double t1 = bx - ax < Epsilon ? 1
                        : Math.Min(1, (x + 1 + padX - ax) / (bx - ax));
                    double y0 = ay + (by - ay) * t0;
                    double y1 = ay + (by - ay) * t1;
                    int firstY = LowerCell(Math.Min(y0, y1) - padY);
                    int lastY = UpperCell(Math.Max(y0, y1) + padY);
                    CheckSpan(firstY, lastY);
                    for (int y = firstY; y <= lastY; y++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            cells.Add((x + dx, y + dy));
                            if (cells.Count > MaximumBays) throw TooLarge();
                        }
                    }
                }

                void IncludeWidth(QuickLineOrbitPreviewEnvelope.EnvelopePoint rail,
                    QuickLineOrbitPreviewEnvelope.EnvelopePoint center)
                {
                    padX = Math.Max(padX, Math.Abs(rail.X - center.X) / Pitch);
                    padY = Math.Max(padY, Math.Abs(rail.Y - center.Y) / Pitch);
                }
            }
            return new QuickLinePreviewFootprint(cells, anchorX, anchorY);
        }

        private static int LowerCell(double value) => Index(Math.Ceiling(value - Epsilon) - 1);
        private static int UpperCell(double value) => Index(Math.Floor(value + Epsilon));

        private static int Index(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)
                || value < int.MinValue + 2 || value > int.MaxValue - 2)
                throw new ArgumentException("回路坐标无效或超出三维预览范围。");
            return (int)value;
        }

        private static void CheckSpan(int first, int last)
        {
            if ((long)last - first + 1 > MaximumBays) throw TooLarge();
        }

        private static InvalidOperationException TooLarge()
            => new InvalidOperationException(
                "回路占用范围超过 4096 个柱跨，请缩小预览选集。");

        private static IEnumerable<(int Start, int End)> Runs(IEnumerable<int> source)
        {
            int? start = null;
            int end = 0;
            foreach (int value in source.OrderBy(value => value))
            {
                if (start.HasValue && value > end)
                {
                    yield return (start.Value, end);
                    start = null;
                }
                if (!start.HasValue) start = value;
                end = value + 1;
            }
            if (start.HasValue) yield return (start.Value, end);
        }

        private static IReadOnlyList<QuickLineColumnGridLine> MergeEdges(
            HashSet<(bool ConstantX, int At, int Along)> edges,
            double anchorX, double anchorY)
        {
            var result = new List<QuickLineColumnGridLine>();
            foreach (var line in edges.GroupBy(edge => (edge.ConstantX, edge.At))
                .OrderBy(group => group.Key.ConstantX).ThenBy(group => group.Key.At))
            {
                bool constantX = line.Key.ConstantX;
                foreach (var run in Runs(line.Select(edge => edge.Along)))
                    result.Add(new QuickLineColumnGridLine(constantX,
                        (constantX ? anchorX : anchorY) + line.Key.At * Pitch,
                        (constantX ? anchorY : anchorX) + run.Start * Pitch,
                        (constantX ? anchorY : anchorX) + run.End * Pitch));
            }
            return result;
        }

        internal sealed class Area
        {
            internal Area(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            internal double MinX { get; }
            internal double MinY { get; }
            internal double MaxX { get; }
            internal double MaxY { get; set; }
        }
    }
}
