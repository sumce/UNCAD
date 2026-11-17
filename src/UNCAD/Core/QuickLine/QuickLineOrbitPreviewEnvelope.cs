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
            bool anyTrayWidthAssumedDefault)
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
        }

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

        /// <summary>由预览模型构建信封（model 为 null/空段时返回 null）。</summary>
        public static QuickLineOrbitPreviewEnvelope Build(
            QuickLineOrbitPreviewModel model)
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
                    // 特性B：段中点上方 0.6×标签高。
                    segmentLabels.Add(new EnvelopeLabel(
                        segment.LabelText,
                        (segment.Start.X + segment.End.X) / 2.0,
                        (segment.Start.Y + segment.End.Y) / 2.0,
                        (segment.Start.Z + segment.End.Z) / 2.0
                            + labelHeight * 0.6,
                        labelHeight));
                }
            }

            List<EnvelopePoint> gridLines = new List<EnvelopePoint>();
            var numericTicks = new List<EnvelopeLabel>();
            var letterLabels = new List<EnvelopeLabel>();
            var pins = new List<EnvelopePin>();
            QuickLineColumnGrid grid = QuickLineColumnGridPlanner.Plan(
                model.Segments, model.UpstreamAxisLabel,
                model.DownstreamAxisLabel);
            if (grid != null)
            {
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
                        grid.NumericAxisIsX ? tick.Position : grid.MinX,
                        grid.NumericAxisIsX ? grid.MinY : tick.Position,
                        tickHeight * 0.5, tickHeight));
                }
                foreach (QuickLineColumnGridLetterLabel letter
                    in grid.LetterLabels)
                {
                    letterLabels.Add(new EnvelopeLabel(
                        letter.Letter,
                        grid.NumericAxisIsX ? grid.MaxX : letter.Position,
                        grid.NumericAxisIsX ? letter.Position : grid.MaxY,
                        tickHeight * 0.5, tickHeight));
                }
                double pinLength = diagonal * EndpointPinLengthDiagonalRatio;
                AddEndpointPin(pins, grid.UpstreamEndpoint,
                    model.UpstreamAxisLabel, pinLength, tickHeight);
                AddEndpointPin(pins, grid.DownstreamEndpoint,
                    model.DownstreamAxisLabel, pinLength, tickHeight);
            }

            return new QuickLineOrbitPreviewEnvelope(
                segments, gridLines, numericTicks, letterLabels,
                segmentLabels, pins, diagonal,
                model.AxisSummary ?? string.Empty,
                (model.UpstreamAxisLabel ?? string.Empty).Trim(),
                (model.DownstreamAxisLabel ?? string.Empty).Trim(),
                anyAssumed);
        }

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

        /// <summary>场景文字标签（HTML 叠加层渲染；WorldHeight 供字号换算参考）。</summary>
        public sealed class EnvelopeLabel
        {
            public EnvelopeLabel(string text, double x, double y, double z,
                double worldHeight)
            {
                Text = text;
                X = x;
                Y = y;
                Z = z;
                WorldHeight = worldHeight;
            }

            public string Text { get; }
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public double WorldHeight { get; }
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

            private double MinX { get; }
            private double MinY { get; }
            private double MinZ { get; }
            private double MaxX { get; }
            private double MaxY { get; }
            private double MaxZ { get; }

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
