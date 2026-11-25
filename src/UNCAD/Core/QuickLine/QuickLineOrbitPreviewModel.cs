using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// 3.3.1（D-054 Amendment，特性D）：一段路由的桥架/线管通道归属。由
    /// ParallelAnnotationOwnership 把 U1Q/U1C 输出归属到宿主电缆段后带入预览；
    /// 无归属（普通电缆段）时 Segment.Channel 为 null。
    /// </summary>
    public sealed class QuickLineOrbitPreviewChannel
    {
        public QuickLineOrbitPreviewChannel(double? trayWidthMillimetres,
            bool trayWidthAssumedDefault, bool hasConduit)
        {
            TrayWidthMillimetres = trayWidthMillimetres;
            TrayWidthAssumedDefault = trayWidthAssumedDefault;
            HasConduit = hasConduit;
        }

        /// <summary>桥架宽度（mm）；解析不出宽度时是 200 缺省值。</summary>
        public double? TrayWidthMillimetres { get; }
        /// <summary>宽度是否为缺省值（U1Q 标注文本里解析不出规格）——图例要注明。</summary>
        public bool TrayWidthAssumedDefault { get; }
        /// <summary>该段是线管（U1C 归属；无宽度语义）。</summary>
        public bool HasConduit { get; }
    }

    /// <summary>3D 预览的一条线段（已地面归零；3.3.0 引入，渲染内核无关）。</summary>
    public sealed class QuickLineOrbitPreviewSegment
    {
        public QuickLineOrbitPreviewSegment(string id, QuickLineSpatialPoint start,
            QuickLineSpatialPoint end, QuickLineSpatialAxis axis,
            double distanceMillimetres, bool completed,
            string labelText = null, QuickLineOrbitPreviewChannel channel = null)
        {
            Id = id ?? string.Empty;
            Start = start;
            End = end;
            Axis = axis;
            DistanceMillimetres = distanceMillimetres;
            Completed = completed;
            LabelText = labelText ?? string.Empty;
            Channel = channel;
        }

        public string Id { get; }
        public QuickLineSpatialPoint Start { get; }
        public QuickLineSpatialPoint End { get; }
        public QuickLineSpatialAxis Axis { get; }
        public double DistanceMillimetres { get; }
        public bool Completed { get; }
        /// <summary>3.3.1（特性B）：源 CAD 毫米标原文（如「3500mm」），段中点标字用。</summary>
        public string LabelText { get; }
        /// <summary>3.3.1（特性D）：桥架/线管通道归属（null=普通电缆段）。</summary>
        public QuickLineOrbitPreviewChannel Channel { get; }
    }

    /// <summary>
    /// D-058：图框里的软管（设备末端的柔性段）。长度/口径来自 Ruanguan 软管块
    /// 属性；图框里只有 U1B 软管标注文字时取文字（<see cref="ByLabelOnly"/>）。
    /// 预览里按设备端末段的方向示意画出，不写回图纸、不进任何统计。
    /// </summary>
    public sealed class QuickLineOrbitPreviewHose
    {
        public QuickLineOrbitPreviewHose(double lengthMillimetres,
            double? diameterMillimetres, string sourceText, bool byLabelOnly)
        {
            if (double.IsNaN(lengthMillimetres) || double.IsInfinity(lengthMillimetres)
                || lengthMillimetres <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(lengthMillimetres));
            LengthMillimetres = lengthMillimetres;
            DiameterMillimetres = diameterMillimetres;
            SourceText = sourceText ?? string.Empty;
            ByLabelOnly = byLabelOnly;
        }

        /// <summary>软管长度（mm）。</summary>
        public double LengthMillimetres { get; }
        /// <summary>软管口径（mm）；读不出时为空。</summary>
        public double? DiameterMillimetres { get; }
        /// <summary>来源原文（Ruanguan 块属性或 U1B 标注文字）。</summary>
        public string SourceText { get; }
        /// <summary>长度来自 U1B 标注文字而不是 Ruanguan 块（U1B 文字不进统计）。</summary>
        public bool ByLabelOnly { get; }
    }

    /// <summary>
    /// D-059：一趟桥架/线管——一个标注、一个整趟总长、它盖住的若干宿主电缆段。
    /// 标注上的长度是这几段电缆合起来的长度，不是每段各有这么长。
    /// </summary>
    public sealed class QuickLineOrbitPreviewAnnotationRun
    {
        public QuickLineOrbitPreviewAnnotationRun(RouteAnnotationTextKind kind,
            string label, double? lengthMillimetres, double? trayWidthMillimetres,
            IReadOnlyList<string> hostSegmentIds, string recoveredBy)
        {
            Kind = kind;
            Label = label ?? string.Empty;
            LengthMillimetres = lengthMillimetres;
            TrayWidthMillimetres = trayWidthMillimetres;
            HostSegmentIds = hostSegmentIds ?? Array.Empty<string>();
            RecoveredBy = recoveredBy ?? string.Empty;
        }

        public RouteAnnotationTextKind Kind { get; }
        /// <summary>标注的首行（型号或规格）。</summary>
        public string Label { get; }
        /// <summary>整趟长度（mm）；标注里读不出长度时为空。</summary>
        public double? LengthMillimetres { get; }
        public double? TrayWidthMillimetres { get; }
        /// <summary>这一趟盖住的电缆线段 id（按路线顺序）。</summary>
        public IReadOnlyList<string> HostSegmentIds { get; }
        /// <summary>宿主怎么找到的：元数据 / 红线就近 / 文字就近。</summary>
        public string RecoveredBy { get; }
    }

    /// <summary>
    /// U1U 确认窗左侧 3D 预览的场景模型——由 QuickLineIsometricSceneBuilder
    /// .BuildAll（带毫米标）重建后整体**地面归零**（所有节点 Z 减去最小 Z，
    /// 「最低的那条线段的端点高度为 0」），附上游/下游轴位摘要文字与轴位原文。
    /// 3.3.5（D-054 Amendment）：渲染内核换 WebView2+three.js（3.3.2 曾换
    /// HelixToolkit.Wpf，现场反馈不可用后退役），本数据模型与 FromScene 构建
    /// 链始终不变（窗体只绘制，不重建场景——几何推导在信封构建器）。
    /// </summary>
    public sealed class QuickLineOrbitPreviewModel
    {
        public QuickLineOrbitPreviewModel(
            IReadOnlyList<QuickLineOrbitPreviewSegment> segments,
            string axisSummary, IReadOnlyList<string> diagnostics,
            string upstreamAxisLabel = null, string downstreamAxisLabel = null,
            QuickLineSpatialPoint? upstreamEndpoint = null,
            QuickLineSpatialPoint? downstreamEndpoint = null,
            string upstreamType = null, bool directionResolved = false,
            IReadOnlyList<string> directionDiagnostics = null,
            QuickLineOrbitPreviewHose hose = null,
            string recognitionSummary = null,
            IReadOnlyList<string> routeIssues = null,
            PreviewFloorPlan floors = null,
            PreviewCabinetKind cabinetKind = PreviewCabinetKind.None,
            IReadOnlyList<QuickLineOrbitPreviewAnnotationRun> annotationRuns = null)
        {
            Segments = segments ?? Array.Empty<QuickLineOrbitPreviewSegment>();
            AxisSummary = axisSummary ?? string.Empty;
            Diagnostics = diagnostics ?? Array.Empty<string>();
            UpstreamAxisLabel = upstreamAxisLabel ?? string.Empty;
            DownstreamAxisLabel = downstreamAxisLabel ?? string.Empty;
            UpstreamEndpoint = upstreamEndpoint;
            DownstreamEndpoint = downstreamEndpoint;
            UpstreamType = upstreamType ?? string.Empty;
            DirectionResolved = directionResolved
                && upstreamEndpoint != null && downstreamEndpoint != null;
            DirectionDiagnostics = directionDiagnostics
                ?? Array.Empty<string>();
            Hose = hose;
            RecognitionSummary = recognitionSummary ?? string.Empty;
            RouteIssues = routeIssues ?? Array.Empty<string>();
            Floors = floors;
            CabinetKind = cabinetKind;
            AnnotationRuns = annotationRuns ?? Array.Empty<QuickLineOrbitPreviewAnnotationRun>();
        }

        public IReadOnlyList<QuickLineOrbitPreviewSegment> Segments { get; }
        /// <summary>上游/下游轴位摘要（如「上游 48/V ↔ 下游 37/V · Δ11 跨 × 4.8m ≈ 52.8m」）。</summary>
        public string AxisSummary { get; }
        /// <summary>3.3.1（特性C）：上游轴位原文（QuickLineAxisSpan 解析前的台账原文）。</summary>
        public string UpstreamAxisLabel { get; }
        /// <summary>3.3.1（特性C）：下游轴位原文。</summary>
        public string DownstreamAxisLabel { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        public QuickLineSpatialPoint? UpstreamEndpoint { get; }
        public QuickLineSpatialPoint? DownstreamEndpoint { get; }
        public string UpstreamType { get; }
        public bool DirectionResolved { get; }
        public IReadOnlyList<string> DirectionDiagnostics { get; }
        /// <summary>D-058：图框里的软管；没有可读长度的软管时为 null。</summary>
        public QuickLineOrbitPreviewHose Hose { get; }
        /// <summary>D-058：线路识别结果一句话（段数/桥架/线管/软管/两端判定依据）。</summary>
        public string RecognitionSummary { get; }
        /// <summary>D-058：识别出的问题（无毫米标的线段、被容差接上的断口、非等测方向等）。</summary>
        public IReadOnlyList<string> RouteIssues { get; }
        /// <summary>D-059：要画的楼层平面（台账上下游楼层）；null = 一层都定不出来。</summary>
        public PreviewFloorPlan Floors { get; }
        /// <summary>D-059：上游柜的类型（I-Line 盘落地 / 插座盘离地 1 m / 母线插接口）。</summary>
        public PreviewCabinetKind CabinetKind { get; }
        /// <summary>D-059：桥架/线管整趟清单（每趟一个标注、一个总长、若干宿主电缆段）。</summary>
        public IReadOnlyList<QuickLineOrbitPreviewAnnotationRun> AnnotationRuns { get; }

        /// <summary>
        /// 由等测场景构建预览模型：先整体地面归零（minZ→0），再按上游柜的类型
        /// 整体抬高（3.8.0.2：插座盘的线路直接抬高 1 m，见
        /// <see cref="PreviewCabinet.RouteLiftMillimetres"/>）。段端点取场景节点
        /// 坐标，毫米值取真实距离（DistanceMillimetres）。scene 为 null 或没有
        /// 线段时返回 null（调用方隐藏预览区）。labelTexts/channels 按段句柄
        /// 带入毫米标原文与桥架/线管通道归属（缺省无标签无通道）。
        /// </summary>
        public static QuickLineOrbitPreviewModel FromScene(
            QuickLineIsometricScene scene, string axisSummary,
            string upstreamAxisLabel = null, string downstreamAxisLabel = null,
            IReadOnlyDictionary<string, string> labelTexts = null,
            IReadOnlyDictionary<string, QuickLineOrbitPreviewChannel> channels = null,
            QuickLineSpatialPoint? upstreamEndpoint = null,
            QuickLineSpatialPoint? downstreamEndpoint = null,
            string upstreamType = null, bool directionResolved = false,
            IReadOnlyList<string> directionDiagnostics = null,
            QuickLineOrbitPreviewHose hose = null,
            string recognitionSummary = null,
            IReadOnlyList<string> routeIssues = null,
            PreviewFloorPlan floors = null,
            PreviewCabinetKind cabinetKind = PreviewCabinetKind.None,
            IReadOnlyList<QuickLineOrbitPreviewAnnotationRun> annotationRuns = null)
        {
            if (scene == null || scene.Segments.Count == 0) return null;
            double minZ = scene.Nodes.Count == 0
                ? 0.0
                : scene.Nodes.Min(node => node.Position.Z);
            if (double.IsNaN(minZ) || double.IsInfinity(minZ)) minZ = 0.0;
            var positions = scene.Nodes.ToDictionary(node => node.Id,
                node => new QuickLineSpatialPoint(node.Position.X,
                    node.Position.Y, node.Position.Z - minZ),
                StringComparer.OrdinalIgnoreCase);
            var segments = new List<QuickLineOrbitPreviewSegment>(
                scene.Segments.Count);
            foreach (QuickLineIsometricSegment segment in scene.Segments)
            {
                if (!positions.TryGetValue(segment.StartNodeId,
                    out QuickLineSpatialPoint start)
                    || !positions.TryGetValue(segment.EndNodeId,
                    out QuickLineSpatialPoint end)) continue;
                string label = null;
                if (labelTexts != null
                    && labelTexts.TryGetValue(segment.Id, out string foundLabel))
                    label = foundLabel;
                QuickLineOrbitPreviewChannel channel = null;
                channels?.TryGetValue(segment.Id, out channel);
                segments.Add(new QuickLineOrbitPreviewSegment(segment.Id,
                    start, end, segment.Axis, segment.DistanceMillimetres,
                    segment.Completed, label, channel));
            }
            if (segments.Count == 0) return null;
            QuickLineSpatialPoint? upstream = NormalizeEndpoint(
                upstreamEndpoint, minZ);
            QuickLineSpatialPoint? downstream = NormalizeEndpoint(
                downstreamEndpoint, minZ);
            // 3.8.0.2：图框里画不出地面——插座盘的出线在楼层地面 + 1 m，整条
            // 线路直接抬高 1 m（盘的方块只是占位）。
            double lift = PreviewCabinet.RouteLiftMillimetres(cabinetKind);
            if (lift != 0.0)
            {
                segments = segments.Select(segment => new QuickLineOrbitPreviewSegment(
                    segment.Id, Lift(segment.Start, lift), Lift(segment.End, lift),
                    segment.Axis, segment.DistanceMillimetres, segment.Completed,
                    segment.LabelText, segment.Channel)).ToList();
                upstream = Lift(upstream, lift);
                downstream = Lift(downstream, lift);
            }
            return new QuickLineOrbitPreviewModel(segments, axisSummary,
                scene.Diagnostics, upstreamAxisLabel, downstreamAxisLabel,
                upstream, downstream,
                upstreamType, directionResolved, directionDiagnostics,
                hose, recognitionSummary, routeIssues, floors, cabinetKind, annotationRuns);
        }

        private static QuickLineSpatialPoint Lift(QuickLineSpatialPoint point,
            double lift)
            => new QuickLineSpatialPoint(point.X, point.Y, point.Z + lift);

        private static QuickLineSpatialPoint? Lift(QuickLineSpatialPoint? point,
            double lift)
            => point.HasValue
                ? Lift(point.Value, lift)
                : (QuickLineSpatialPoint?)null;

        private static QuickLineSpatialPoint? NormalizeEndpoint(
            QuickLineSpatialPoint? point, double minZ)
        {
            return point.HasValue
                ? new QuickLineSpatialPoint(point.Value.X, point.Value.Y,
                    point.Value.Z - minZ)
                : (QuickLineSpatialPoint?)null;
        }
    }
}
