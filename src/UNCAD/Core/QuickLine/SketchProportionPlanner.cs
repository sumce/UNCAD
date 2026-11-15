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
            bool placeholderLabel, double labelWidthUnits = 0.0)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A segment id is required.", "id");
            Id = id;
            Start = start;
            End = end;
            LabelMillimetres = labelMillimetres;
            Completed = completed;
            PlaceholderLabel = placeholderLabel;
            LabelWidthUnits = labelWidthUnits;
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
        /// <summary>
        /// Estimated final drawn width of this segment's mm label text (drawing
        /// units). 3.2.0.1（D-053 修订）：短段下限除 1.5×洞径外还必须放得下
        /// 自己的毫米标——现场报告"短段比标注文字还短"。
        /// </summary>
        public double LabelWidthUnits { get; }
        public double Length => Start.DistanceTo(End);
    }

    /// <summary>
    /// 3.2.0.1（D-053 修订）：重排目标空间——图框的可用内部矩形。给了 target，
    /// 整个草图（全部连通分量共用一个比例系数）按毫米比例铺开到用满该空间并
    /// 联合居中；没给 target 保持旧行为（包络不超过原图范围、原位锚定）。
    /// </summary>
    public sealed class SketchProportionTarget
    {
        public SketchProportionTarget(double centreX, double centreY,
            double width, double height)
        {
            if (double.IsNaN(centreX) || double.IsInfinity(centreX)
                || double.IsNaN(centreY) || double.IsInfinity(centreY))
                throw new ArgumentException("Target centre must be finite.");
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0
                || double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
                throw new ArgumentException("Target size must be positive.");
            CentreX = centreX;
            CentreY = centreY;
            Width = width;
            Height = height;
        }

        public double CentreX { get; }
        public double CentreY { get; }
        public double Width { get; }
        public double Height { get; }
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
    ///   read off a uniform scale, then re-embedded with clamped distances
    ///   eff = max(display, floor/k).  The per-segment floor is
    ///   max(minFinalUnits, LabelWidthUnits×1.15)：1.5×洞径保证洞放得下，
    ///   标注宽度保证毫米标放得下（3.2.0.1——旧版只钳洞径，现场出现
    ///   "短段比自己的毫米标还短"）；
    /// - target == null（旧行为）：k = min(current/new width, current/new
    ///   height) keeps the component's 2D bounding box inside its current
    ///   envelope; the box centre is anchored so the sketch stays put;
    /// - target != null（D-053 修订，3.2.0.1）：全部分量共用一个 k，二分求
    ///   最大的 k 使"各分量按当前中心 ± k×投影半幅"的联合包络落在目标
    ///   矩形内（草图整体铺开用满图框可用空间，例如 75000×7500 的图框），
    ///   再统一平移使联合包络居中于目标中心——分量之间的相对位置不变；
    /// - a component containing any unlabelled or unclassifiable line is
    ///   skipped and named, never partially moved (a half-moved sketch would
    ///   tear endpoint connections).
    /// </summary>
    public static class SketchProportionPlanner
    {
        private const double BboxEpsilon = 1e-6;
        private const double LabelWidthPaddingFactor = 1.15;

        public static SketchProportionPlan Plan(
            IReadOnlyList<SketchProportionInput> segments, double minFinalUnits,
            double endpointTolerance = 1.0, SketchProportionTarget target = null)
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

            var prepared = new List<PreparedComponent>();
            foreach (List<QuickLineSegment> component in CollectComponents(graph))
            {
                PreparedComponent item = TryPrepareComponent(graph, component,
                    inputsById, diagnostics);
                if (item != null) prepared.Add(item);
            }

            if (target == null)
            {
                foreach (PreparedComponent item in prepared)
                    ApplyComponent(graph, item, minFinalUnits,
                        ResolveLegacyScale(item), new QuickLinePoint(0, 0),
                        moves, depths, diagnostics);
                return new SketchProportionPlan(moves, depths, diagnostics);
            }

            double scale = ResolveTargetScale(prepared, target);
            if (!IsUsableScale(scale))
            {
                diagnostics.Add("目标空间内放不下重排草图（k="
                    + scale.ToString("0.###") + "），整帧未重排。");
                return new SketchProportionPlan(moves, depths, diagnostics);
            }
            // 先按各分量当前中心锚定产出坐标，再统一平移到目标中心居中。
            var anchored = new List<SketchSegmentMove>();
            foreach (PreparedComponent item in prepared)
                ApplyComponent(graph, item, minFinalUnits, scale,
                    new QuickLinePoint(0, 0), anchored, depths, diagnostics);
            if (anchored.Count > 0)
            {
                QuickLinePoint unionCentre = MovesUnionCentre(anchored);
                var translation = new QuickLinePoint(
                    target.CentreX - unionCentre.X,
                    target.CentreY - unionCentre.Y);
                foreach (SketchSegmentMove move in anchored)
                    moves.Add(new SketchSegmentMove(move.Id,
                        Translate(move.NewStart, translation),
                        Translate(move.NewEnd, translation)));
                ReportTargetOverflow(anchored, target, diagnostics);
            }
            return new SketchProportionPlan(moves, depths, diagnostics);
        }

        private sealed class PreparedComponent
        {
            public List<QuickLineSegment> Component;
            public Dictionary<string, double> Distances;
            public Dictionary<string, double> Display;
            public Dictionary<string, double> LabelWidths;
            public HashSet<string> Completed;
            public string RootId;
            public QuickLineIsometricScene FirstPass;
            /// <summary>Current drawn bbox centre — per-component anchor.</summary>
            public QuickLinePoint CurrentCentre;
            /// <summary>Unit-scale projected half extents of the first pass.</summary>
            public double HalfWidth;
            public double HalfHeight;
        }

        private static PreparedComponent TryPrepareComponent(QuickLineGraph graph,
            List<QuickLineSegment> component,
            IDictionary<string, SketchProportionInput> inputsById,
            ICollection<string> diagnostics)
        {
            // 分量里只要有一条未标注线就整分量跳过，绝不部分移动：
            // 否则该线端点与已移动邻线脱开，草图被撕烂。
            foreach (QuickLineSegment segment in component)
            {
                if (inputsById[segment.Id].LabelMillimetres.HasValue) continue;
                diagnostics.Add("分量包含未标注毫米的线段 " + segment.Id
                    + "，整分量未重排。");
                return null;
            }

            var distances = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            var display = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            var labelWidths = new Dictionary<string, double>(
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
                labelWidths[segment.Id] = input.LabelWidthUnits;
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
                return null;
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
                return null;
            }

            double currentMinX, currentMaxX, currentMinY, currentMaxY;
            ComponentBounds(component, out currentMinX, out currentMaxX,
                out currentMinY, out currentMaxY);
            double projMinX = double.MaxValue, projMaxX = double.MinValue;
            double projMinY = double.MaxValue, projMaxY = double.MinValue;
            foreach (QuickLineIsometricNode node in firstPass.Nodes)
            {
                QuickLinePoint point = IsometricProjection.Project(node.Position,
                    firstPass.ProjectionMode);
                projMinX = Math.Min(projMinX, point.X);
                projMaxX = Math.Max(projMaxX, point.X);
                projMinY = Math.Min(projMinY, point.Y);
                projMaxY = Math.Max(projMaxY, point.Y);
            }
            return new PreparedComponent
            {
                Component = component,
                Distances = distances,
                Display = display,
                LabelWidths = labelWidths,
                Completed = completed,
                RootId = rootId,
                FirstPass = firstPass,
                CurrentCentre = new QuickLinePoint(
                    (currentMinX + currentMaxX) / 2.0,
                    (currentMinY + currentMaxY) / 2.0),
                HalfWidth = (projMaxX - projMinX) / 2.0,
                HalfHeight = (projMaxY - projMinY) / 2.0
            };
        }

        private static void ApplyComponent(QuickLineGraph graph,
            PreparedComponent prepared, double minFinalUnits, double scale,
            QuickLinePoint translation, ICollection<SketchSegmentMove> moves,
            IDictionary<string, SketchSegmentDepth> depths,
            ICollection<string> diagnostics)
        {
            if (!IsUsableScale(scale))
            {
                diagnostics.Add("分量包络退化（k=" + scale.ToString("0.###")
                    + "），整分量未重排。");
                return;
            }

            // 第二遍：短段钳到最小可读长度——洞（1.5×洞径）与毫米标文字
            // （LabelWidthUnits×1.15，3.2.0.1）都放得下为止。
            var clamped = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineSegment segment in prepared.Component)
            {
                double labelWidth = prepared.LabelWidths[segment.Id];
                double floor = Math.Max(minFinalUnits,
                    labelWidth > 0 ? labelWidth * LabelWidthPaddingFactor : 0);
                double minEff = floor / scale;
                clamped[segment.Id] = Math.Max(prepared.Display[segment.Id], minEff);
            }
            QuickLineIsometricScene secondPass = QuickLineIsometricSceneBuilder
                .Build(graph, prepared.RootId, prepared.Distances, clamped,
                    prepared.Completed);
            foreach (string diagnostic in secondPass.Diagnostics)
                diagnostics.Add(diagnostic);

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            var projected = new Dictionary<string, QuickLinePoint>(
                StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineIsometricNode node in secondPass.Nodes)
            {
                QuickLinePoint point = IsometricProjection.Project(node.Position,
                    secondPass.ProjectionMode);
                point = new QuickLinePoint(point.X * scale, point.Y * scale);
                projected[node.Id] = point;
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }
            // 分量锚定在各自当前中心（目标模式下分量间相对位置由联合平移保持）。
            var anchor = new QuickLinePoint(
                prepared.CurrentCentre.X - (minX + maxX) / 2.0,
                prepared.CurrentCentre.Y - (minY + maxY) / 2.0);

            foreach (QuickLineIsometricSegment segment in secondPass.Segments)
            {
                moves.Add(new SketchSegmentMove(segment.Id,
                    Translate(Translate(projected[segment.StartNodeId], anchor),
                        translation),
                    Translate(Translate(projected[segment.EndNodeId], anchor),
                        translation)));

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

        private static QuickLinePoint Translate(QuickLinePoint point,
            QuickLinePoint offset)
            => new QuickLinePoint(point.X + offset.X, point.Y + offset.Y);

        /// <summary>
        /// 旧行为（无目标空间）：k = min(旧宽/新宽, 旧高/新高)，包络不超过原范围。
        /// </summary>
        private static double ResolveLegacyScale(PreparedComponent prepared)
        {
            double newWidth = prepared.HalfWidth * 2.0;
            double newHeight = prepared.HalfHeight * 2.0;
            ComponentEnvelope(prepared.Component,
                out double currentWidth, out double currentHeight);
            if (newWidth < BboxEpsilon && newHeight < BboxEpsilon) return 0.0;
            if (newWidth < BboxEpsilon) return currentHeight / newHeight;
            if (newHeight < BboxEpsilon) return currentWidth / newWidth;
            return Math.Min(currentWidth / newWidth,
                currentHeight / newHeight);
        }

        /// <summary>
        /// 目标空间模式：二分求最大 k，使各分量「当前中心 ± k×投影半幅」的
        /// 联合包络仍落在目标矩形内（包络宽高对 k 单调不减，二分收敛即最优）。
        /// </summary>
        private static double ResolveTargetScale(
            IReadOnlyList<PreparedComponent> prepared, SketchProportionTarget target)
        {
            bool Fits(double k)
            {
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                foreach (PreparedComponent item in prepared)
                {
                    minX = Math.Min(minX, item.CurrentCentre.X - k * item.HalfWidth);
                    maxX = Math.Max(maxX, item.CurrentCentre.X + k * item.HalfWidth);
                    minY = Math.Min(minY, item.CurrentCentre.Y - k * item.HalfHeight);
                    maxY = Math.Max(maxY, item.CurrentCentre.Y + k * item.HalfHeight);
                }
                return maxX - minX <= target.Width + BboxEpsilon
                    && maxY - minY <= target.Height + BboxEpsilon;
            }

            if (!Fits(0)) return 0.0;
            double hi = 1.0;
            while (Fits(hi) && hi < 1e9) hi *= 2.0;
            double lo = 0.0;
            for (int index = 0; index < 60; index++)
            {
                double mid = (lo + hi) / 2.0;
                if (Fits(mid)) lo = mid;
                else hi = mid;
            }
            return lo;
        }

        private static QuickLinePoint MovesUnionCentre(
            IReadOnlyList<SketchSegmentMove> moves)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (SketchSegmentMove move in moves)
            {
                foreach (QuickLinePoint point in new[] { move.NewStart, move.NewEnd })
                {
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);
                }
            }
            return new QuickLinePoint((minX + maxX) / 2.0, (minY + maxY) / 2.0);
        }

        private static void ReportTargetOverflow(
            IReadOnlyList<SketchSegmentMove> moves, SketchProportionTarget target,
            ICollection<string> diagnostics)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (SketchSegmentMove move in moves)
            {
                foreach (QuickLinePoint point in new[] { move.NewStart, move.NewEnd })
                {
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);
                }
            }
            double width = maxX - minX, height = maxY - minY;
            if (width > target.Width * 1.02 || height > target.Height * 1.02)
                diagnostics.Add("钳位下限把包络撑到 "
                    + width.ToString("0") + "×" + height.ToString("0")
                    + "，略超图框可用空间（" + target.Width.ToString("0") + "×"
                    + target.Height.ToString("0") + "）；短段较多属正常，可按需微调。");
        }

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
