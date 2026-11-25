using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// 3.3.5（D-054 Amendment）：U1U 确认窗 3D 预览的**渲染信封**——把
    /// <see cref="QuickLineOrbitPreviewModel"/> 连同柱网规划、通道双线偏移、
    /// 标签/刻度锚点一次性解析成"哑渲染器友好"的纯几何数据（坐标全部算好，
    /// 消费端不重建场景、不做几何推导）。渲染内核（WebView2+three.js）只负责
    /// 把这些点线面画出来——分层铁律不变：场景语义在 Core/Features 定死。
    ///
    /// 几何口径与 3.3.2 Helix 控件逐条对齐（那套公式已被数据级测试锁定过）：
    /// 桥架双线偏移=宽/2 且不低于对角线 0.0012；线管双线=对角线 0.0006；
    /// 米数标世界高度=对角线 0.016、刻度/字母 0.013、端点钉长 0.030；标签锚
    /// 在段中点上方 0.6×标签高；数字刻度贴柱网与包络边沿交点、字母贴对边；
    /// 端点钉=竖直引线+原文标签。柱网规划沿用 QuickLineColumnGridPlanner
    ///（3.3.2 两向正交：数字轴向由端点平面位移决定）。
    /// </summary>
    public sealed class QuickLineOrbitPreviewEnvelope
    {
        // 与 3.3.2 Helix 控件相同的显示比例（对角线占比）。
        private const double MinimumTrayOffsetDiagonalRatio = 0.0012;
        private const double ConduitOffsetDiagonalRatio = 0.0006;
        private const double LabelHeightDiagonalRatio = 0.016;
        private const double TickHeightDiagonalRatio = 0.013;
        private const double EndpointPinLengthDiagonalRatio = 0.030;

        private QuickLineOrbitPreviewEnvelope(
            IReadOnlyList<EnvelopeSegment> segments,
            IReadOnlyList<EnvelopePoint> columnGridLines,
            IReadOnlyList<EnvelopeLabel> numericTicks,
            IReadOnlyList<EnvelopeLabel> letterLabels,
            IReadOnlyList<EnvelopeLabel> segmentLabels,
            IReadOnlyList<EnvelopePin> endpointPins,
            double diagonal,
            string axisSummary, string upstreamAxisLabel,
            string downstreamAxisLabel,
            bool anyTrayWidthAssumedDefault,
            IReadOnlyList<EnvelopeReferenceLevel> referenceLevels,
            double baseFloorZ,
            EnvelopePoint upstreamEndpoint,
            EnvelopePoint downstreamEndpoint,
            EnvelopePoint gridAnchor,
            bool directionResolved,
            IReadOnlyList<string> directionDiagnostics,
            EnvelopeHose hose,
            string recognitionSummary,
            IReadOnlyList<string> routeIssues,
            IReadOnlyList<EnvelopeFloor> floors,
            IReadOnlyList<EnvelopeLabel> axisEdgeLabels,
            IReadOnlyList<EnvelopeLabel> runLabels,
            EnvelopeCabinet cabinet)
        {
            Segments = segments;
            ColumnGridLines = columnGridLines;
            NumericTicks = numericTicks;
            LetterLabels = letterLabels;
            SegmentLabels = segmentLabels;
            EndpointPins = endpointPins;
            Diagonal = diagonal;
            AxisSummary = axisSummary;
            UpstreamAxisLabel = upstreamAxisLabel;
            DownstreamAxisLabel = downstreamAxisLabel;
            AnyTrayWidthAssumedDefault = anyTrayWidthAssumedDefault;
            ReferenceLevels = referenceLevels ?? Array.Empty<EnvelopeReferenceLevel>();
            BaseFloorZ = baseFloorZ;
            UpstreamEndpoint = upstreamEndpoint;
            DownstreamEndpoint = downstreamEndpoint;
            GridAnchor = gridAnchor;
            DirectionResolved = directionResolved;
            DirectionDiagnostics = directionDiagnostics
                ?? Array.Empty<string>();
            Hose = hose;
            RecognitionSummary = recognitionSummary ?? string.Empty;
            RouteIssues = routeIssues ?? Array.Empty<string>();
            Floors = floors ?? Array.Empty<EnvelopeFloor>();
            AxisEdgeLabels = axisEdgeLabels ?? Array.Empty<EnvelopeLabel>();
            RunLabels = runLabels ?? Array.Empty<EnvelopeLabel>();
            Cabinet = cabinet;
        }

        /// <summary>
        /// D-059：楼层平面（台账上下游楼层之间的各层，从低到高）。每块是一个矩形
        /// 平面 + 该层地面上的柱网线。
        /// </summary>
        public IReadOnlyList<EnvelopeFloor> Floors { get; }

        /// <summary>
        /// D-059：图签式轴位边标——数字轴号、字母轴号分别贴在最高那层平面的
        /// 两条相邻外边上（不再在每个柱位上挂完整组合）。
        /// </summary>
        public IReadOnlyList<EnvelopeLabel> AxisEdgeLabels { get; }

        /// <summary>D-059：桥架/线管整趟标签（一趟一个：规格 + 整趟长度）。</summary>
        public IReadOnlyList<EnvelopeLabel> RunLabels { get; }

        /// <summary>D-059：上游柜（I-Line 盘 / 插座盘 / 母线插接口）；不画时为 null。</summary>
        public EnvelopeCabinet Cabinet { get; }

        /// <summary>
        /// D-058：软管的示意几何（从设备端沿末段方向伸出，长度＝软管毫米）。
        /// 没有软管、或设备端未确认（放不出来）时为 null。
        /// </summary>
        public EnvelopeHose Hose { get; }

        /// <summary>D-058：线路识别结果一句话（由预览模型原样带入）。</summary>
        public string RecognitionSummary { get; }

        /// <summary>D-058：识别出的问题，逐条成句（由预览模型原样带入）。</summary>
        public IReadOnlyList<string> RouteIssues { get; }

        /// <summary>一条电缆段：中心线端点 + 轴向 + 通道双线（null=普通电缆段）。</summary>
        public IReadOnlyList<EnvelopeSegment> Segments { get; }

        /// <summary>地面柱网线段端点对（扁平；空=无柱网）。</summary>
        public IReadOnlyList<EnvelopePoint> ColumnGridLines { get; }

        /// <summary>数字轴刻度标签。</summary>
        public IReadOnlyList<EnvelopeLabel> NumericTicks { get; }

        /// <summary>字母轴标签。</summary>
        public IReadOnlyList<EnvelopeLabel> LetterLabels { get; }

        /// <summary>段中点毫米标标签（3.3.1 特性B）。</summary>
        public IReadOnlyList<EnvelopeLabel> SegmentLabels { get; }

        /// <summary>上下游端点轴位钉（竖直引线+原文标签，3.3.1 特性C）。</summary>
        public IReadOnlyList<EnvelopePin> EndpointPins { get; }

        /// <summary>场景对角线（mm）——相机距离与线宽的世界换算基准。</summary>
        public double Diagonal { get; }

        public string AxisSummary { get; }
        public string UpstreamAxisLabel { get; }
        public string DownstreamAxisLabel { get; }

        /// <summary>任一桥架宽度用了缺省 200（图例要注明）。</summary>
        public bool AnyTrayWidthAssumedDefault { get; }

        /// <summary>3.3.6：参照层（建筑标高/设备高度的水平参照线，U1SET
        /// UNC_PREVIEW_REF_LEVELS 配置；空=不画）。</summary>
        public IReadOnlyList<EnvelopeReferenceLevel> ReferenceLevels { get; }

        /// <summary>
        /// 3.3.7 三轮：本层基准（「本层=2F地面」指令解析出的绝对标高；0=未
        /// 指定）。页面用它把整栋楼重定位——本层地面=场景 0（上游就在本层，
        /// 设备/母线在本层地面上起算）。
        /// </summary>
        public double BaseFloorZ { get; }

        /// <summary>上游路线端点（柜体定位用；没有柱网规划时为空）。</summary>
        public EnvelopePoint UpstreamEndpoint { get; }

        /// <summary>下游路线端点（确定柜体排布朝向用；没有柱网规划时为空）。</summary>
        public EnvelopePoint DownstreamEndpoint { get; }

        /// <summary>柱网锚点；方向未确认时不代表上游。</summary>
        public EnvelopePoint GridAnchor { get; }

        public bool DirectionResolved { get; }
        public IReadOnlyList<string> DirectionDiagnostics { get; }

        /// <summary>由预览模型构建信封（model 为 null/空段时返回 null）。
        /// referenceLevels：参照层清单（U1SET 配置解析而来；null=无参照层）。</summary>
        public static QuickLineOrbitPreviewEnvelope Build(
            QuickLineOrbitPreviewModel model,
            IReadOnlyList<PreviewReferenceLevel> referenceLevels = null,
            string baseFloorLabel = null)
        {
            if (model == null || model.Segments.Count == 0) return null;

            Bounds bounds = Bounds.Of(model.Segments);
            double diagonal = Math.Max(bounds.Diagonal, 1.0);

            var segments = new List<EnvelopeSegment>(model.Segments.Count);
            var segmentLabels = new List<EnvelopeLabel>();
            bool anyAssumed = false;
            double labelHeight = diagonal * LabelHeightDiagonalRatio;
            foreach (QuickLineOrbitPreviewSegment segment in model.Segments)
            {
                EnvelopeChannel channel = null;
                QuickLineOrbitPreviewChannel info = segment.Channel;
                if (info != null)
                {
                    if (info.TrayWidthMillimetres.HasValue)
                    {
                        double offset = Math.Max(
                            info.TrayWidthMillimetres.Value / 2.0,
                            diagonal * MinimumTrayOffsetDiagonalRatio);
                        channel = BuildChannelRails(segment, offset, "tray");
                        if (info.TrayWidthAssumedDefault) anyAssumed = true;
                    }
                    else if (info.HasConduit)
                    {
                        channel = BuildChannelRails(segment,
                            diagonal * ConduitOffsetDiagonalRatio, "conduit");
                    }
                }
                segments.Add(new EnvelopeSegment(
                    segment.Id,
                    EnvelopePoint.Of(segment.Start),
                    EnvelopePoint.Of(segment.End),
                    AxisName(segment.Axis),
                    segment.DistanceMillimetres,
                    segment.Completed,
                    channel));
                if (segment.LabelText.Length > 0)
                {
                    // 3.8.0.2：米数贴在线上——锚点就是段中点，带上段方向，由叠加层
                    // 沿线排字、放在线的上侧（不再抬到管子上方参与防重叠）。
                    segmentLabels.Add(new EnvelopeLabel(
                        segment.LabelText,
                        (segment.Start.X + segment.End.X) / 2.0,
                        (segment.Start.Y + segment.End.Y) / 2.0,
                        (segment.Start.Z + segment.End.Z) / 2.0,
                        labelHeight,
                        segment.End.X - segment.Start.X,
                        segment.End.Y - segment.Start.Y,
                        segment.End.Z - segment.Start.Z));
                }
            }

            List<EnvelopePoint> gridLines = new List<EnvelopePoint>();
            var numericTicks = new List<EnvelopeLabel>();
            var letterLabels = new List<EnvelopeLabel>();
            var pins = new List<EnvelopePin>();
            EnvelopePoint upstreamEndpoint = null;
            EnvelopePoint downstreamEndpoint = null;
            EnvelopePoint gridAnchor = null;
            QuickLineColumnGrid grid = QuickLineColumnGridPlanner.Plan(
                model.Segments, model.UpstreamAxisLabel,
                model.DownstreamAxisLabel, model.UpstreamEndpoint,
                model.DownstreamEndpoint, model.DirectionResolved,
                QuickLineColumnGridPlanner.DefaultSpacingMillimetres,
                model.DirectionDiagnostics);
            if (grid != null)
            {
                gridAnchor = EnvelopePoint.Of(grid.GridAnchor);
                if (grid.DirectionResolved)
                {
                    upstreamEndpoint = EnvelopePoint.Of(
                        grid.UpstreamEndpoint.Value);
                    downstreamEndpoint = EnvelopePoint.Of(
                        grid.DownstreamEndpoint.Value);
                }
                foreach (QuickLineColumnGridLine line in grid.Lines)
                {
                    gridLines.Add(line.ConstantX
                        ? new EnvelopePoint(line.FixedValue, line.Minimum, 0.0)
                        : new EnvelopePoint(line.Minimum, line.FixedValue, 0.0));
                    gridLines.Add(line.ConstantX
                        ? new EnvelopePoint(line.FixedValue, line.Maximum, 0.0)
                        : new EnvelopePoint(line.Maximum, line.FixedValue, 0.0));
                }
                double tickHeight = diagonal * TickHeightDiagonalRatio;
                foreach (QuickLineColumnGridTick tick in grid.NumericTicks)
                {
                    numericTicks.Add(new EnvelopeLabel(
                        tick.Number.ToString(CultureInfo.InvariantCulture),
                        grid.NumericAxisIsX ? tick.Position
                            : (grid.MinX + grid.MaxX) / 2.0,
                        grid.NumericAxisIsX
                            ? (grid.MinY + grid.MaxY) / 2.0 : tick.Position,
                        tickHeight * 0.5, tickHeight));
                }
                foreach (QuickLineColumnGridLetterLabel letter
                    in grid.LetterLabels)
                {
                    letterLabels.Add(new EnvelopeLabel(
                        letter.Letter,
                        grid.NumericAxisIsX
                            ? (grid.MinX + grid.MaxX) / 2.0 : letter.Position,
                        grid.NumericAxisIsX ? letter.Position
                            : (grid.MinY + grid.MaxY) / 2.0,
                        tickHeight * 0.5, tickHeight));
                }
                if (grid.DirectionResolved)
                {
                    double pinLength = diagonal * EndpointPinLengthDiagonalRatio;
                    AddEndpointPin(pins, grid.UpstreamEndpoint.Value,
                        model.UpstreamAxisLabel, pinLength, tickHeight);
                    AddEndpointPin(pins, grid.DownstreamEndpoint.Value,
                        model.DownstreamAxisLabel, pinLength, tickHeight);
                }
            }

            var levels = new List<EnvelopeReferenceLevel>();
            IReadOnlyList<PreviewReferenceLevel> source =
                referenceLevels ?? Array.Empty<PreviewReferenceLevel>();
            // 3.3.7 三轮：锚定解析（单跳，绝对空间）——设备/母线高度相对本层
            // 地面条目（如母线 3660 锚 2F地面 → 绝对 11460）；锚定标签按
            // 包含匹配、自引用排除、找不到保持原值不猜。
            foreach (PreviewReferenceLevel level in source)
            {
                if (level == null || level.Label.Length == 0) continue;
                double z = level.ZMillimetres;
                if (level.AnchorLabel.Length > 0)
                {
                    for (int index = 0; index < source.Count; index++)
                    {
                        if (ReferenceEquals(source[index], level)) continue;
                        PreviewReferenceLevel candidate = source[index];
                        if (candidate == null || candidate.Kind == null) continue;
                        if ((candidate.Label ?? "").IndexOf(level.AnchorLabel,
                                StringComparison.Ordinal) < 0) continue;
                        z += candidate.ZMillimetres;
                        break;
                    }
                }
                levels.Add(new EnvelopeReferenceLevel(level.Label, z,
                    level.Kind));
            }
            double baseFloorZ = 0.0;
            if (!string.IsNullOrWhiteSpace(baseFloorLabel))
            {
                foreach (EnvelopeReferenceLevel level in levels)
                {
                    if ((level.Label ?? "").IndexOf(baseFloorLabel,
                            StringComparison.Ordinal) < 0) continue;
                    baseFloorZ = level.Z;
                    break;
                }
            }
            // ---- D-059：楼层平面、图签式轴位边标、整趟标签、上游柜 ----
            // 楼层平面是一个盖住整条路线（外留半跨到两跨）的矩形，四边对齐柱网线。
            double pitch = QuickLineColumnGridPlanner.DefaultSpacingMillimetres;
            double anchorX = gridAnchor != null ? gridAnchor.X : bounds.MinX;
            double anchorY = gridAnchor != null ? gridAnchor.Y : bounds.MinY;
            double margin = Math.Max(pitch * 0.5, Math.Min(pitch * 2.0,
                0.2 * Math.Max(bounds.MaxX - bounds.MinX, bounds.MaxY - bounds.MinY)));
            double slabMinX = Snap(anchorX, bounds.MinX - margin, pitch, false);
            double slabMaxX = Snap(anchorX, bounds.MaxX + margin, pitch, true);
            double slabMinY = Snap(anchorY, bounds.MinY - margin, pitch, false);
            double slabMaxY = Snap(anchorY, bounds.MaxY + margin, pitch, true);

            var floors = new List<EnvelopeFloor>();
            if (model.Floors != null)
            {
                foreach (PreviewFloorSlab slab in model.Floors.Floors)
                {
                    var floorGrid = new List<EnvelopePoint>();
                    foreach (double x in QuickLineColumnGridPlanner.GridPositions(
                        anchorX, slabMinX, slabMaxX, pitch))
                    {
                        floorGrid.Add(new EnvelopePoint(x, slabMinY, slab.SceneZ));
                        floorGrid.Add(new EnvelopePoint(x, slabMaxY, slab.SceneZ));
                    }
                    foreach (double y in QuickLineColumnGridPlanner.GridPositions(
                        anchorY, slabMinY, slabMaxY, pitch))
                    {
                        floorGrid.Add(new EnvelopePoint(slabMinX, y, slab.SceneZ));
                        floorGrid.Add(new EnvelopePoint(slabMaxX, y, slab.SceneZ));
                    }
                    floors.Add(new EnvelopeFloor(slab.Label, ElevationText(slab.SceneZ),
                        slab.SceneZ, slabMinX, slabMinY, slabMaxX, slabMaxY, floorGrid));
                }
            }

            // 轴位边标：数字、字母分别贴在最高那层平面的两条相邻外边上。
            var axisEdgeLabels = new List<EnvelopeLabel>();
            if (floors.Count > 0 && grid != null)
            {
                double tickHeight = diagonal * TickHeightDiagonalRatio;
                double offset = pitch * 0.3;
                EnvelopeFloor top = floors[floors.Count - 1];
                bool numericIsX = grid.NumericAxisIsX;
                foreach (Tuple<double, int> mark in ExtendNumbers(grid.NumericTicks,
                    numericIsX ? slabMinX : slabMinY, numericIsX ? slabMaxX : slabMaxY,
                    pitch, numericIsX ? anchorX : anchorY,
                    grid.NumbersIncreaseWithAxis))
                {
                    axisEdgeLabels.Add(numericIsX
                        ? new EnvelopeLabel(mark.Item2.ToString(CultureInfo.InvariantCulture),
                            mark.Item1, slabMinY - offset, top.Z, tickHeight)
                        : new EnvelopeLabel(mark.Item2.ToString(CultureInfo.InvariantCulture),
                            slabMinX - offset, mark.Item1, top.Z, tickHeight));
                }
                foreach (Tuple<double, string> mark in ExtendLetters(grid.LetterLabels,
                    numericIsX ? slabMinY : slabMinX, numericIsX ? slabMaxY : slabMaxX,
                    pitch, numericIsX ? anchorY : anchorX,
                    grid.LettersIncreaseWithAxis))
                {
                    axisEdgeLabels.Add(numericIsX
                        ? new EnvelopeLabel(mark.Item2, slabMinX - offset, mark.Item1, top.Z, tickHeight)
                        : new EnvelopeLabel(mark.Item2, mark.Item1, slabMinY - offset, top.Z, tickHeight));
                }
            }

            // 桥架/线管整趟标签：一趟一个，放在它盖住的中间那段电缆的上方。
            var runLabels = new List<EnvelopeLabel>();
            var segmentById = new Dictionary<string, EnvelopeSegment>(
                StringComparer.OrdinalIgnoreCase);
            foreach (EnvelopeSegment segment in segments)
                if (!segmentById.ContainsKey(segment.Id)) segmentById[segment.Id] = segment;
            foreach (QuickLineOrbitPreviewAnnotationRun run in model.AnnotationRuns)
            {
                if (run.HostSegmentIds.Count == 0) continue;
                if (!segmentById.TryGetValue(run.HostSegmentIds[run.HostSegmentIds.Count / 2],
                    out EnvelopeSegment host)) continue;
                string length = run.LengthMillimetres.HasValue
                    ? " · " + PreviewFloorPlan.Meters(run.LengthMillimetres.Value) + "m"
                    : string.Empty;
                string text = run.Kind == RouteAnnotationTextKind.Tray
                    ? "桥架" + (run.TrayWidthMillimetres.HasValue
                        ? " " + Math.Round(run.TrayWidthMillimetres.Value)
                            .ToString(CultureInfo.InvariantCulture) : string.Empty) + length
                    : "线管" + length;
                // 3.8.0.2：整趟标签同样贴着它盖住的那段电缆（锚点＝段中点），叠加层
                // 沿线排字、放在线的下侧（米数在上侧，互不打架）。
                runLabels.Add(new EnvelopeLabel(text,
                    (host.Start.X + host.End.X) / 2.0, (host.Start.Y + host.End.Y) / 2.0,
                    (host.Start.Z + host.End.Z) / 2.0, labelHeight,
                    host.End.X - host.Start.X, host.End.Y - host.Start.Y,
                    host.End.Z - host.Start.Z));
            }

            return new QuickLineOrbitPreviewEnvelope(
                segments, gridLines, numericTicks, letterLabels,
                segmentLabels, pins, diagonal,
                model.AxisSummary ?? string.Empty,
                (model.UpstreamAxisLabel ?? string.Empty).Trim(),
                (model.DownstreamAxisLabel ?? string.Empty).Trim(),
                anyAssumed, levels, baseFloorZ,
                upstreamEndpoint, downstreamEndpoint, gridAnchor,
                grid?.DirectionResolved ?? false,
                grid?.Diagnostics ?? model.DirectionDiagnostics,
                BuildHoseGeometry(model.Hose, downstreamEndpoint, segments),
                model.RecognitionSummary ?? string.Empty,
                model.RouteIssues ?? Array.Empty<string>(),
                floors, axisEdgeLabels, runLabels,
                PlanCabinet(model, segments, upstreamEndpoint,
                    CabinetVisualScale(model.CabinetKind, bounds, floors)));
        }

        /// <summary>把一个坐标吸附到柱网线上（outward=true 向外、false 向内）。</summary>
        private static double Snap(double anchor, double value, double pitch, bool outward)
        {
            double steps = (value - anchor) / pitch;
            return anchor + (outward ? Math.Ceiling(steps - 1e-9)
                : Math.Floor(steps + 1e-9)) * pitch;
        }

        private static string ElevationText(double sceneZ)
        {
            if (Math.Abs(sceneZ) < 0.5) return "±0.00";
            return (sceneZ > 0 ? "+" : "-") + Math.Abs(sceneZ / 1000.0).ToString("0.00",
                CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 数字轴号沿柱网铺满平面范围：已知刻度之间按同一间隔（号/毫米）线性
        /// 外推。只有一个刻度时按网格给出的递增方向（数字沿 +坐标递增还是
        /// 递减）外推；一个刻度都没有就不标（不猜）。
        /// </summary>
        private static IEnumerable<Tuple<double, int>> ExtendNumbers(
            IReadOnlyList<QuickLineColumnGridTick> ticks, double minimum,
            double maximum, double pitch, double anchor,
            bool increasesWithAxis)
        {
            if (ticks == null || ticks.Count == 0) yield break;
            List<QuickLineColumnGridTick> ordered = ticks
                .OrderBy(tick => tick.Position).ToList();
            QuickLineColumnGridTick first = ordered[0];
            double perMillimetre = (increasesWithAxis ? 1.0 : -1.0) / pitch;
            if (ordered.Count > 1)
            {
                QuickLineColumnGridTick last = ordered[ordered.Count - 1];
                if (last.Position - first.Position > 1e-6)
                    perMillimetre = (last.Number - first.Number)
                        / (last.Position - first.Position);
            }
            int? previous = null;
            foreach (double position in QuickLineColumnGridPlanner.GridPositions(
                anchor, minimum, maximum, pitch))
            {
                double exact = first.Number + (position - first.Position) * perMillimetre;
                if (double.IsNaN(exact) || double.IsInfinity(exact)) continue;
                int number = (int)Math.Round(exact, MidpointRounding.AwayFromZero);
                if (number < 1 || number == previous) continue;
                previous = number;
                yield return Tuple.Create(position, number);
            }
        }

        /// <summary>字母轴号同理（字母按 26 进制折成序号后线性外推）。</summary>
        private static IEnumerable<Tuple<double, string>> ExtendLetters(
            IReadOnlyList<QuickLineColumnGridLetterLabel> labels, double minimum,
            double maximum, double pitch, double anchor,
            bool increasesWithAxis)
        {
            if (labels == null || labels.Count == 0) yield break;
            List<QuickLineColumnGridLetterLabel> ordered = labels
                .OrderBy(label => label.Position).ToList();
            QuickLineColumnGridLetterLabel first = ordered[0];
            long firstIndex = LetterIndex(first.Letter);
            if (firstIndex < 1) yield break;
            double perMillimetre = (increasesWithAxis ? 1.0 : -1.0) / pitch;
            if (ordered.Count > 1)
            {
                QuickLineColumnGridLetterLabel last = ordered[ordered.Count - 1];
                long lastIndex = LetterIndex(last.Letter);
                if (lastIndex >= 1 && last.Position - first.Position > 1e-6)
                    perMillimetre = (lastIndex - firstIndex)
                        / (last.Position - first.Position);
            }
            string previous = null;
            foreach (double position in QuickLineColumnGridPlanner.GridPositions(
                anchor, minimum, maximum, pitch))
            {
                double exact = firstIndex + (position - first.Position) * perMillimetre;
                if (double.IsNaN(exact) || double.IsInfinity(exact)) continue;
                string letter = QuickLineColumnGridPlanner.LettersFromIndex(
                    (long)Math.Round(exact, MidpointRounding.AwayFromZero));
                if (letter == null || letter == previous) continue;
                previous = letter;
                yield return Tuple.Create(position, letter);
            }
        }

        private static long LetterIndex(string letter)
        {
            long index = 0;
            foreach (char character in (letter ?? string.Empty).ToUpperInvariant())
            {
                if (character < 'A' || character > 'Z') return -1;
                index = index * 26 + (character - 'A' + 1);
            }
            return index;
        }

        /// <summary>
        /// 上游柜。端点段是立管时，柜子的朝向取立管另一端上相接的第一根水平段
        /// （柜子放在它的反方向）；基础点规则在 PreviewCabinet.Plan 里。
        /// </summary>
        private static EnvelopeCabinet PlanCabinet(QuickLineOrbitPreviewModel model,
            IReadOnlyList<EnvelopeSegment> segments, EnvelopePoint upstreamEndpoint,
            double visualScale)
        {
            if (model.CabinetKind == PreviewCabinetKind.None || upstreamEndpoint == null)
                return null;
            EnvelopeSegment incident = null;
            bool endpointIsStart = false;
            foreach (EnvelopeSegment segment in segments)
            {
                if (SamePoint(segment.Start, upstreamEndpoint))
                {
                    incident = segment;
                    endpointIsStart = true;
                    break;
                }
                if (SamePoint(segment.End, upstreamEndpoint))
                {
                    incident = segment;
                    break;
                }
            }
            EnvelopePoint other = incident == null ? upstreamEndpoint
                : (endpointIsStart ? incident.End : incident.Start);
            double directionX = other.X - upstreamEndpoint.X;
            double directionY = other.Y - upstreamEndpoint.Y;
            if (directionX * directionX + directionY * directionY <= 1e-6
                && incident != null)
            {
                foreach (EnvelopeSegment segment in segments)
                {
                    if (string.Equals(segment.Id, incident.Id, StringComparison.OrdinalIgnoreCase))
                        continue;
                    EnvelopePoint far;
                    if (SamePoint(segment.Start, other)) far = segment.End;
                    else if (SamePoint(segment.End, other)) far = segment.Start;
                    else continue;
                    if ((far.X - other.X) * (far.X - other.X)
                        + (far.Y - other.Y) * (far.Y - other.Y) <= 1e-6) continue;
                    directionX = far.X - other.X;
                    directionY = far.Y - other.Y;
                    break;
                }
            }
            PreviewFloorSlab upstreamSlab = model.Floors?.UpstreamSlab;
            PreviewCabinet plan = PreviewCabinet.Plan(model.CabinetKind,
                upstreamEndpoint.X, upstreamEndpoint.Y, upstreamEndpoint.Z,
                other.X, other.Y, other.Z, directionX, directionY,
                upstreamSlab == null ? (double?)null : upstreamSlab.SceneZ,
                visualScale);
            if (plan == null) return null;
            return new EnvelopeCabinet(plan.Kind, plan.Label, plan.BaseX, plan.BaseY,
                plan.BaseZ, plan.SizeX, plan.SizeY, plan.Height, upstreamEndpoint,
                plan.MountingFloorZ);
        }

        /// <summary>
        /// 3.8.0.2：占位方块的放大倍数——楼层平面 + 线路的包围盒对角线里，方块
        /// 的宽度至少要占 <see cref="PreviewCabinet.MinimumWidthViewDiagonalFraction"/>
        /// （真实尺寸在几十米的楼层里只有十来个像素）。高度不放大。
        /// </summary>
        private static double CabinetVisualScale(PreviewCabinetKind kind,
            Bounds routeBounds, IReadOnlyList<EnvelopeFloor> floors)
        {
            if (kind == PreviewCabinetKind.None) return 1.0;
            double minX = routeBounds.MinX, minY = routeBounds.MinY;
            double maxX = routeBounds.MaxX, maxY = routeBounds.MaxY;
            double minZ = routeBounds.MinZ, maxZ = routeBounds.MaxZ;
            foreach (EnvelopeFloor floor in floors ?? Array.Empty<EnvelopeFloor>())
            {
                minX = Math.Min(minX, floor.MinX);
                minY = Math.Min(minY, floor.MinY);
                maxX = Math.Max(maxX, floor.MaxX);
                maxY = Math.Max(maxY, floor.MaxY);
                minZ = Math.Min(minZ, floor.Z);
                maxZ = Math.Max(maxZ, floor.Z);
            }
            double width = Math.Max(maxX - minX, 0.0);
            double depth = Math.Max(maxY - minY, 0.0);
            double height = Math.Max(maxZ - minZ, 0.0);
            return PreviewCabinet.VisualScale(kind, Math.Sqrt(
                width * width + depth * depth + height * height));
        }

        /// <summary>
        /// D-058：软管示意几何——从设备端沿「设备端所在末段」的方向伸出一段，
        /// 长度取软管毫米。方向未确认时软管放不出来（返回 null），摘要里仍报长度。
        /// </summary>
        private static EnvelopeHose BuildHoseGeometry(
            QuickLineOrbitPreviewHose hose, EnvelopePoint deviceEnd,
            IReadOnlyList<EnvelopeSegment> segments)
        {
            if (hose == null || deviceEnd == null) return null;
            foreach (EnvelopeSegment segment in segments)
            {
                EnvelopePoint other;
                if (SamePoint(segment.Start, deviceEnd)) other = segment.End;
                else if (SamePoint(segment.End, deviceEnd)) other = segment.Start;
                else continue;
                double dx = deviceEnd.X - other.X;
                double dy = deviceEnd.Y - other.Y;
                double dz = deviceEnd.Z - other.Z;
                double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (length < 1e-9) continue;
                string label = hose.DiameterMillimetres.HasValue
                    ? "软管 ⌀" + Math.Round(hose.DiameterMillimetres.Value)
                        .ToString(CultureInfo.InvariantCulture) + " · "
                    : "软管 · ";
                label += PreviewFloorPlan.Meters(hose.LengthMillimetres) + "m";
                if (hose.ByLabelOnly) label += "（U1B 标注）";
                return new EnvelopeHose(deviceEnd, new EnvelopePoint(
                    deviceEnd.X + dx / length * hose.LengthMillimetres,
                    deviceEnd.Y + dy / length * hose.LengthMillimetres,
                    deviceEnd.Z + dz / length * hose.LengthMillimetres),
                    hose.LengthMillimetres, label);
            }
            return null;
        }

        private static bool SamePoint(EnvelopePoint left, EnvelopePoint right)
            => left.X.Equals(right.X) && left.Y.Equals(right.Y)
                && left.Z.Equals(right.Z);

        /// <summary>段方向的水平法向（dir × ẑ 归一化；竖直段回退 ŷ——与
        /// 3.3.2 控件同口径，竖直爬升段的通道同样向两侧偏移）。</summary>
        private static EnvelopeChannel BuildChannelRails(
            QuickLineOrbitPreviewSegment segment, double offset, string kind)
        {
            double dx = segment.End.X - segment.Start.X;
            double dy = segment.End.Y - segment.Start.Y;
            double dz = segment.End.Z - segment.Start.Z;
            if (dx * dx + dy * dy + dz * dz < 1e-12) return null;
            // n = dir × ẑ = (dy, -dx, 0)（先归一化 dir 再叉乘等价于叉乘后归一化）。
            double nx = dy;
            double ny = -dx;
            double length = Math.Sqrt(nx * nx + ny * ny);
            if (length < 1e-9)
            {
                nx = 0.0;
                ny = 1.0;
            }
            else
            {
                nx /= length;
                ny /= length;
            }
            double sx = segment.Start.X, sy = segment.Start.Y, sz = segment.Start.Z;
            double ex = segment.End.X, ey = segment.End.Y, ez = segment.End.Z;
            return new EnvelopeChannel(kind,
                new EnvelopePoint(sx + nx * offset, sy + ny * offset, sz),
                new EnvelopePoint(ex + nx * offset, ey + ny * offset, ez),
                new EnvelopePoint(sx - nx * offset, sy - ny * offset, sz),
                new EnvelopePoint(ex - nx * offset, ey - ny * offset, ez));
        }

        private static void AddEndpointPin(List<EnvelopePin> pins,
            QuickLineSpatialPoint endpoint, string label,
            double pinLength, double labelHeight)
        {
            string text = (label ?? string.Empty).Trim();
            if (text.Length == 0) return;
            pins.Add(new EnvelopePin(text,
                endpoint.X, endpoint.Y, endpoint.Z,
                endpoint.Z + pinLength,
                endpoint.Z + pinLength + labelHeight * 0.4,
                labelHeight));
        }

        private static string AxisName(QuickLineSpatialAxis axis)
        {
            switch (axis)
            {
                case QuickLineSpatialAxis.X: return "X";
                case QuickLineSpatialAxis.Y: return "Y";
                default: return "Z";
            }
        }

        /// <summary>电缆段（中心线 + 可选通道双线）。</summary>
        public sealed class EnvelopeSegment
        {
            public EnvelopeSegment(string id, EnvelopePoint start,
                EnvelopePoint end, string axis, double distanceMillimetres,
                bool completed, EnvelopeChannel channel)
            {
                Id = id;
                Start = start;
                End = end;
                Axis = axis;
                DistanceMillimetres = distanceMillimetres;
                Completed = completed;
                Channel = channel;
            }

            public string Id { get; }
            public EnvelopePoint Start { get; }
            public EnvelopePoint End { get; }
            /// <summary>轴向名（「X」/「Y」/「Z」——渲染配色键）。</summary>
            public string Axis { get; }
            public double DistanceMillimetres { get; }
            public bool Completed { get; }
            /// <summary>桥架/线管通道双线（null=普通电缆段）。</summary>
            public EnvelopeChannel Channel { get; }
        }

        /// <summary>通道双线（a=正法向侧，b=负法向侧；a1→a2 与 b1→b2 平行于中心线）。</summary>
        public sealed class EnvelopeChannel
        {
            public EnvelopeChannel(string kind, EnvelopePoint a1,
                EnvelopePoint a2, EnvelopePoint b1, EnvelopePoint b2)
            {
                Kind = kind;
                A1 = a1;
                A2 = a2;
                B1 = b1;
                B2 = b2;
            }

            /// <summary>「tray」=桥架（红双线+半透明填充），「conduit」=线管（青细双线）。</summary>
            public string Kind { get; }
            public EnvelopePoint A1 { get; }
            public EnvelopePoint A2 { get; }
            public EnvelopePoint B1 { get; }
            public EnvelopePoint B2 { get; }
        }

        /// <summary>D-058：软管示意几何（设备端 → 沿末段方向伸出软管长度）。</summary>
        public sealed class EnvelopeHose
        {
            internal EnvelopeHose(EnvelopePoint start, EnvelopePoint end,
                double lengthMillimetres, string label)
            {
                Start = start;
                End = end;
                LengthMillimetres = lengthMillimetres;
                Label = label ?? string.Empty;
            }

            public EnvelopePoint Start { get; }
            public EnvelopePoint End { get; }
            public double LengthMillimetres { get; }
            /// <summary>段旁标签文字（如「软管 ⌀100 · 2.5m」）。</summary>
            public string Label { get; }
        }

        /// <summary>
        /// D-059：一块楼层平面——矩形四角（场景 XY）、该层的高度、楼层名与
        /// 相对高度文字，以及这一层地面上的柱网线（首尾成对）。
        /// </summary>
        public sealed class EnvelopeFloor
        {
            internal EnvelopeFloor(string label, string elevationText, double z,
                double minX, double minY, double maxX, double maxY,
                IReadOnlyList<EnvelopePoint> gridLines)
            {
                Label = label ?? string.Empty;
                ElevationText = elevationText ?? string.Empty;
                Z = z;
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
                GridLines = gridLines ?? Array.Empty<EnvelopePoint>();
            }

            public string Label { get; }
            /// <summary>相对最低那块平面的高度（如「+7.80」）。</summary>
            public string ElevationText { get; }
            public double Z { get; }
            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public IReadOnlyList<EnvelopePoint> GridLines { get; }
        }

        /// <summary>D-059：上游柜盒子（轴对齐：底面中心 + 三向尺寸）。</summary>
        public sealed class EnvelopeCabinet
        {
            internal EnvelopeCabinet(PreviewCabinetKind kind, string label,
                double centerX, double centerY, double baseZ, double sizeX,
                double sizeY, double height, EnvelopePoint connection,
                double? mountingFloorZ)
            {
                Kind = kind;
                Label = label ?? string.Empty;
                CenterX = centerX;
                CenterY = centerY;
                BaseZ = baseZ;
                SizeX = sizeX;
                SizeY = sizeY;
                Height = height;
                Connection = connection;
                MountingFloorZ = mountingFloorZ;
            }

            public PreviewCabinetKind Kind { get; }
            public string Label { get; }
            public double CenterX { get; }
            public double CenterY { get; }
            /// <summary>底面高度（场景 Z）。</summary>
            public double BaseZ { get; }
            public double SizeX { get; }
            public double SizeY { get; }
            public double Height { get; }
            /// <summary>柜子连着的路线端点（上游端）。</summary>
            public EnvelopePoint Connection { get; }
            /// <summary>柜子所站楼层的地面高度（母线插接口为空）。</summary>
            public double? MountingFloorZ { get; }
            /// <summary>盒顶中心高度：标签挂在这里。</summary>
            public double TopZ => BaseZ + Height;
        }

        /// <summary>三维点（mm；世界 Z 向上）。</summary>
        public sealed class EnvelopePoint
        {
            public EnvelopePoint(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }

            public static EnvelopePoint Of(QuickLineSpatialPoint point)
                => new EnvelopePoint(point.X, point.Y, point.Z);
        }

        /// <summary>3.3.6：参照层（Z + 标签原文）；3.3.7 起 Kind 告诉页面画哪种几何。</summary>
        public sealed class EnvelopeReferenceLevel
        {
            public EnvelopeReferenceLevel(string label, double z,
                string kind = null)
            {
                Label = label;
                Z = z;
                Kind = string.IsNullOrEmpty(kind) ? "frame" : kind;
            }

            public string Label { get; }
            /// <summary>相对场景地面的高度（mm）。</summary>
            public double Z { get; }
            /// <summary>参照物类型（floor/steel/grating/raised/device/bus；缺省 frame=水平线框）。</summary>
            public string Kind { get; }
        }

        /// <summary>场景文字标签（HTML 叠加层渲染；WorldHeight 供字号换算参考）。</summary>
        public sealed class EnvelopeLabel
        {
            public EnvelopeLabel(string text, double x, double y, double z,
                double worldHeight)
                : this(text, x, y, z, worldHeight, 0.0, 0.0, 0.0)
            {
            }

            /// <summary>
            /// 3.8.0.2：贴着一条线的标签——锚点在线段中点，带上线段方向，叠加层
            /// 沿线排字并固定贴在线旁。
            /// </summary>
            public EnvelopeLabel(string text, double x, double y, double z,
                double worldHeight, double directionX, double directionY,
                double directionZ)
            {
                Text = text;
                X = x;
                Y = y;
                Z = z;
                WorldHeight = worldHeight;
                DirectionX = directionX;
                DirectionY = directionY;
                DirectionZ = directionZ;
            }

            public string Text { get; }
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public double WorldHeight { get; }
            /// <summary>所贴线段的方向（世界坐标）；三点全 0 = 不是线标签。</summary>
            public double DirectionX { get; }
            public double DirectionY { get; }
            public double DirectionZ { get; }
            public bool HasDirection => DirectionX != 0.0 || DirectionY != 0.0
                || DirectionZ != 0.0;
        }

        /// <summary>端点轴位钉：竖直引线（Z→TipZ）+ 顶端标签。</summary>
        public sealed class EnvelopePin
        {
            public EnvelopePin(string text, double x, double y, double z,
                double tipZ, double labelZ, double labelHeight)
            {
                Text = text;
                X = x;
                Y = y;
                Z = z;
                TipZ = tipZ;
                LabelZ = labelZ;
                LabelHeight = labelHeight;
            }

            public string Text { get; }
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public double TipZ { get; }
            public double LabelZ { get; }
            public double LabelHeight { get; }
        }

        /// <summary>场景包络（对角线换算用；口径同 3.3.2 控件内嵌版）。</summary>
        private sealed class Bounds
        {
            private Bounds(double minX, double minY, double minZ,
                double maxX, double maxY, double maxZ)
            {
                MinX = minX;
                MinY = minY;
                MinZ = minZ;
                MaxX = maxX;
                MaxY = maxY;
                MaxZ = maxZ;
            }

            public double MinX { get; }
            public double MinY { get; }
            public double MinZ { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public double MaxZ { get; }

            public double Diagonal => Math.Sqrt(
                (MaxX - MinX) * (MaxX - MinX)
                + (MaxY - MinY) * (MaxY - MinY)
                + (MaxZ - MinZ) * (MaxZ - MinZ));

            public static Bounds Of(
                IReadOnlyList<QuickLineOrbitPreviewSegment> segments)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                double maxZ = double.MinValue;
                foreach (QuickLineOrbitPreviewSegment segment in segments)
                {
                    foreach (QuickLineSpatialPoint point in new[]
                        { segment.Start, segment.End })
                    {
                        minX = Math.Min(minX, point.X);
                        minY = Math.Min(minY, point.Y);
                        minZ = Math.Min(minZ, point.Z);
                        maxX = Math.Max(maxX, point.X);
                        maxY = Math.Max(maxY, point.Y);
                        maxZ = Math.Max(maxZ, point.Z);
                    }
                }
                return new Bounds(minX, minY, minZ, maxX, maxY, maxZ);
            }
        }
    }
}
