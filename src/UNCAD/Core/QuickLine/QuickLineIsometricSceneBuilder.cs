using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// Reconstructs a connected isometric or orthographic sketch as an
    /// orthogonal X/Y/Z route. Actual label values and initial editor geometry
    /// lengths are kept separate so U1L placeholders do not distort the view.
    /// </summary>
    public static class QuickLineIsometricSceneBuilder
    {
        private const double DefaultAngleToleranceDegrees = 3.0;

        /// <summary>Creates the blank southeast-isometric drawing workspace.</summary>
        public static QuickLineIsometricScene CreateDrawingScene()
            => new QuickLineIsometricScene("N1",
                QuickLineProjectionMode.Isometric,
                new List<QuickLineIsometricNode>
                {
                    new QuickLineIsometricNode("N1",
                        new QuickLineSpatialPoint(0.0, 0.0, 0.0))
                },
                new List<QuickLineIsometricSegment>(),
                new List<string>());

        public static QuickLineIsometricScene Build(QuickLineGraph graph,
            string selectedSegmentId,
            IReadOnlyDictionary<string, double> distancesMillimetres,
            double angleToleranceDegrees = DefaultAngleToleranceDegrees,
            QuickLineProjectionMode projectionMode = QuickLineProjectionMode.Auto)
        {
            if (distancesMillimetres == null)
                throw new ArgumentNullException(nameof(distancesMillimetres));
            return Build(graph, selectedSegmentId, distancesMillimetres,
                distancesMillimetres, new HashSet<string>(
                    distancesMillimetres.Keys, StringComparer.OrdinalIgnoreCase),
                angleToleranceDegrees, projectionMode);
        }

        public static QuickLineIsometricScene Build(QuickLineGraph graph,
            string selectedSegmentId,
            IReadOnlyDictionary<string, double> distancesMillimetres,
            IReadOnlyDictionary<string, double> displayDistancesMillimetres,
            ISet<string> completedSegmentIds,
            double angleToleranceDegrees = DefaultAngleToleranceDegrees,
            QuickLineProjectionMode projectionMode = QuickLineProjectionMode.Auto)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (distancesMillimetres == null)
                throw new ArgumentNullException(nameof(distancesMillimetres));
            if (displayDistancesMillimetres == null)
                throw new ArgumentNullException(nameof(displayDistancesMillimetres));
            if (completedSegmentIds == null)
                throw new ArgumentNullException(nameof(completedSegmentIds));
            ValidateTolerance(angleToleranceDegrees);
            ValidateProjectionMode(projectionMode);

            QuickLineSegment selected = graph.GetSegment(selectedSegmentId);
            QuickLineProjectionMode resolvedMode = ResolveProjectionMode(graph,
                selected, angleToleranceDegrees, projectionMode);
            var classified = new Dictionary<string, AxisMatch>(
                StringComparer.OrdinalIgnoreCase);
            var diagnostics = new List<string>();
            foreach (QuickLineSegment segment in CollectRawComponent(graph,
                selected.Id))
            {
                if (TryClassify(segment, angleToleranceDegrees, resolvedMode,
                        out AxisMatch match))
                    classified[segment.Id] = match;
            }
            if (!classified.ContainsKey(selected.Id))
                throw new ArgumentException("选中的线段不属于可识别的等轴测或正交方向。",
                    nameof(selectedSegmentId));

            List<QuickLineSegment> component = CollectComponent(graph, selected.Id,
                classified, diagnostics);
            var endpoints = new EndpointDisjointSet(component);
            var componentIds = new HashSet<string>(
                component.Select(item => item.Id), StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineSegment segment in component)
            {
                foreach (QuickLineConnection connection in graph.GetConnections(segment.Id))
                {
                    if (!componentIds.Contains(connection.ConnectedSegmentId)) continue;
                    endpoints.Union(Key(segment.Id, connection.Endpoint),
                        Key(connection.ConnectedSegmentId,
                            connection.ConnectedEndpoint));
                }
            }

            Dictionary<string, string> nodeIds = endpoints.BuildNodeIds();
            var sceneSegments = new List<QuickLineIsometricSegment>();
            foreach (QuickLineSegment segment in component)
            {
                if (!distancesMillimetres.TryGetValue(segment.Id, out double distance)
                    || double.IsNaN(distance) || double.IsInfinity(distance)
                    || distance < 0.0)
                    throw new ArgumentException("线段 " + segment.Id
                        + " 缺少有效毫米距离。", nameof(distancesMillimetres));
                if (!displayDistancesMillimetres.TryGetValue(segment.Id,
                        out double displayDistance)
                    || double.IsNaN(displayDistance)
                    || double.IsInfinity(displayDistance)
                    || displayDistance < 0.0)
                    throw new ArgumentException("线段 " + segment.Id
                        + " 缺少有效初始显示距离。",
                        nameof(displayDistancesMillimetres));

                AxisMatch match = classified[segment.Id];
                sceneSegments.Add(new QuickLineIsometricSegment(segment.Id,
                    nodeIds[Key(segment.Id, QuickLineEndpoint.Start)],
                    nodeIds[Key(segment.Id, QuickLineEndpoint.End)],
                    match.Axis, match.DirectionSign, match.PlanAngleDegrees,
                    distance, displayDistance,
                    completedSegmentIds.Contains(segment.Id)));
            }

            string rootNodeId = nodeIds[Key(selected.Id, QuickLineEndpoint.Start)];
            Dictionary<string, QuickLineSpatialPoint> positions = Embed(rootNodeId,
                sceneSegments, diagnostics);
            var nodes = nodeIds.Values.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(NodeNumber)
                .Select(id => new QuickLineIsometricNode(id, positions[id]))
                .ToList();
            return new QuickLineIsometricScene(rootNodeId, resolvedMode, nodes,
                sceneSegments, diagnostics);
        }

        public static bool TryClassify(QuickLineSegment segment,
            double angleToleranceDegrees, out QuickLineSpatialAxis axis,
            out int directionSign, out double planAngleDegrees)
        {
            ValidateTolerance(angleToleranceDegrees);
            if (TryClassify(segment, angleToleranceDegrees,
                    QuickLineProjectionMode.Isometric, out AxisMatch match))
            {
                axis = match.Axis;
                directionSign = match.DirectionSign;
                planAngleDegrees = match.PlanAngleDegrees;
                return true;
            }
            axis = QuickLineSpatialAxis.X;
            directionSign = 0;
            planAngleDegrees = 0.0;
            return false;
        }

        /// <summary>
        /// Classifies a conventional orthographic sketch. Horizontal screen
        /// lines become the abstract X axis and vertical screen lines become
        /// the abstract Z axis; the editor can map that pair to another plane.
        /// </summary>
        public static bool TryClassifyOrthographic(QuickLineSegment segment,
            double angleToleranceDegrees, out QuickLineSpatialAxis axis,
            out int directionSign, out double planAngleDegrees)
        {
            ValidateTolerance(angleToleranceDegrees);
            if (TryClassify(segment, angleToleranceDegrees,
                    QuickLineProjectionMode.Orthographic, out AxisMatch match))
            {
                axis = match.Axis;
                directionSign = match.DirectionSign;
                planAngleDegrees = match.PlanAngleDegrees;
                return true;
            }
            axis = QuickLineSpatialAxis.X;
            directionSign = 0;
            planAngleDegrees = 0.0;
            return false;
        }

        private static List<QuickLineSegment> CollectComponent(QuickLineGraph graph,
            string selectedId, IDictionary<string, AxisMatch> classified,
            ICollection<string> diagnostics)
        {
            var result = new List<QuickLineSegment>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            visited.Add(selectedId);
            queue.Enqueue(selectedId);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                result.Add(graph.GetSegment(id));
                foreach (QuickLineConnection connection in graph.GetConnections(id))
                {
                    string neighborId = connection.ConnectedSegmentId;
                    if (!classified.ContainsKey(neighborId))
                    {
                        diagnostics.Add("已忽略不符合当前投影模式的线段 " + neighborId + "。");
                        continue;
                    }
                    if (visited.Add(neighborId)) queue.Enqueue(neighborId);
                }
            }
            return result;
        }

        private static Dictionary<string, QuickLineSpatialPoint> Embed(
            string rootNodeId, IReadOnlyList<QuickLineIsometricSegment> segments,
            ICollection<string> diagnostics)
        {
            var incident = new Dictionary<string, List<QuickLineIsometricSegment>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineIsometricSegment segment in segments)
            {
                AddIncident(incident, segment.StartNodeId, segment);
                AddIncident(incident, segment.EndNodeId, segment);
            }

            var positions = new Dictionary<string, QuickLineSpatialPoint>(
                StringComparer.OrdinalIgnoreCase)
            {
                [rootNodeId] = new QuickLineSpatialPoint(0.0, 0.0, 0.0)
            };
            var queue = new Queue<string>();
            queue.Enqueue(rootNodeId);
            var reportedCycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (queue.Count > 0)
            {
                string nodeId = queue.Dequeue();
                QuickLineSpatialPoint current = positions[nodeId];
                if (!incident.TryGetValue(nodeId,
                        out List<QuickLineIsometricSegment> edges)) continue;
                foreach (QuickLineIsometricSegment edge in edges)
                {
                    bool fromStart = string.Equals(nodeId, edge.StartNodeId,
                        StringComparison.OrdinalIgnoreCase);
                    string otherId = fromStart ? edge.EndNodeId : edge.StartNodeId;
                    QuickLineSpatialVector delta = AxisVector(edge.Axis)
                        * (edge.DirectionSign * edge.DisplayDistanceMillimetres);
                    QuickLineSpatialPoint candidate = fromStart
                        ? current + delta : current - delta;
                    if (!positions.TryGetValue(otherId,
                            out QuickLineSpatialPoint existing))
                    {
                        positions[otherId] = candidate;
                        queue.Enqueue(otherId);
                    }
                    else if (existing.DistanceTo(candidate) > 1.0
                        && reportedCycles.Add(edge.Id))
                    {
                        diagnostics.Add("线段 " + edge.Id
                            + " 存在大于 1mm 的三维闭合偏差。");
                    }
                }
            }
            return positions;
        }

        private static bool TryClassify(QuickLineSegment segment,
            double tolerance, QuickLineProjectionMode mode,
            out AxisMatch match)
        {
            match = default;
            if (segment == null) return false;
            double dx = segment.End.X - segment.Start.X;
            double dy = segment.End.Y - segment.Start.Y;
            if (Math.Abs(dx) < 1e-12 && Math.Abs(dy) < 1e-12) return false;

            double angle = NormalizeAxialAngle(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            AxisCandidate[] candidates = mode == QuickLineProjectionMode.Orthographic
                ? OrthographicCandidates : IsometricCandidates;
            AxisCandidate best = candidates[0];
            double bestDifference = AxialDifference(angle, best.AngleDegrees);
            for (int index = 1; index < candidates.Length; index++)
            {
                double difference = AxialDifference(angle,
                    candidates[index].AngleDegrees);
                if (difference < bestDifference)
                {
                    best = candidates[index];
                    bestDifference = difference;
                }
            }
            if (bestDifference > tolerance) return false;

            double radians = best.AngleDegrees * Math.PI / 180.0;
            double dot = dx * Math.Cos(radians) + dy * Math.Sin(radians);
            match = new AxisMatch(best.Axis, dot >= 0.0 ? 1 : -1, angle);
            return true;
        }

        private static double NormalizeAxialAngle(double degrees)
        {
            double result = degrees % 180.0;
            if (result < 0.0) result += 180.0;
            return result;
        }

        private static double AxialDifference(double left, double right)
        {
            double difference = Math.Abs(left - right) % 180.0;
            return Math.Min(difference, 180.0 - difference);
        }

        private static QuickLineSpatialVector AxisVector(QuickLineSpatialAxis axis)
        {
            switch (axis)
            {
                case QuickLineSpatialAxis.X:
                    return new QuickLineSpatialVector(1.0, 0.0, 0.0);
                case QuickLineSpatialAxis.Y:
                    return new QuickLineSpatialVector(0.0, 1.0, 0.0);
                default:
                    return new QuickLineSpatialVector(0.0, 0.0, 1.0);
            }
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

        private static string Key(string segmentId, QuickLineEndpoint endpoint)
            => segmentId + ":" + (endpoint == QuickLineEndpoint.Start ? "S" : "E");

        private static int NodeNumber(string nodeId)
            => int.TryParse(nodeId.Substring(1), out int result)
                ? result : int.MaxValue;

        private static void ValidateTolerance(double tolerance)
        {
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance)
                || tolerance < 0.0 || tolerance >= 30.0)
                throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        private static void ValidateProjectionMode(QuickLineProjectionMode mode)
        {
            if (mode != QuickLineProjectionMode.Auto
                && mode != QuickLineProjectionMode.Isometric
                && mode != QuickLineProjectionMode.Orthographic)
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        private static readonly AxisCandidate[] IsometricCandidates =
        {
            new AxisCandidate(QuickLineSpatialAxis.X, 30.0),
            new AxisCandidate(QuickLineSpatialAxis.Z, 90.0),
            new AxisCandidate(QuickLineSpatialAxis.Y, 150.0)
        };

        private static readonly AxisCandidate[] OrthographicCandidates =
        {
            new AxisCandidate(QuickLineSpatialAxis.X, 0.0),
            new AxisCandidate(QuickLineSpatialAxis.Z, 90.0)
        };

        private static QuickLineProjectionMode ResolveProjectionMode(
            QuickLineGraph graph, QuickLineSegment selected,
            double tolerance, QuickLineProjectionMode requested)
        {
            if (requested != QuickLineProjectionMode.Auto) return requested;

            int isometricCount = 0;
            int orthographicCount = 0;
            foreach (QuickLineSegment segment in CollectRawComponent(graph,
                selected.Id))
            {
                if (TryClassify(segment, tolerance,
                        QuickLineProjectionMode.Isometric, out _))
                    isometricCount++;
                if (TryClassify(segment, tolerance,
                        QuickLineProjectionMode.Orthographic, out _))
                    orthographicCount++;
            }
            // A vertical line belongs to both families. Prefer the family
            // explaining more of the drawing; ties retain the historical
            // isometric interpretation.
            if (orthographicCount > isometricCount)
                return QuickLineProjectionMode.Orthographic;
            if (isometricCount > 0)
                return QuickLineProjectionMode.Isometric;
            return TryClassify(selected, tolerance,
                QuickLineProjectionMode.Orthographic, out _)
                ? QuickLineProjectionMode.Orthographic
                : QuickLineProjectionMode.Isometric;
        }

        private static IReadOnlyList<QuickLineSegment> CollectRawComponent(
            QuickLineGraph graph, string selectedId)
        {
            var result = new List<QuickLineSegment>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                selectedId
            };
            var queue = new Queue<string>();
            queue.Enqueue(selectedId);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                result.Add(graph.GetSegment(id));
                foreach (QuickLineConnection connection in graph.GetConnections(id))
                    if (visited.Add(connection.ConnectedSegmentId))
                        queue.Enqueue(connection.ConnectedSegmentId);
            }
            return result;
        }

        private readonly struct AxisCandidate
        {
            public AxisCandidate(QuickLineSpatialAxis axis, double angleDegrees)
            {
                Axis = axis;
                AngleDegrees = angleDegrees;
            }

            public QuickLineSpatialAxis Axis { get; }
            public double AngleDegrees { get; }
        }

        private readonly struct AxisMatch
        {
            public AxisMatch(QuickLineSpatialAxis axis, int directionSign,
                double planAngleDegrees)
            {
                Axis = axis;
                DirectionSign = directionSign;
                PlanAngleDegrees = planAngleDegrees;
            }

            public QuickLineSpatialAxis Axis { get; }
            public int DirectionSign { get; }
            public double PlanAngleDegrees { get; }
        }

        private sealed class EndpointDisjointSet
        {
            private readonly Dictionary<string, string> _parent;

            public EndpointDisjointSet(IEnumerable<QuickLineSegment> segments)
            {
                _parent = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (QuickLineSegment segment in segments)
                {
                    string start = Key(segment.Id, QuickLineEndpoint.Start);
                    string end = Key(segment.Id, QuickLineEndpoint.End);
                    _parent[start] = start;
                    _parent[end] = end;
                }
            }

            public void Union(string left, string right)
            {
                string leftRoot = Find(left);
                string rightRoot = Find(right);
                if (!string.Equals(leftRoot, rightRoot,
                        StringComparison.OrdinalIgnoreCase))
                    _parent[rightRoot] = leftRoot;
            }

            public Dictionary<string, string> BuildNodeIds()
            {
                var rootIds = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                int next = 1;
                var result = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (string endpoint in _parent.Keys.OrderBy(item => item,
                    StringComparer.OrdinalIgnoreCase))
                {
                    string root = Find(endpoint);
                    if (!rootIds.TryGetValue(root, out string nodeId))
                    {
                        nodeId = "N" + next++;
                        rootIds[root] = nodeId;
                    }
                    result[endpoint] = nodeId;
                }
                return result;
            }

            private string Find(string item)
            {
                if (!_parent.TryGetValue(item, out string parent))
                    throw new ArgumentException("Unknown endpoint: " + item);
                if (string.Equals(parent, item, StringComparison.OrdinalIgnoreCase))
                    return item;
                string root = Find(parent);
                _parent[item] = root;
                return root;
            }
        }
    }
}
