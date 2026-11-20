using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using UNCAD.Core.QuickLine;

namespace UNCAD.UI
{
    /// <summary>
    /// 3.4.0：U1U 渲染信封（QuickLineOrbitPreviewEnvelope）→ 原生场景
    /// （QuickLine3dScene）。视觉公式与 3.3.5~3.3.8 的 three.js 页面
    /// （Web/QuickLineOrbit/app.js，已退役）逐条对齐：管线半径 0.0019×对角
    /// 线、桥架红双线+0.3 半透明填充带、线管青双线、参照建筑模型全套
    /// （楼板/环氧/华夫圆孔板/工字钢/葡萄架/自定义设备盒/母线玻璃/结构柱/地面）
    /// 配色与尺寸公式、本层基准重定位、柱网外扩一跨的包络推算、
    /// 相机方位 -135°/俯仰约 30°、距离 1.6×对角线。
    /// </summary>
    internal static class QuickLineOrbitSceneBuilder
    {
        private static readonly Color AxisX = FromRgb(0xE0, 0x6C, 0x00);
        private static readonly Color AxisY = FromRgb(0x2E, 0x9E, 0x5B);
        private static readonly Color AxisZ = FromRgb(0x3B, 0x6E, 0xD0);
        private static readonly Color GridTint = FromRgb(0x55, 0x60, 0x6E);
        private static readonly Color TrayTint = FromRgb(0xF2, 0x57, 0x4C);
        private static readonly Color ConduitTint = FromRgb(0x2E, 0xA8, 0xC0);
        private static readonly Color PinTint = FromRgb(0x99, 0xA7, 0xB8);
        private static readonly Color FrameTint = FromRgb(0xE0, 0xA9, 0x4A);

        // 3.3.7 参照物实体配色（原页面 refColor 原样搬迁）。
        private static readonly Color FloorTint = FromRgb(0x7D, 0x93, 0xA6);
        private static readonly Color FloorEdge = FromRgb(0xAA, 0xB6, 0xC2);
        private static readonly Color SteelTint = FromRgb(0xA5, 0xB2, 0xC3);
        private static readonly Color GratingTint = FromRgb(0x94, 0xA8, 0x84);
        private static readonly Color RaisedTint = FromRgb(0x8F, 0xAD, 0xC0);
        private static readonly Color DeviceTint = FromRgb(0xD9, 0xDF, 0xE6);
        private static readonly Color DeviceEdge = FromRgb(0x39, 0x42, 0x4E);
        private static readonly Color BusGlassTint = FromRgb(0xE8, 0xB4, 0x5F);
        private static readonly Color BusEdge = FromRgb(0x7A, 0x54, 0x26);
        private static readonly Color ColumnTint = FromRgb(0x5D, 0x69, 0x77);
        private static readonly Color GroundTint = FromRgb(0x39, 0x42, 0x4E);
        private static readonly Color EpoxyTint = FromRgb(0x5F, 0x9E, 0x6E);
        private static readonly Color EpoxyEdge = FromRgb(0x9F, 0xD3, 0xAB);
        private static readonly Color WaffleTint = FromRgb(0x84, 0x97, 0xA8);

        /// <summary>构建场景（envelope 为 null 时返回 null——空态由宿主提示）。</summary>
        public static QuickLine3dScene Build(
            QuickLineOrbitPreviewEnvelope envelope)
        {
            if (envelope == null) return null;
            var scene = new QuickLine3dScene();
            double diagonal = Math.Max(envelope.Diagonal, 1.0);
            scene.Diagonal = diagonal;

            Bounds bounds = MeasureBounds(envelope);
            double radius = diagonal * 0.0019;
            double baseFloorZ = envelope.BaseFloorZ;

            // 参照层：本层基准重定位（本层地面=场景 0）。
            var levels = new List<RefLevel>();
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeReferenceLevel raw
                in envelope.ReferenceLevels)
            {
                if (raw == null || string.IsNullOrEmpty(raw.Label) || raw.Z <= 0)
                    continue;
                levels.Add(new RefLevel(raw.Label, raw.Z - baseFloorZ,
                    (raw.Kind ?? "frame").ToLowerInvariant()));
            }

            QuickLinePreviewFootprint footprint = QuickLinePreviewFootprint.Plan(envelope);
            double margin = Math.Max(400.0, diagonal * 0.06);
            var areas = footprint.Regions.Select(area => new Extent(
                area.MinX, area.MinY, area.MaxX, area.MaxY, margin)).ToList();
            var extent = new Extent(areas.Min(area => area.Minx),
                areas.Min(area => area.Miny), areas.Max(area => area.Maxx),
                areas.Max(area => area.Maxy), margin);
            double tubeRadius = radius * 0.9;
            var devices = new List<RefLevel>();
            var buses = new List<RefLevel>();
            double modelTopZ = 0.0;
            int cornerFlip = 0;

            foreach (RefLevel level in levels)
            {
                bool anchorRight = cornerFlip++ % 2 == 1;
                if (!string.Equals(level.Kind, "device",
                        StringComparison.Ordinal) && level.Z > modelTopZ)
                    modelTopZ = level.Z;
                switch (level.Kind)
                {
                    case "floor":
                        foreach (Extent area in areas)
                            AddSlab(scene, level, area, FloorTint,
                                FloorEdge, Math.Max(220.0, diagonal * 0.005));
                        break;
                    case "epoxy":
                        foreach (Extent area in areas)
                            AddSlab(scene, level, area, EpoxyTint,
                                EpoxyEdge, Math.Max(180.0, diagonal * 0.004));
                        break;
                    case "waffle":
                        AddWafflePlate(scene, level, footprint);
                        break;
                    case "raised":
                        foreach (Extent area in areas)
                            AddSlab(scene, level, area, RaisedTint,
                                FloorEdge, Math.Max(140.0, diagonal * 0.003));
                        break;
                    case "steel":
                        foreach (Extent area in areas)
                            AddSteelBeams(scene, level, area, diagonal);
                        break;
                    case "grating":
                        foreach (Extent area in areas)
                            AddGrating(scene, level, area, diagonal);
                        break;
                    case "device":
                        if (!IsRemovedDefaultDeviceLevel(level.Label))
                            devices.Add(level);
                        continue;
                    case "bus":
                        buses.Add(level);
                        continue;
                    default:
                        AddFootprintLines(scene, footprint.BoundaryLines,
                            level.Z, tubeRadius, FrameTint,
                            QuickLine3dSceneGroupKind.Reference);
                        break;
                }
                AddCornerRefLabel(scene, level, LabelExtent(footprint, margin,
                    anchorRight), level.Z, anchorRight,
                    level.Kind == "steel" || level.Kind == "grating"
                        ? QuickLine3dSceneGroupKind.Detail
                        : QuickLine3dSceneGroupKind.Reference, "ref");
            }

            AddDevices(scene, devices, diagonal,
                envelope.UpstreamEndpoint, envelope.DownstreamEndpoint);
            AddBusGlassLevels(scene, buses, areas, footprint, diagonal, margin);
            if (modelTopZ > -baseFloorZ)
                AddGridColumns(scene, footprint, diagonal, -baseFloorZ, modelTopZ);
            AddGroundPlate(scene, areas, LabelExtent(footprint, margin, false),
                diagonal, -baseFloorZ);

            AddFootprintLines(scene, footprint.GridLines, 0,
                Math.Max(diagonal * 0.0006, 8.0), GridTint,
                QuickLine3dSceneGroupKind.Grid);
            AddChannels(scene, envelope, radius);
            AddRoutes(scene, envelope, radius);
            AddLabels(scene, envelope, footprint, diagonal);

            // 取景：参照层高度算进去（否则整栋楼被挤到画面上半区——3.3.7
            // 实测教训原样保留）。
            double refTop = 0.0;
            double refBottom = -baseFloorZ;
            foreach (RefLevel level in levels)
            {
                if (level.Z > refTop) refTop = level.Z;
                if (level.Z < refBottom) refBottom = level.Z;
            }
            double viewMinZ = Math.Min(bounds.Minz, refBottom);
            double viewMaxZ = Math.Max(bounds.Maxz, refTop + diagonal * 0.03);
            double viewDiagonal = Math.Max(diagonal,
                Math.Sqrt(extent.W * extent.W + extent.D * extent.D
                    + (viewMaxZ - viewMinZ) * (viewMaxZ - viewMinZ)));
            scene.Centerx = extent.Centerx;
            scene.Centery = extent.Centery;
            scene.Centerz = (viewMinZ + viewMaxZ) / 2.0;
            scene.Orbitx = -1.0;
            scene.Orbitsy = -1.0;
            scene.Orbitz = 0.57735026918962573;
            scene.CameraDistance = viewDiagonal * 1.6;
            scene.Near = Math.Max(viewDiagonal * 0.001, 0.1);
            scene.Far = viewDiagonal * 100.0;
            scene.Orthographic = false;
            return scene;
        }

        /// <summary>图例/摘要文案（对齐原页面 updateLegend；无参照时省略）。</summary>
        public static string BuildLegend(QuickLineOrbitPreviewEnvelope envelope)
        {
            string legend = "橙X 绿Y 蓝Z　红=桥架(宽) 青=线管";
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeReferenceLevel level
                in envelope.ReferenceLevels)
            {
                kinds.Add(((level != null ? level.Kind : null) ?? "frame")
                    .ToLowerInvariant());
            }
            var parts = new List<string>();
            if (kinds.Contains("floor")) parts.Add("灰=楼板");
            if (kinds.Contains("epoxy")) parts.Add("绿=环氧地坪");
            if (kinds.Contains("waffle")) parts.Add("华夫=圆孔板");
            if (kinds.Contains("steel")) parts.Add("银=钢梁");
            if (kinds.Contains("grating")) parts.Add("架=葡萄架");
            if (kinds.Contains("raised")) parts.Add("蓝灰=高架板");
            if (kinds.Contains("device")) parts.Add("白=设备");
            if (kinds.Contains("bus")) parts.Add("琥珀玻璃=母线层");
            if (kinds.Contains("frame")) parts.Add("黄=标高线");
            if (parts.Count > 0) legend += "　参照: " + string.Join(" ", parts.ToArray());
            if (envelope.AnyTrayWidthAssumedDefault)
                legend += "　桥架宽未标注按 200 显示";
            return legend;
        }

        public static string BuildSummary(QuickLineOrbitPreviewEnvelope envelope)
        {
            string summary = string.IsNullOrEmpty(envelope.AxisSummary)
                ? "" : envelope.AxisSummary + "　";
            if (!envelope.DirectionResolved)
            {
                string reason = envelope.DirectionDiagnostics.Count == 0
                    ? "路线端点或轴位不足，柜体和上下游轴位标记已隐藏。"
                    : string.Join("；", envelope.DirectionDiagnostics);
                summary += "上下游未确认：" + reason + "　";
            }
            return summary
                + "左键拖动=旋转 · 滚轮=缩放 · 右键拖动=平移 · 双击=复位";
        }

        // ---- 参照建筑模型（公式=原页面 addSlab/addEpoxy/addWaffle/…） -----------

        private static void AddSlab(QuickLine3dScene scene, RefLevel level,
            Extent extent, Color tint, Color edgeTint, double thickness,
            double opacity = 0.45)
        {
            scene.AddBox(QuickLine3dSceneGroupKind.Reference,
                extent.Centerx, extent.Centery, level.Z - thickness / 2.0,
                extent.W, extent.D, thickness, tint, opacity, edgeTint);
        }

        private static void AddWafflePlate(QuickLine3dScene scene, RefLevel level,
            QuickLinePreviewFootprint footprint)
        {
            foreach (QuickLinePreviewFootprint.Area bay in footprint.Bays)
            {
                for (int subX = 0; subX < 3; subX++)
                for (int subY = 0; subY < 3; subY++)
                {
                    double cellSize = (bay.MaxX - bay.MinX) / 3.0;
                    double x = bay.MinX + subX * cellSize;
                    double y = bay.MinY + subY * cellSize;
                    scene.AddWaffleCell(QuickLine3dSceneGroupKind.Reference,
                        x, y, x + cellSize, y + cellSize, level.Z,
                        cellSize * 0.36, WaffleTint, 0.42);
                }
            }
        }

        private static void AddBusGlassLevels(QuickLine3dScene scene,
            List<RefLevel> levels, List<Extent> areas,
            QuickLinePreviewFootprint footprint, double diagonal, double margin)
        {
            if (levels.Count == 0) return;
            levels.Sort((a, b) => a.Z.CompareTo(b.Z));
            double thickness = Math.Max(70.0, diagonal * 0.0018);
            int flip = 0;
            foreach (RefLevel level in levels)
            {
                foreach (Extent area in areas)
                    AddSlab(scene, level, area, BusGlassTint,
                        Colors.Transparent, thickness, 0.16);
                bool right = flip++ % 2 == 1;
                AddCornerRefLabel(scene, level, LabelExtent(footprint, margin,
                    right), level.Z, right,
                    QuickLine3dSceneGroupKind.Reference, "ref");
            }
        }

        private static void AddFootprintLines(QuickLine3dScene scene,
            IReadOnlyList<QuickLineColumnGridLine> lines, double z,
            double radius, Color tint, QuickLine3dSceneGroupKind group)
        {
            foreach (QuickLineColumnGridLine line in lines)
            {
                scene.AddTube(group,
                    line.ConstantX ? line.FixedValue : line.Minimum,
                    line.ConstantX ? line.Minimum : line.FixedValue, z,
                    line.ConstantX ? line.FixedValue : line.Maximum,
                    line.ConstantX ? line.Maximum : line.FixedValue, z,
                    radius, tint);
            }
        }

        private static void AddGridColumns(QuickLine3dScene scene,
            QuickLinePreviewFootprint footprint, double diagonal,
            double bottomZ, double topZ)
        {
            double size = Math.Max(350.0, diagonal * 0.014);
            foreach (QuickLineSpatialPoint column in footprint.Columns)
            {
                scene.AddBox(QuickLine3dSceneGroupKind.Columns,
                    column.X, column.Y, (topZ + bottomZ) / 2.0,
                    size, size, topZ - bottomZ, ColumnTint, 0.95,
                    Colors.Transparent);
            }
        }

        private static bool IsRemovedDefaultDeviceLevel(string label)
        {
            switch (label)
            {
                case "I-L盘高度":
                case "I-L盘顶面":
                case "I-Line盘高度":
                case "I-Line盘顶面":
                case "插座盘安装":
                case "插座盘顶面":
                    return true;
                default:
                    return false;
            }
        }

        private static void AddGroundPlate(QuickLine3dScene scene,
            List<Extent> areas, Extent labelExtent, double diagonal, double z)
        {
            double thickness = Math.Max(60.0, diagonal * 0.0015);
            foreach (Extent area in areas)
                scene.AddBox(QuickLine3dSceneGroupKind.Reference,
                    area.Centerx, area.Centery, z - thickness / 2.0,
                    area.W, area.D, thickness, GroundTint, 0.5,
                    Colors.Transparent);
            scene.AddLabel(QuickLine3dSceneGroupKind.Reference, "ref",
                "1F地面 " + Format(z),
                labelExtent.Minx - labelExtent.Margin * 0.35,
                labelExtent.Miny - labelExtent.Margin * 0.35, z);
        }

        private static void AddSteelBeams(QuickLine3dScene scene, RefLevel level,
            Extent extent, double diagonal)
        {
            double z = level.Z;
            double height = Math.Max(400.0, diagonal * 0.014);
            double flange = Math.Max(220.0, height * 0.6);
            double flangeT = Math.Max(55.0, height * 0.18);
            double webT = Math.Max(70.0, height * 0.14);
            int count = ClampInt((int)Math.Round(
                extent.D / (diagonal * 0.16)), 3, 8);
            double centerx = extent.Centerx;
            for (int index = 0; index < count; index++)
            {
                double y = extent.Miny + extent.D * (index + 0.5) / count;
                scene.AddBox(QuickLine3dSceneGroupKind.Detail,
                    centerx, y, z - flangeT / 2.0, extent.W, flange, flangeT,
                    SteelTint, 1.0, SteelTint);
                scene.AddBox(QuickLine3dSceneGroupKind.Detail,
                    centerx, y, z - flangeT - (height - 2 * flangeT) / 2.0,
                    extent.W, webT, height - 2 * flangeT, SteelTint, 1.0,
                    Colors.Transparent);
                scene.AddBox(QuickLine3dSceneGroupKind.Detail,
                    centerx, y, z - height + flangeT / 2.0, extent.W, flange,
                    flangeT, SteelTint, 1.0, Colors.Transparent);
            }
        }

        private static void AddGrating(QuickLine3dScene scene, RefLevel level,
            Extent extent, double diagonal)
        {
            double z = level.Z;
            double barHeight = Math.Max(140.0, diagonal * 0.005);
            double barWidth = Math.Max(110.0, diagonal * 0.004);
            int count = ClampInt((int)Math.Round(
                extent.D / (diagonal * 0.04)), 8, 20);
            double centerx = extent.Centerx;
            for (int index = 0; index < count; index++)
            {
                double y = extent.Miny + extent.D * (index + 0.5) / count;
                scene.AddBox(QuickLine3dSceneGroupKind.Detail,
                    centerx, y, z - barHeight / 2.0, extent.W, barWidth,
                    barHeight, GratingTint, 1.0, Colors.Transparent);
            }
        }

        private static void AddDevices(QuickLine3dScene scene,
            List<RefLevel> levels, double diagonal,
            QuickLineOrbitPreviewEnvelope.EnvelopePoint upstream,
            QuickLineOrbitPreviewEnvelope.EnvelopePoint downstream)
        {
            if (levels.Count == 0 || upstream == null) return;

            double cabinetWidth = Clamp(diagonal * 0.025, 700.0, 1200.0);
            double cabinetDepth = Clamp(cabinetWidth * 0.65, 450.0, 700.0);
            double gap = cabinetWidth * 0.2;
            double directionX = downstream == null
                ? 1.0 : downstream.X - upstream.X;
            double directionY = downstream == null
                ? 0.0 : downstream.Y - upstream.Y;
            double directionLength = Math.Sqrt(directionX * directionX
                + directionY * directionY);
            if (directionLength < 1e-9)
            {
                directionX = 1.0;
                directionY = 0.0;
            }
            else
            {
                directionX /= directionLength;
                directionY /= directionLength;
            }
            double sideX = -directionY;
            double sideY = directionX;
            double connectionRadius = Math.Max(8.0, diagonal * 0.00025);

            for (int index = 0; index < levels.Count; index++)
            {
                RefLevel level = levels[index];
                bool isSocketPanel = level.Label.IndexOf("插座盘",
                    StringComparison.Ordinal) >= 0;
                double height = isSocketPanel ? 1000.0
                    : Math.Max(level.Z, 600.0);
                double bottom = isSocketPanel
                    ? level.Z : 0.0;
                double connectionZ = isSocketPanel ? level.Z : bottom + height;
                double sideOffset = (index - (levels.Count - 1) / 2.0)
                    * (cabinetWidth + gap);
                double x = upstream.X - directionX * cabinetDepth / 2.0
                    + sideX * sideOffset;
                double y = upstream.Y - directionY * cabinetDepth / 2.0
                    + sideY * sideOffset;
                double portX = x + directionX * cabinetDepth / 2.0;
                double portY = y + directionY * cabinetDepth / 2.0;
                scene.AddBox(QuickLine3dSceneGroupKind.Reference,
                    x, y, bottom + height / 2.0,
                    cabinetWidth, cabinetDepth, height, DeviceTint,
                    1.0, DeviceEdge);
                scene.AddTube(QuickLine3dSceneGroupKind.Reference,
                    upstream.X, upstream.Y, upstream.Z,
                    portX, portY, connectionZ, connectionRadius, PinTint);
                scene.AddLabel(QuickLine3dSceneGroupKind.Reference, "ref",
                    (isSocketPanel ? "插座盘安装" : level.Label) + " "
                        + Format(level.Z),
                    x, y, bottom + height + diagonal * 0.014);
            }
        }

        private static Extent LabelExtent(QuickLinePreviewFootprint footprint,
            double margin, bool right)
        {
            QuickLinePreviewFootprint.Area bay = footprint.Bays[0];
            if (right)
                bay = footprint.Bays.Last(candidate => candidate.MinY == bay.MinY);
            return new Extent(bay.MinX, bay.MinY, bay.MaxX, bay.MaxY, margin);
        }

        private static void AddCornerRefLabel(QuickLine3dScene scene,
            RefLevel level, Extent extent, double z, bool anchorRight,
            QuickLine3dSceneGroupKind group, string kind)
        {
            scene.AddLabel(group, kind, level.Label + " " + Format(level.Z),
                anchorRight ? extent.Maxx + extent.Margin * 0.2
                    : extent.Minx - extent.Margin * 0.35,
                extent.Miny - extent.Margin * 0.35, z);
        }

        // ---- 路由 / 通道 / 柱网 / 标签 -------------------------------------------

        private static void AddChannels(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope, double radius)
        {
            double railRadius = radius * 0.7;
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeSegment segment
                in envelope.Segments)
            {
                QuickLineOrbitPreviewEnvelope.EnvelopeChannel channel =
                    segment.Channel;
                if (channel == null) continue;
                if (string.Equals(channel.Kind, "tray",
                    StringComparison.OrdinalIgnoreCase))
                {
                    scene.AddQuad(QuickLine3dSceneGroupKind.Routes,
                        channel.A1.X, channel.A1.Y, channel.A1.Z,
                        channel.A2.X, channel.A2.Y, channel.A2.Z,
                        channel.B2.X, channel.B2.Y, channel.B2.Z,
                        channel.B1.X, channel.B1.Y, channel.B1.Z,
                        TrayTint, 0.3);
                    AddTintedTube(scene, channel.A1, channel.A2, TrayTint,
                        railRadius);
                    AddTintedTube(scene, channel.B1, channel.B2, TrayTint,
                        railRadius);
                }
                else if (string.Equals(channel.Kind, "conduit",
                    StringComparison.OrdinalIgnoreCase))
                {
                    AddTintedTube(scene, channel.A1, channel.A2, ConduitTint,
                        railRadius * 0.8);
                    AddTintedTube(scene, channel.B1, channel.B2, ConduitTint,
                        railRadius * 0.8);
                }
            }
        }

        private static void AddRoutes(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope, double radius)
        {
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeSegment segment
                in envelope.Segments)
            {
                Color tint = segment.Axis == "X" ? AxisX
                    : segment.Axis == "Y" ? AxisY : AxisZ;
                AddTintedTube(scene, segment.Start, segment.End, tint, radius);
            }
        }

        private static void AddTintedTube(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope.EnvelopePoint a,
            QuickLineOrbitPreviewEnvelope.EnvelopePoint b, Color tint,
            double radius)
        {
            scene.AddTube(QuickLine3dSceneGroupKind.Routes,
                a.X, a.Y, a.Z, b.X, b.Y, b.Z, radius, tint);
        }

        private static void AddLabels(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope,
            QuickLinePreviewFootprint footprint, double diagonal)
        {
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeLabel label
                in envelope.SegmentLabels)
                scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "mm",
                    label.Text, label.X, label.Y, label.Z);
            foreach (var label in footprint.AxisLabels(envelope))
                scene.AddLabel(QuickLine3dSceneGroupKind.Grid, "axis",
                    label.Text, label.X, label.Y, label.Z);
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopePin pin
                in envelope.EndpointPins)
            {
                scene.AddTube(QuickLine3dSceneGroupKind.Routes,
                    pin.X, pin.Y, pin.Z, pin.X, pin.Y, pin.TipZ,
                    diagonal * 0.0004, PinTint);
                scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "pin",
                    pin.Text, pin.X, pin.Y, pin.LabelZ);
            }
        }

        private static Bounds MeasureBounds(
            QuickLineOrbitPreviewEnvelope envelope)
        {
            double minx = double.MaxValue, miny = double.MaxValue;
            double minz = double.MaxValue;
            double maxx = double.MinValue, maxy = double.MinValue;
            double maxz = double.MinValue;
            Action<QuickLineOrbitPreviewEnvelope.EnvelopePoint> feed = point =>
            {
                minx = Math.Min(minx, point.X);
                maxx = Math.Max(maxx, point.X);
                miny = Math.Min(miny, point.Y);
                maxy = Math.Max(maxy, point.Y);
                minz = Math.Min(minz, point.Z);
                maxz = Math.Max(maxz, point.Z);
            };
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeSegment segment
                in envelope.Segments)
            {
                feed(segment.Start);
                feed(segment.End);
            }
            if (minx > maxx)
            {
                minx = miny = minz = 0.0;
                maxx = maxy = maxz = 1000.0;
            }
            return new Bounds(minx, miny, minz, maxx, maxy, maxz);
        }

        private static double Clamp(double value, double low, double high)
        {
            return value < low ? low : (value > high ? high : value);
        }

        private static int ClampInt(int value, int low, int high)
        {
            return value < low ? low : (value > high ? high : value);
        }

        private static string Format(double value)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        private static Color FromRgb(byte r, byte g, byte b)
        {
            return Color.FromRgb(r, g, b);
        }

        private sealed class RefLevel
        {
            internal RefLevel(string label, double z, string kind)
            {
                Label = label;
                Z = z;
                Kind = kind;
            }

            internal string Label { get; }
            internal double Z { get; }
            internal string Kind { get; }
        }

        private sealed class Extent
        {
            internal Extent(double minx, double miny, double maxx, double maxy,
                double margin)
            {
                Minx = minx;
                Miny = miny;
                Maxx = maxx;
                Maxy = maxy;
                Margin = margin;
                W = maxx - minx;
                D = maxy - miny;
                Centerx = (minx + maxx) / 2.0;
                Centery = (miny + maxy) / 2.0;
            }

            internal double Minx { get; }
            internal double Miny { get; }
            internal double Maxx { get; }
            internal double Maxy { get; }
            internal double Margin { get; }
            internal double W { get; }
            internal double D { get; }
            internal double Centerx { get; }
            internal double Centery { get; }
        }

        private sealed class Bounds
        {
            internal Bounds(double minx, double miny, double minz,
                double maxx, double maxy, double maxz)
            {
                Minx = minx;
                Miny = miny;
                Minz = minz;
                Maxx = maxx;
                Maxy = maxy;
                Maxz = maxz;
                Centerx = (minx + maxx) / 2.0;
                Centery = (miny + maxy) / 2.0;
            }

            internal double Minx { get; }
            internal double Miny { get; }
            internal double Minz { get; }
            internal double Maxx { get; }
            internal double Maxy { get; }
            internal double Maxz { get; }
            internal double Centerx { get; }
            internal double Centery { get; }
        }
    }
}
