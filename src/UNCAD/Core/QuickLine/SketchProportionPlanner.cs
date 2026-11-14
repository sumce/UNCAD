using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>One sketch line entering proportion planning (CAD-independent).</summary>
    public sealed class SketchProportionInput
    {
        public SketchProportionInput(string id, QuickLinePoint start,
            QuickLinePoint end, double? labelMillimetres, bool completed,
            bool placeholderLabel)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A segment id is required.", "id");
            Id = id;
            Start = start;
            End = end;
            LabelMillimetres = labelMillimetres;
            Completed = completed;
            PlaceholderLabel = placeholderLabel;
        }

        public string Id { get; }
        public QuickLinePoint Start { get; }
        public QuickLinePoint End { get; }
        /// <summary>Parsed mm label value; null when the line has no label.</summary>
        public double? LabelMillimetres { get; }
        /// <summary>U1LX completion marker for the label.</summary>
        public bool Completed { get; }
        /// <summary>True for unconfirmed trailing-00mm placeholders.</summary>
        public bool PlaceholderLabel { get; }
        public double Length => Start.DistanceTo(End);
    }

    /// <summary>New absolute endpoints computed for one line.</summary>
    public sealed class SketchSegmentMove
    {
        internal SketchSegmentMove(string id, QuickLinePoint newStart,
            QuickLinePoint newEnd)
        {
            Id = id;
            NewStart = newStart;
            NewEnd = newEnd;
        }

        public string Id { get; }
        public QuickLinePoint NewStart { get; }
        public QuickLinePoint NewEnd { get; }
    }

    /// <summary>Camera closeness of a relaid segment, used for hop decisions.</summary>
    public sealed class SketchSegmentDepth
    {
        internal SketchSegmentDepth(double closeness,
            QuickLineProjectionMode mode)
        {
            Closeness = closeness;
            Mode = mode;
        }

        /// <summary>z−x−y in mm (isometric) or y (orthographic); larger is nearer.</summary>
        public double Closeness { get; }
        public QuickLineProjectionMode Mode { get; }
    }

    /// <summary>Result of proportion planning for one drawing scope.</summary>
    public sealed class SketchProportionPlan
    {
        internal SketchProportionPlan(IReadOnlyList<SketchSegmentMove> moves,
            IReadOnlyDictionary<string, SketchSegmentDepth> depths,
            IReadOnlyList<string> diagnostics)
        {
            Moves = moves;
            Depths = depths;
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<SketchSegmentMove> Moves { get; }
        public IReadOnlyDictionary<string, SketchSegmentDepth> Depths { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        public int RelaidSegmentCount => Moves.Count;
    }

    /// <summary>
    /// Relays a classified isometric/orthographic sketch so every segment's
    /// drawn length is proportional to its real millimetre label, mirroring
    /// the reconstruction QuickLineIsometricSceneBuilder performs for the 3D
    /// preview.  Placeholder labels have no real value and keep their current
    /// drawn length as the display distance, exactly like the preview input.
    ///
    /// Layout rules:
    /// - each connected component is embedded once with display distances to
    ///   read off a uniform scale k, then re-embedded with clamped distances
    ///   eff = max(display, minFinalUnits / k) so short-but-real segments stay
    ///   readable (the hop diameter and mm label still fit);
    /// - k = min(current/new width, current/new height) keeps the component's
    ///   2D bounding box inside its current envelope; the box centre is
    ///   anchored so the sketch stays where the user drew it;
    /// - a component containing any unlabelled or unclassifiable line is
    ///   skipped and named, never partially moved (a half-moved sketch would
    ///   tear endpoint connections).
    /// </summary>
    public static class SketchProportionPlanner
    {
        private const double BboxEpsilon = 1e-6;

        public static SketchProportionPlan Plan(
            IReadOnlyList<SketchProportionInput> segments, double minFinalUnits,
            double endpointTolerance = 1.0)
        {
            if (segments == null)
                throw new ArgumentNullException("segments");
            if (double.IsNaN(minFinalUnits) || double.IsInfinity(minFinalUnits)
                || minFinalUnits <= 0)
                throw new ArgumentOutOfRangeException("minFinalUnits");

            var inputs = new List<SketchProportionInput>();
            var inputsById = new Dictionary<string, SketchProportionInput>(
                StringComparer.OrdinalIgnoreCase);
            foreach (SketchProportionInput segment in segments)
            {
                if (segment == null) continue;
                inputs.Add(segment);
                inputsById[segment.Id] = segment;
            }
            var diagnostics = new List<string>();
            var moves = new List<SketchSegmentMove>();
            var depths = new Dictionary<string, SketchSegmentDepth>(
                StringComparer.OrdinalIgnoreCase);
            if (inputs.Count == 0)
                return new SketchProportionPlan(moves, depths, diagnostics);

            QuickLineGraph graph = QuickLineGraph.Build(inputs.Select(
                item => new QuickLineSegment(item.Id, item.Start, item.End)),
                endpointTolerance);
            foreach (List<QuickLineSegment> component in CollectComponents(graph))
                PlanComponent(graph, component, inputsById, minFinalUnits,
                    moves, depths, diagnostics);
            return new SketchProportionPlan(moves, depths, diagnostics);
        }

        private static void PlanComponent(QuickLineGraph graph,
            List<QuickLineSegment> component,
            IDictionary<string, SketchProportionInput> inputsById,
            double minFinalUnits, ICollection<SketchSegmentMove> moves,
            IDictionary<string, SketchSegmentDepth> depths,
            ICollection<string> diagnostics)
        {
            // 分量里只要有一条未标注线就整分量跳过，绝不部分移动：
            // 否则该线端点与已移动邻线脱开，草图被撕烂。
            foreach (QuickLineSegment segment in component)
            {
                if (inputsById[segment.Id].LabelMillimetres.HasValue) continue;
                diagnostics.Add("分量包含未标注毫米的线段 " + segment.Id
                    + "，整分量未重排。");
                return;
            }

            var distances = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            var display = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineSegment segment in component)
            {
                SketchProportionInput input = inputsById[segment.Id];
                // 占位标注没有真实值：沿用当前图上长度参与整体缩放（与 3D
                // 预览的 displayDistances 口径一致）。
                double real = input.LabelMillimetres.Value;
                distances[segment.Id] = real;
                display[segment.Id] = input.PlaceholderLabel && !input.Completed
                    ? input.Length : real;
                if (input.Completed) completed.Add(segment.Id);
            }

            string rootId = component[0].Id;
            QuickLineIsometricScene firstPass;
            try
            {
                firstPass = QuickLineIsometricSceneBuilder.Build(graph, rootId,
                    distances, display, completed);
            }
            catch (ArgumentException ex)
            {
                diagnostics.Add("分量方向无法归入等测/正交投影，整分量未重排（"
                    + ex.Message + "）。");
                return;
            }
            if (firstPass.Segments.Count != component.Count)
            {
                var classifiedIds = new HashSet<string>(
                    firstPass.Segments.Select(item => item.Id),
                    StringComparer.OrdinalIgnoreCase);
                var missing = component.Where(item => !classifiedIds.Contains(item.Id))
                    .Select(item => item.Id).ToList();
                diagnostics.Add("分量包含不可分类线段 "
                    + string.Join("、", missing) + "，整分量未重排。");
                return;
            }

            double scale = ResolveScale(component, firstPass);
            if (!IsUsableScale(scale))
            {
                diagnostics.Add("分量包络退化（k=" + scale.ToString("0.###")
                    + "），整分量未重排。");
                return;
            }

            // 第二遍：极短段钳到最小可读长度（最终图上长度 ≥ minFinalUnits）。
            double minEff = minFinalUnits / scale;
            var clamped = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, double> pair in display)
                clamped[pair.Key] = Math.Max(pair.Value, minEff);
            QuickLineIsometricScene secondPass = QuickLineIsometricSceneBuilder
                .Build(graph, rootId, distances, clamped, completed);
            foreach (string diagnostic in secondPass.Diagnostics)
                diagnostics.Add(diagnostic);

            Dictionary<string, QuickLinePoint> projected = ProjectNodes(
                secondPass, scale, component, out QuickLinePoint anchor);
            foreach (QuickLineIsometricSegment segment in secondPass.Segments)
            {
                moves.Add(new SketchSegmentMove(segment.Id,
                    Anchor(projected[segment.StartNodeId], anchor),
                    Anchor(projected[segment.EndNodeId], anchor)));

                QuickLineSpatialPoint start = NodePosition(secondPass,
                    segment.StartNodeId);
                QuickLineSpatialPoint end = NodePosition(secondPass,
                    segment.EndNodeId);
                var midpoint = new QuickLineSpatialPoint(
                    (start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0,
                    (start.Z + end.Z) / 2.0);
                depths[segment.Id] = new SketchSegmentDepth(
                    IsometricProjection.ClosenessToCamera(midpoint,
                        secondPass.ProjectionMode), secondPass.ProjectionMode);
            }
        }

        private static double ResolveScale(
            IReadOnlyList<QuickLineSegment> component,
            QuickLineIsometricScene scene)
        {
            double newMinX = double.MaxValue, newMaxX = double.MinValue;
            double newMinY = double.MaxValue, newMaxY = double.MinValue;
            foreach (QuickLineIsometricNode node in scene.Nodes)
            {
                QuickLinePoint point = IsometricProjection.Project(node.Position,
                    scene.ProjectionMode);
                newMinX = Math.Min(newMinX, point.X);
                newMaxX = Math.Max(newMaxX, point.X);
                newMinY = Math.Min(newMinY, point.Y);
                newMaxY = Math.Max(newMaxY, point.Y);
            }
            double currentWidth, currentHeight;
            ComponentEnvelope(component, out currentWidth, out currentHeight);

            double newWidth = newMaxX - newMinX;
            double newHeight = newMaxY - newMinY;
            if (newWidth < BboxEpsilon && newHeight < BboxEpsilon) return 0.0;

            // 退化方向（纯直线分量）用另一维的比例；包络保持在原大小以内。
            if (newWidth < BboxEpsilon) return currentHeight / newHeight;
            if (newHeight < BboxEpsilon) return currentWidth / newWidth;
            return Math.Min(currentWidth / newWidth,
                currentHeight / newHeight);
        }

        private static Dictionary<string, QuickLinePoint> ProjectNodes(
            QuickLineIsometricScene scene, double scale,
            IReadOnlyList<QuickLineSegment> component, out QuickLinePoint anchor)
        {
            var projected = new Dictionary<string, QuickLinePoint>(
                StringComparer.OrdinalIgnoreCase);
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (QuickLineIsometricNode node in scene.Nodes)
            {
                QuickLinePoint point = IsometricProjection.Project(node.Position,
                    scene.ProjectionMode);
                point = new QuickLinePoint(point.X * scale, point.Y * scale);
                projected[node.Id] = point;
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }

            // 包络中心锚定：重排后草图仍停留在用户画它的位置。
            double currentMinX, currentMaxX, currentMinY, currentMaxY;
            ComponentBounds(component, out currentMinX, out currentMaxX,
                out currentMinY, out currentMaxY);
            anchor = new QuickLinePoint(
                (currentMinX + currentMaxX) / 2.0 - (minX + maxX) / 2.0,
                (currentMinY + currentMaxY) / 2.0 - (minY + maxY) / 2.0);
            return projected;
        }

        private static QuickLinePoint Anchor(QuickLinePoint point,
            QuickLinePoint anchor)
            => new QuickLinePoint(point.X + anchor.X, point.Y + anchor.Y);

        private static QuickLineSpatialPoint NodePosition(
            QuickLineIsometricScene scene, string nodeId)
        {
            foreach (QuickLineIsometricNode node in scene.Nodes)
                if (string.Equals(node.Id, nodeId,
                        StringComparison.OrdinalIgnoreCase))
                    return node.Position;
            throw new KeyNotFoundException("Unknown scene node: " + nodeId);
        }

        private static List<List<QuickLineSegment>> CollectComponents(
            QuickLineGraph graph)
        {
            var result = new List<List<QuickLineSegment>>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineSegment segment in graph.Segments)
            {
                if (visited.Contains(segment.Id)) continue;
                var component = new List<QuickLineSegment>();
                var queue = new Queue<string>();
                visited.Add(segment.Id);
                queue.Enqueue(segment.Id);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    component.Add(graph.GetSegment(current));
                    foreach (QuickLineConnection connection in graph
                        .GetConnections(current))
                        if (visited.Add(connection.ConnectedSegmentId))
                            queue.Enqueue(connection.ConnectedSegmentId);
                }
                result.Add(component);
            }
            return result;
        }

        private static void ComponentEnvelope(
            IReadOnlyList<QuickLineSegment> component,
            out double width, out double height)
        {
            double minX, maxX, minY, maxY;
            ComponentBounds(component, out minX, out maxX, out minY, out maxY);
            width = maxX - minX;
            height = maxY - minY;
        }

        private static void ComponentBounds(
            IReadOnlyList<QuickLineSegment> component,
            out double minX, out double maxX, out double minY, out double maxY)
        {
            minX = double.MaxValue; maxX = double.MinValue;
            minY = double.MaxValue; maxY = double.MinValue;
            foreach (QuickLineSegment segment in component)
            {
                minX = Math.Min(minX, Math.Min(segment.Start.X, segment.End.X));
                maxX = Math.Max(maxX, Math.Max(segment.Start.X, segment.End.X));
                minY = Math.Min(minY, Math.Min(segment.Start.Y, segment.End.Y));
                maxY = Math.Max(maxY, Math.Max(segment.Start.Y, segment.End.Y));
            }
        }

        private static bool IsUsableScale(double scale)
            => !double.IsNaN(scale) && !double.IsInfinity(scale) && scale > 0;
    }
}
