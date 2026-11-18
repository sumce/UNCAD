using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using UNCAD.Core.QuickLine;

namespace UNCAD.UI
{
    /// <summary>
    /// 3.4.0：U1U 渲染信封（QuickLineOrbitPreviewEnvelope）→ 原生场景
    /// （QuickLine3dScene）。视觉公式与 3.3.5~3.3.8 的 three.js 页面
    /// （Web/QuickLineOrbit/app.js，已退役）逐条对齐：管线半径 0.0019×对角
    /// 线、桥架红双线+0.3 半透明填充带、线管青双线、参照建筑模型全套
    /// （楼板/环氧/华夫圆孔板/工字钢/葡萄架/设备盒/母线玻璃/结构柱/地面）
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

            Extent extent = HorizontalExtent(envelope, bounds, diagonal * 0.06);
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
                        AddSlab(scene, level, extent, diagonal, FloorTint,
                            FloorEdge, Math.Max(220.0, diagonal * 0.005),
                            anchorRight);
                        break;
                    case "epoxy":
                        AddSlab(scene, level, extent, diagonal, EpoxyTint,
                            EpoxyEdge, Math.Max(180.0, diagonal * 0.004),
                            anchorRight);
                        break;
                    case "waffle":
                        AddWafflePlate(scene, level, extent, diagonal,
                            anchorRight, envelope);
                        break;
                    case "raised":
                        AddSlab(scene, level, extent, diagonal, RaisedTint,
                            FloorEdge, Math.Max(140.0, diagonal * 0.003),
                            anchorRight);
                        break;
                    case "steel":
                        AddSteelBeams(scene, level, extent, diagonal, anchorRight);
                        break;
                    case "grating":
                        AddGrating(scene, level, extent, diagonal, anchorRight);
                        break;
                    case "device":
                        devices.Add(level);
                        continue;
                    case "bus":
                        buses.Add(level);
                        continue;
                    default:
                        AddRefFrame(scene, level, extent, tubeRadius);
                        break;
                }
            }

            AddDevices(scene, devices, extent, diagonal);
            AddBusGlassLevels(scene, buses, extent, diagonal);
            if (modelTopZ > 0.0)
                AddGridColumns(scene, envelope, extent, diagonal, modelTopZ);
            if (baseFloorZ == 0.0) AddGroundPlate(scene, extent, diagonal);

            AddGridLines(scene, envelope, diagonal);
            AddChannels(scene, envelope, radius);
            AddRoutes(scene, envelope, radius);
            AddLabels(scene, envelope, diagonal);

            // 取景：参照层高度算进去（否则整栋楼被挤到画面上半区——3.3.7
            // 实测教训原样保留）。
            double refTop = 0.0;
            double refBottom = 0.0;
            foreach (RefLevel level in levels)
            {
                if (level.Z > refTop) refTop = level.Z;
                if (level.Z < refBottom) refBottom = level.Z;
            }
            double viewMinZ = Math.Min(bounds.Minz, refBottom);
            double viewMaxZ = Math.Max(bounds.Maxz, refTop + diagonal * 0.03);
            double viewDiagonal = Math.Max(diagonal,
                Math.Sqrt(diagonal * diagonal
                    + (viewMaxZ - viewMinZ) * (viewMaxZ - viewMinZ)));
            scene.Centerx = bounds.Centerx;
            scene.Centery = bounds.Centery;
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
            return (string.IsNullOrEmpty(envelope.AxisSummary)
                    ? "" : envelope.AxisSummary + "　")
                + "左键拖动=旋转 · 滚轮=缩放 · 右键拖动=平移 · 双击=复位";
        }

        // ---- 参照建筑模型（公式=原页面 addSlab/addEpoxy/addWaffle/…） -----------

        private static void AddSlab(QuickLine3dScene scene, RefLevel level,
            Extent extent, double diagonal, Color tint, Color edgeTint,
            double thickness, bool anchorRight)
        {
            scene.AddBox(QuickLine3dSceneGroupKind.Reference,
                extent.Centerx, extent.Centery, level.Z - thickness / 2.0,
                extent.W, extent.D, thickness, tint, 0.45, edgeTint);
            AddCornerRefLabel(scene, level, extent, level.Z, anchorRight,
                QuickLine3dSceneGroupKind.Reference, "ref");
        }

        private static void AddWafflePlate(QuickLine3dScene scene, RefLevel level,
            Extent extent, double diagonal, bool anchorRight,
            QuickLineOrbitPreviewEnvelope envelope)
        {
            // 真圆孔穿透板：网格采样铺设顶面（孔内格心跳过）——对齐原页面
            // ShapeGeometry 挖孔观感。孔位：柱网分间每格 3×3。
            GridAxisSet axes = CollectGridAxes(envelope, diagonal);
            double cell = Math.Max(diagonal * 0.0018, 40.0);
            var holes = new List<double[]>();   // [cx, cy, r]
            if (axes.Xs.Count >= 2 && axes.Ys.Count >= 2)
            {
                for (int i = 0; i + 1 < axes.Xs.Count; i++)
                {
                    double x0 = Math.Max(axes.Xs[i], extent.Minx);
                    double x1 = Math.Min(axes.Xs[i + 1], extent.Maxx);
                    for (int j = 0; j + 1 < axes.Ys.Count; j++)
                    {
                        double y0 = Math.Max(axes.Ys[j], extent.Miny);
                        double y1 = Math.Min(axes.Ys[j + 1], extent.Maxy);
                        double spanX = x1 - x0;
                        double spanY = y1 - y0;
                        if (spanX <= 0 || spanY <= 0) continue;
                        double holeRadius = Math.Min(spanX, spanY) / 3.0
                            * 0.5 * 0.72;
                        for (int a = 0; a < 3; a++)
                        {
                            for (int b = 0; b < 3; b++)
                            {
                                holes.Add(new[]
                                {
                                    x0 + spanX * (a + 0.5) / 3.0,
                                    y0 + spanY * (b + 0.5) / 3.0,
                                    holeRadius
                                });
                            }
                        }
                    }
                }
            }
            else
            {
                double span = Math.Min(extent.W, extent.D);
                double pitch = Clamp(span / 7.0, 900.0, 2400.0);
                double holeRadius = pitch * 0.36;
                double inset = pitch * 0.8;
                for (double cx = extent.Minx + inset; cx <= extent.Maxx - inset;
                    cx += pitch)
                {
                    for (double cy = extent.Miny + inset;
                        cy <= extent.Maxy - inset; cy += pitch)
                    {
                        holes.Add(new[] { cx, cy, holeRadius });
                    }
                }
            }
            double z = level.Z;
            int cells = 0;
            for (double x = extent.Minx; x < extent.Maxx; x += cell)
            {
                for (double y = extent.Miny; y < extent.Maxy; y += cell)
                {
                    double x2 = Math.Min(x + cell, extent.Maxx);
                    double y2 = Math.Min(y + cell, extent.Maxy);
                    double mx = (x + x2) / 2.0;
                    double my = (y + y2) / 2.0;
                    if (InAnyHole(holes, mx, my)) continue;
                    scene.AddQuad(QuickLine3dSceneGroupKind.Reference,
                        x, y, z, x2, y, z, x2, y2, z, x, y2, z,
                        WaffleTint, 0.42);
                    cells++;
                    if (cells > 60000) break;
                }
                if (cells > 60000) break;
            }
            AddCornerRefLabel(scene, level, extent, z, anchorRight,
                QuickLine3dSceneGroupKind.Reference, "ref");
        }

        private static bool InAnyHole(List<double[]> holes, double x, double y)
        {
            foreach (double[] hole in holes)
            {
                double dx = x - hole[0];
                double dy = y - hole[1];
                if (dx * dx + dy * dy <= hole[2] * hole[2]) return true;
            }
            return false;
        }

        private static void AddBusGlassLevels(QuickLine3dScene scene,
            List<RefLevel> levels, Extent extent, double diagonal)
        {
            if (levels.Count == 0) return;
            levels.Sort((a, b) => a.Z.CompareTo(b.Z));
            double thickness = Math.Max(70.0, diagonal * 0.0018);
            int flip = 0;
            foreach (RefLevel level in levels)
            {
                scene.AddBox(QuickLine3dSceneGroupKind.Reference,
                    extent.Centerx, extent.Centery, level.Z - thickness / 2.0,
                    extent.W, extent.D, thickness, BusGlassTint, 0.16,
                    Colors.Transparent);
                AddCornerRefLabel(scene, level, extent, level.Z,
                    flip++ % 2 == 1, QuickLine3dSceneGroupKind.Reference, "ref");
            }
        }

        private static void AddGridLines(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope, double diagonal)
        {
            double radius = Math.Max(diagonal * 0.0006, 8.0);
            // 原页面 LineSegments 1px 线，原生用细条等价（扁平端点对 a→b）。
            for (int index = 0; index + 1 < envelope.ColumnGridLines.Count;
                index += 2)
            {
                QuickLineOrbitPreviewEnvelope.EnvelopePoint a =
                    envelope.ColumnGridLines[index];
                QuickLineOrbitPreviewEnvelope.EnvelopePoint b =
                    envelope.ColumnGridLines[index + 1];
                if (a == null || b == null) continue;
                scene.AddTube(QuickLine3dSceneGroupKind.Grid,
                    a.X, a.Y, a.Z, b.X, b.Y, b.Z, radius, GridTint);
            }
        }

        private static void AddGridColumns(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope, Extent extent,
            double diagonal, double topZ)
        {
            GridAxisSet axes = CollectGridAxes(envelope, diagonal);
            if (axes.Xs.Count == 0 || axes.Ys.Count == 0) return;
            double size = Math.Max(350.0, diagonal * 0.014);
            int count = 0;
            foreach (double x in axes.Xs)
            {
                foreach (double y in axes.Ys)
                {
                    if (x < extent.Minx - axes.Eps || x > extent.Maxx + axes.Eps
                        || y < extent.Miny - axes.Eps || y > extent.Maxy + axes.Eps)
                        continue;
                    if (++count > 400) return;
                    scene.AddBox(QuickLine3dSceneGroupKind.Reference,
                        x, y, topZ / 2.0, size, size, topZ, ColumnTint, 0.95,
                        Colors.Transparent);
                }
            }
        }

        private static void AddGroundPlate(QuickLine3dScene scene,
            Extent extent, double diagonal)
        {
            double thickness = Math.Max(60.0, diagonal * 0.0015);
            scene.AddBox(QuickLine3dSceneGroupKind.Reference,
                extent.Centerx, extent.Centery, -thickness / 2.0,
                extent.W, extent.D, thickness, GroundTint, 0.5,
                Colors.Transparent);
            scene.AddLabel(QuickLine3dSceneGroupKind.Reference, "ref",
                "1F地面 0",
                extent.Minx - extent.Margin * 0.35,
                extent.Miny - extent.Margin * 0.35, 0.0);
        }

        private static void AddSteelBeams(QuickLine3dScene scene, RefLevel level,
            Extent extent, double diagonal, bool anchorRight)
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
            AddCornerRefLabel(scene, level, extent, z, anchorRight,
                QuickLine3dSceneGroupKind.Detail, "ref");
        }

        private static void AddGrating(QuickLine3dScene scene, RefLevel level,
            Extent extent, double diagonal, bool anchorRight)
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
            AddCornerRefLabel(scene, level, extent, z, anchorRight,
                QuickLine3dSceneGroupKind.Detail, "ref");
        }

        private static void AddDevices(QuickLine3dScene scene,
            List<RefLevel> levels, Extent extent, double diagonal)
        {
            if (levels.Count == 0) return;
            double footX = Math.Max(1000.0, diagonal * 0.042);
            double footY = footX * 0.7;
            for (int index = 0; index < levels.Count; index++)
            {
                RefLevel level = levels[index];
                double x = extent.Minx + extent.W * (index + 1)
                    / (levels.Count + 1);
                double y = extent.Miny + extent.Margin * 0.22 + footY / 2.0;
                scene.AddBox(QuickLine3dSceneGroupKind.Reference,
                    x, y, level.Z / 2.0, footX, footY, level.Z, DeviceTint,
                    1.0, DeviceEdge);
                scene.AddLabel(QuickLine3dSceneGroupKind.Reference, "ref",
                    level.Label + " " + Format(level.Z),
                    x, y, level.Z + diagonal * 0.014);
            }
        }

        private static void AddRefFrame(QuickLine3dScene scene, RefLevel level,
            Extent extent, double tubeRadius)
        {
            double z = level.Z;
            scene.AddRectOutline(QuickLine3dSceneGroupKind.Reference,
                extent.Minx, extent.Miny, extent.Maxx, extent.Maxy, z,
                FrameTint, tubeRadius);
            scene.AddLabel(QuickLine3dSceneGroupKind.Reference, "ref",
                level.Label + " " + Format(level.Z),
                extent.Minx - extent.Margin * 0.35,
                extent.Miny - extent.Margin * 0.35, z);
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
            QuickLineOrbitPreviewEnvelope envelope, double diagonal)
        {
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeLabel label
                in envelope.SegmentLabels)
                scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "mm",
                    label.Text, label.X, label.Y, label.Z);
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeLabel tick
                in envelope.NumericTicks)
                scene.AddLabel(QuickLine3dSceneGroupKind.Grid, "axis",
                    tick.Text, tick.X, tick.Y, tick.Z);
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeLabel letter
                in envelope.LetterLabels)
                scene.AddLabel(QuickLine3dSceneGroupKind.Grid, "axis",
                    letter.Text, letter.X, letter.Y, letter.Z);
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

        // ---- 包络 / 柱网推算（对齐原页面 horizontalExtent/gridAxes） -------------

        private static GridAxisSet CollectGridAxes(
            QuickLineOrbitPreviewEnvelope envelope, double diagonal)
        {
            double eps = Math.Max(diagonal * 0.0005, 1.0);
            var xs = new List<double>();
            var ys = new List<double>();
            for (int index = 0; index + 1 < envelope.ColumnGridLines.Count;
                index += 2)
            {
                QuickLineOrbitPreviewEnvelope.EnvelopePoint a =
                    envelope.ColumnGridLines[index];
                QuickLineOrbitPreviewEnvelope.EnvelopePoint b =
                    envelope.ColumnGridLines[index + 1];
                if (a == null || b == null) continue;
                if (Math.Abs(a.X - b.X) < eps) PushUnique(xs, a.X, eps);
                else if (Math.Abs(a.Y - b.Y) < eps) PushUnique(ys, a.Y, eps);
            }
            xs.Sort();
            ys.Sort();
            return new GridAxisSet(xs, ys, eps);
        }

        private static void PushUnique(List<double> values, double value,
            double eps)
        {
            foreach (double existing in values)
            {
                if (Math.Abs(existing - value) < eps) return;
            }
            values.Add(value);
        }

        private static Extent HorizontalExtent(
            QuickLineOrbitPreviewEnvelope envelope, Bounds bounds,
            double margin)
        {
            var axes = CollectGridAxes(envelope, margin / 0.06);
            if (axes.Xs.Count >= 2 && axes.Ys.Count >= 2)
            {
                double bayX = MaxGap(axes.Xs);
                if (bayX <= 0) bayX = margin;
                double bayY = MaxGap(axes.Ys);
                if (bayY <= 0) bayY = margin;
                double minx, maxx, miny, maxy;
                SnapOut(axes.Xs, bounds.Minx, bounds.Maxx, bayX,
                    out minx, out maxx);
                SnapOut(axes.Ys, bounds.Miny, bounds.Maxy, bayY,
                    out miny, out maxy);
                return new Extent(minx, miny, maxx, maxy, margin);
            }
            return new Extent(bounds.Minx - margin, bounds.Miny - margin,
                bounds.Maxx + margin, bounds.Maxy + margin, margin);
        }

        private static double MaxGap(List<double> values)
        {
            double best = 0.0;
            for (int index = 1; index < values.Count; index++)
            {
                best = Math.Max(best, values[index] - values[index - 1]);
            }
            return best;
        }

        private static void SnapOut(List<double> values, double lo, double hi,
            double bay, out double low, out double high)
        {
            double below = values[0];
            double above = values[values.Count - 1];
            foreach (double value in values)
            {
                if (value <= lo) below = value;
                if (value >= hi && above > value) above = value;
            }
            low = Math.Min(below - bay, lo - bay);
            high = Math.Max(above + bay, hi + bay);
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

        private sealed class GridAxisSet
        {
            internal GridAxisSet(List<double> xs, List<double> ys, double eps)
            {
                Xs = xs;
                Ys = ys;
                Eps = eps;
            }

            internal List<double> Xs { get; }
            internal List<double> Ys { get; }
            internal double Eps { get; }
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
