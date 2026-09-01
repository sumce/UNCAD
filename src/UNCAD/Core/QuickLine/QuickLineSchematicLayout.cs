using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    public sealed class QuickLineSchematicSegment
    {
        internal QuickLineSchematicSegment(string id, QuickLinePoint start,
            QuickLinePoint end)
        {
            Id = id;
            Start = start;
            End = end;
        }

        public string Id { get; }
        public QuickLinePoint Start { get; }
        public QuickLinePoint End { get; }
        public double DisplayLength => Start.DistanceTo(End);
    }

    /// <summary>
    /// CAD-facing schematic layout. Actual millimetres remain in annotations;
    /// these points only control how the route occupies its original square.
    /// </summary>
    public sealed class QuickLineSchematicLayout
    {
        internal QuickLineSchematicLayout(
            IList<QuickLineSchematicSegment> segments, double targetSide)
        {
            Segments = new ReadOnlyCollection<QuickLineSchematicSegment>(segments);
            TargetSide = targetSide;
        }

        public IReadOnlyList<QuickLineSchematicSegment> Segments { get; }
        public double TargetSide { get; }
    }

    public static class QuickLineSchematicLayoutBuilder
    {
        public const double DefaultMaximumSide = 78021.0;
        private const double MinimumRatio = 0.35;
        private const double MaximumRatio = 2.75;
        private const double Epsilon = 1e-9;

        /// <summary>
        /// Projects newly drawn spatial segments to AutoCAD's southeast
        /// isometric directions and scales the complete route into the fixed
        /// drawing square. Actual distances are used only as source values;
        /// visual lengths are clamped around a geometric median so outliers
        /// do not make short segments disappear.
        /// </summary>
        public static QuickLineSchematicLayout BuildCreated(
            IReadOnlyList<QuickLineCreatedSegment> createdSegments,
            double maximumSide = DefaultMaximumSide)
        {
            if (createdSegments == null)
                throw new ArgumentNullException(nameof(createdSegments));
            if (createdSegments.Count == 0)
                throw new ArgumentException("路线至少需要一根线段。",
                    nameof(createdSegments));
            if (!IsFinite(maximumSide) || maximumSide <= Epsilon)
                throw new ArgumentOutOfRangeException(nameof(maximumSide));

            var segmentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineCreatedSegment segment in createdSegments)
            {
                if (segment == null || string.IsNullOrWhiteSpace(segment.Id)
                    || string.IsNullOrWhiteSpace(segment.StartNodeId)
                    || string.IsNullOrWhiteSpace(segment.EndNodeId)
                    || string.Equals(segment.StartNodeId, segment.EndNodeId,
                        StringComparison.OrdinalIgnoreCase)
                    || !segmentIds.Add(segment.Id)
                    || !Enum.IsDefined(typeof(QuickLineSpatialAxis), segment.Axis)
                    || (segment.DirectionSign != -1 && segment.DirectionSign != 1)
                    || !IsFinite(segment.DistanceMillimetres)
                    || segment.DistanceMillimetres <= Epsilon)
                    throw new ArgumentException("新建路线包含无效线段。",
                        nameof(createdSegments));
            }

            var visual = BuildCreatedVisualLengths(createdSegments);
            var positions3d = new Dictionary<string, QuickLineSpatialPoint>(
                StringComparer.OrdinalIgnoreCase) { ["N1"] =
                    new QuickLineSpatialPoint(0.0, 0.0, 0.0) };
            var pending = new Queue<QuickLineCreatedSegment>(createdSegments);
            int guard = 0;
            while (pending.Count > 0 && guard++ <= createdSegments.Count * 3)
            {
                QuickLineCreatedSegment segment = pending.Dequeue();
                bool hasStart = positions3d.TryGetValue(segment.StartNodeId,
                    out QuickLineSpatialPoint start);
                bool hasEnd = positions3d.TryGetValue(segment.EndNodeId,
                    out QuickLineSpatialPoint end);
                QuickLineSpatialVector delta = CreatedAxisVector(segment.Axis)
                    * (segment.DirectionSign * visual[segment.Id]);
                if (hasStart && !hasEnd)
                {
                    positions3d[segment.EndNodeId] = start + delta;
                    continue;
                }
                if (!hasStart && hasEnd)
                {
                    positions3d[segment.StartNodeId] = end - delta;
                    continue;
                }
                if (!hasStart && !hasEnd)
                {
                    pending.Enqueue(segment);
                    continue;
                }
                // A closed route may not be geometrically exact after visual
                // compression. Keep the existing node and still draw it.
            }
            if (positions3d.Count < 2
                || createdSegments.Any(item => !positions3d.ContainsKey(item.StartNodeId)
                    || !positions3d.ContainsKey(item.EndNodeId)))
                throw new ArgumentException("新建路线必须从 N1 连通。",
                    nameof(createdSegments));

            var projected = positions3d.ToDictionary(item => item.Key,
                item => ProjectIsometric(item.Value), StringComparer.OrdinalIgnoreCase);
            Bounds bounds = Bounds.From(projected.Values);
            double side = Math.Max(bounds.Width, bounds.Height);
            if (side <= Epsilon) throw new ArgumentException("路线没有有效的投影范围。",
                nameof(createdSegments));
            double scale = maximumSide / side;
            QuickLinePoint center = bounds.Center;
            var output = createdSegments.Select(item =>
                new QuickLineSchematicSegment(item.Id,
                    new QuickLinePoint((projected[item.StartNodeId].X - center.X) * scale,
                        (projected[item.StartNodeId].Y - center.Y) * scale),
                    new QuickLinePoint((projected[item.EndNodeId].X - center.X) * scale,
                        (projected[item.EndNodeId].Y - center.Y) * scale)))
                .ToList();
            return new QuickLineSchematicLayout(output, Math.Min(maximumSide,
                side * scale));
        }

        private static Dictionary<string, double> BuildCreatedVisualLengths(
            IReadOnlyList<QuickLineCreatedSegment> segments)
        {
            var values = segments.Select(item => item.DistanceMillimetres)
                .Where(item => IsFinite(item) && item > Epsilon).OrderBy(item => item)
                .ToList();
            if (values.Count != segments.Count)
                throw new ArgumentException("新建路线包含无效距离。", nameof(segments));
            int middle = values.Count / 2;
            double reference = values.Count % 2 == 1 ? values[middle]
                : Math.Sqrt(values[middle - 1] * values[middle]);
            double minimum = reference * MinimumRatio;
            double maximum = reference * MaximumRatio;
            return segments.ToDictionary(item => item.Id,
                item => Math.Max(minimum, Math.Min(maximum,
                    item.DistanceMillimetres)), StringComparer.OrdinalIgnoreCase);
        }

        private static QuickLinePoint ProjectIsometric(QuickLineSpatialPoint point)
            => new QuickLinePoint(point.X * 0.8660254037844386
                - point.Y * 0.8660254037844386,
                point.X * 0.5 + point.Y * 0.5 + point.Z);

        private static QuickLineSpatialVector CreatedAxisVector(
            QuickLineSpatialAxis axis)
        {
            switch (axis)
            {
                case QuickLineSpatialAxis.X:
                    return new QuickLineSpatialVector(1.0, 0.0, 0.0);
                case QuickLineSpatialAxis.Y:
                    return new QuickLineSpatialVector(0.0, 1.0, 0.0);
                case QuickLineSpatialAxis.Z:
                    return new QuickLineSpatialVector(0.0, 0.0, 1.0);
                default: throw new ArgumentOutOfRangeException(nameof(axis));
            }
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        public static QuickLineSchematicLayout Build(QuickLineGraph graph,
            QuickLineIsometricScene scene,
            IReadOnlyDictionary<string, double> actualDistancesMillimetres)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (actualDistancesMillimetres == null)
                throw new ArgumentNullException(nameof(actualDistancesMillimetres));
            if (scene.Segments.Count == 0)
                throw new ArgumentException("场景没有可布局的线段。", nameof(scene));

            Dictionary<string, QuickLinePoint> originalNodes =
                BuildOriginalNodes(graph, scene);
            Bounds originalBounds = Bounds.From(originalNodes.Values);
            double targetSide = Math.Min(DefaultMaximumSide,
                Math.Max(originalBounds.Width, originalBounds.Height));
            if (targetSide <= Epsilon)
                throw new ArgumentException("原线路没有有效占用范围。", nameof(scene));

            Dictionary<string, double> visualLengths = BuildVisualLengths(
                scene.Segments, actualDistancesMillimetres);
            Dictionary<string, QuickLinePoint> rawNodes = Embed(graph, scene,
                originalNodes[scene.RootNodeId], visualLengths);
            Bounds rawBounds = Bounds.From(rawNodes.Values);
            double rawSide = Math.Max(rawBounds.Width, rawBounds.Height);
            if (rawSide <= Epsilon)
                throw new ArgumentException("调整后的线路没有有效占用范围。",
                    nameof(actualDistancesMillimetres));

            double scale = targetSide / rawSide;
            QuickLinePoint originalCenter = originalBounds.Center;
            QuickLinePoint rawCenter = rawBounds.Center;
            var finalNodes = rawNodes.ToDictionary(item => item.Key,
                item => new QuickLinePoint(
                    originalCenter.X + (item.Value.X - rawCenter.X) * scale,
                    originalCenter.Y + (item.Value.Y - rawCenter.Y) * scale),
                StringComparer.OrdinalIgnoreCase);

            var segments = scene.Segments.Select(item =>
                new QuickLineSchematicSegment(item.Id,
                    finalNodes[item.StartNodeId], finalNodes[item.EndNodeId]))
                .ToList();
            return new QuickLineSchematicLayout(segments, targetSide);
        }

        private static Dictionary<string, double> BuildVisualLengths(
            IReadOnlyList<QuickLineIsometricSegment> segments,
            IReadOnlyDictionary<string, double> actualDistances)
        {
            var values = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            var positive = new List<double>();
            foreach (QuickLineIsometricSegment segment in segments)
            {
                if (!actualDistances.TryGetValue(segment.Id, out double value)
                    || double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
                    throw new ArgumentException("线段 " + segment.Id
                        + " 缺少有效真实距离。", nameof(actualDistances));
                values[segment.Id] = value;
                if (value > 0.0) positive.Add(value);
            }

            positive.Sort();
            double reference = 1.0;
            if (positive.Count > 0)
            {
                int middle = positive.Count / 2;
                reference = positive.Count % 2 == 1
                    ? positive[middle]
                    : Math.Exp((Math.Log(positive[middle - 1])
                        + Math.Log(positive[middle])) / 2.0);
            }
            double minimum = reference * MinimumRatio;
            double maximum = reference * MaximumRatio;
            foreach (string id in values.Keys.ToArray())
                values[id] = Math.Max(minimum, Math.Min(maximum, values[id]));
            return values;
        }

        private static Dictionary<string, QuickLinePoint> BuildOriginalNodes(
            QuickLineGraph graph, QuickLineIsometricScene scene)
        {
            var accumulators = new Dictionary<string, NodeAccumulator>(
                StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineIsometricSegment sceneSegment in scene.Segments)
            {
                QuickLineSegment source = graph.GetSegment(sceneSegment.Id);
                Add(accumulators, sceneSegment.StartNodeId, source.Start);
                Add(accumulators, sceneSegment.EndNodeId, source.End);
            }
            return accumulators.ToDictionary(item => item.Key,
                item => item.Value.Average, StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, QuickLinePoint> Embed(
            QuickLineGraph graph, QuickLineIsometricScene scene,
            QuickLinePoint rootPosition,
            IReadOnlyDictionary<string, double> visualLengths)
        {
            var incident = new Dictionary<string,
                List<QuickLineIsometricSegment>>(StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineIsometricSegment segment in scene.Segments)
            {
                AddIncident(incident, segment.StartNodeId, segment);
                AddIncident(incident, segment.EndNodeId, segment);
            }

            var positions = new Dictionary<string, QuickLinePoint>(
                StringComparer.OrdinalIgnoreCase)
            {
                [scene.RootNodeId] = rootPosition
            };
            var queue = new Queue<string>();
            queue.Enqueue(scene.RootNodeId);
            double closureTolerance = Math.Max(1e-6,
                graph.EndpointTolerance * 2.0);
            while (queue.Count > 0)
            {
                string nodeId = queue.Dequeue();
                QuickLinePoint current = positions[nodeId];
                if (!incident.TryGetValue(nodeId,
                        out List<QuickLineIsometricSegment> edges)) continue;
                foreach (QuickLineIsometricSegment edge in edges)
                {
                    QuickLineSegment source = graph.GetSegment(edge.Id);
                    double sourceLength = source.Length;
                    if (sourceLength <= Epsilon)
                        throw new ArgumentException("线段 " + edge.Id
                            + " 长度无效。", nameof(graph));
                    double dx = (source.End.X - source.Start.X) / sourceLength
                        * visualLengths[edge.Id];
                    double dy = (source.End.Y - source.Start.Y) / sourceLength
                        * visualLengths[edge.Id];
                    bool fromStart = string.Equals(nodeId, edge.StartNodeId,
                        StringComparison.OrdinalIgnoreCase);
                    string otherId = fromStart ? edge.EndNodeId : edge.StartNodeId;
                    var candidate = new QuickLinePoint(
                        current.X + (fromStart ? dx : -dx),
                        current.Y + (fromStart ? dy : -dy));
                    if (!positions.TryGetValue(otherId,
                            out QuickLinePoint existing))
                    {
                        positions[otherId] = candidate;
                        queue.Enqueue(otherId);
                    }
                    else if (existing.DistanceTo(candidate) > closureTolerance)
                    {
                        throw new ArgumentException("线路包含无法按新比例闭合的环路。",
                            nameof(visualLengths));
                    }
                }
            }
            if (positions.Count != scene.Nodes.Count)
                throw new ArgumentException("线路包含未连接的节点。", nameof(scene));
            return positions;
        }

        private static void Add(
            IDictionary<string, NodeAccumulator> accumulators,
            string nodeId, QuickLinePoint point)
        {
            if (!accumulators.TryGetValue(nodeId,
                    out NodeAccumulator accumulator))
                accumulator = new NodeAccumulator();
            accumulator.Add(point);
            accumulators[nodeId] = accumulator;
        }

        private static void AddIncident(
            IDictionary<string, List<QuickLineIsometricSegment>> incident,
            string nodeId, QuickLineIsometricSegment segment)
        {
            if (!incident.TryGetValue(nodeId,
                    out List<QuickLineIsometricSegment> list))
            {
                list = new List<QuickLineIsometricSegment>();
                incident[nodeId] = list;
            }
            list.Add(segment);
        }

        private struct NodeAccumulator
        {
            private double _x;
            private double _y;
            private int _count;

            public void Add(QuickLinePoint point)
            {
                _x += point.X;
                _y += point.Y;
                _count++;
            }

            public QuickLinePoint Average
                => new QuickLinePoint(_x / _count, _y / _count);
        }

        private readonly struct Bounds
        {
            private Bounds(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public double Width => MaxX - MinX;
            public double Height => MaxY - MinY;
            public QuickLinePoint Center => new QuickLinePoint(
                (MinX + MaxX) / 2.0, (MinY + MaxY) / 2.0);

            public static Bounds From(IEnumerable<QuickLinePoint> points)
            {
                QuickLinePoint[] values = points.ToArray();
                if (values.Length == 0)
                    throw new ArgumentException("无法计算空线路范围。",
                        nameof(points));
                return new Bounds(values.Min(item => item.X),
                    values.Min(item => item.Y), values.Max(item => item.X),
                    values.Max(item => item.Y));
            }
        }
    }
}
