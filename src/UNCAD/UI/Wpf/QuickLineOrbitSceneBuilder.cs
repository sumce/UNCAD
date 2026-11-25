using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using UNCAD.Core.QuickLine;

namespace UNCAD.UI
{
    /// <summary>
    /// 3.8.0（D-059）重写：U1U 预览信封（QuickLineOrbitPreviewEnvelope）→ 原生场景
    /// （QuickLine3dScene）。场景里只有四样东西：
    ///  - 楼层平面：台账上下游楼层之间的各层，只有边框 + 柱网线（不画面——半透明面
    ///    在 WPF 里照样写深度，会把楼层另一侧的电缆、标签挡住；线框从任何角度看
    ///    都看得见线路）；
    ///  - 轴位：图签式边标（数字、字母各贴一条外边）；
    ///  - 线路：电缆管 + 桥架/线管通道 + 软管，整趟标签；
    ///  - 上游柜（I-Line 盘落地 / 插座盘离地 1 m / 母线插接口）与两端标记。
    /// 3.3.7~3.4.0 的参照建筑模型（华夫板、工字钢、葡萄架、柱子、母线玻璃层、
    /// 地面板）整体去掉——它们把线路埋在里面，而且三角形多、半透明材质多，是
    /// 旋转卡顿的根源。
    /// </summary>
    internal static class QuickLineOrbitSceneBuilder
    {
        // 3.8.0.7：电缆就是一色（现场「你加XYZ干嘛」）——不再按等测轴 X/Y/Z 换色。
        private static readonly Color RouteTint = FromRgb(0xE0, 0x6C, 0x00);
        private static readonly Color GridTint = FromRgb(0x3A, 0x46, 0x56);
        private static readonly Color FloorEdge = FromRgb(0x8F, 0xA4, 0xBA);
        private static readonly Color TrayTint = FromRgb(0xF2, 0x57, 0x4C);
        private static readonly Color ConduitTint = FromRgb(0x2E, 0xA8, 0xC0);
        private static readonly Color HoseTint = FromRgb(0x5F, 0xC9, 0x8A);
        private static readonly Color CabinetTint = FromRgb(0xC9, 0xD3, 0xDE);
        private static readonly Color CabinetEdge = FromRgb(0x5D, 0x69, 0x77);
        private static readonly Color UpstreamEndTint = FromRgb(0xE0, 0xB0, 0x64);
        private static readonly Color DeviceEndTint = FromRgb(0x63, 0xC9, 0xA5);

        private static readonly Regex MillimetreText = new Regex(
            @"^\s*(\d+(?:\.\d+)?)\s*mm\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>构建场景（envelope 为 null 时返回 null——空态由宿主提示）。</summary>
        public static QuickLine3dScene Build(QuickLineOrbitPreviewEnvelope envelope)
        {
            if (envelope == null) return null;
            var scene = new QuickLine3dScene();
            double diagonal = Math.Max(envelope.Diagonal, 1.0);
            scene.Diagonal = diagonal;

            // 包围盒：楼层平面 + 柜顶 + 软管都框进来。
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            double minZ = 0.0, maxZ = 0.0;
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeSegment segment in envelope.Segments)
            {
                Feed(segment.Start);
                Feed(segment.End);
            }
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeFloor floor in envelope.Floors)
            {
                minX = Math.Min(minX, floor.MinX);
                minY = Math.Min(minY, floor.MinY);
                maxX = Math.Max(maxX, floor.MaxX);
                maxY = Math.Max(maxY, floor.MaxY);
                minZ = Math.Min(minZ, floor.Z);
                maxZ = Math.Max(maxZ, floor.Z);
            }
            if (envelope.Cabinet != null)
                maxZ = Math.Max(maxZ, envelope.Cabinet.TopZ);
            if (envelope.Hose != null) Feed(envelope.Hose.End);
            if (minX > maxX)
            {
                minX = minY = 0.0;
                maxX = maxY = 1000.0;
            }
            double width = Math.Max(maxX - minX, 1.0);
            double depth = Math.Max(maxY - minY, 1.0);
            double height = Math.Max(maxZ - minZ, 1.0);
            double viewDiagonal = Math.Sqrt(width * width + depth * depth + height * height);

            // 线宽按「屏幕像素」反推：按 700 px 宽的窗格、初始取景距离估算每像素
            // 多少毫米，保证网格线/边框/管线在屏幕上至少有 1.5~3 px 粗——按场景
            // 对角线的比例算，图幅一小就不足一个像素，看着像虚线。
            double millimetresPerPixel = viewDiagonal * 1.5 / 700.0;
            double radius = Math.Max(diagonal * 0.0034, millimetresPerPixel * 1.7);
            double labelHeight = diagonal * 0.016;

            AddFloors(scene, envelope, radius, millimetresPerPixel);
            AddChannels(scene, envelope, radius);
            AddRoutes(scene, envelope, radius);
            AddHose(scene, envelope, radius);
            AddCabinet(scene, envelope, radius, millimetresPerPixel);
            AddEndMarkers(scene, envelope, radius, millimetresPerPixel);
            AddLabels(scene, envelope, radius, labelHeight);

            // 取景：默认从 −X−Y 方向、约 35° 俯视；中心略抬高，画面不被底部
            // 识别摘要面板压住。
            scene.Centerx = (minX + maxX) / 2.0;
            scene.Centery = (minY + maxY) / 2.0;
            scene.Centerz = (minZ + maxZ) / 2.0 + height * 0.12;
            scene.Orbitx = -1.0;
            scene.Orbitsy = -1.0;
            scene.Orbitz = 1.0;
            scene.CameraDistance = viewDiagonal * 1.3;
            scene.Near = Math.Max(viewDiagonal * 0.001, 0.1);
            scene.Far = viewDiagonal * 100.0;
            scene.Orthographic = false;
            return scene;

            void Feed(QuickLineOrbitPreviewEnvelope.EnvelopePoint point)
            {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxZ = Math.Max(maxZ, point.Z);
            }
        }

        /// <summary>图例/摘要文案。</summary>
        public static string BuildLegend(QuickLineOrbitPreviewEnvelope envelope)
        {
            string legend = "橙=电缆　红=桥架 青=线管";
            if (envelope.Hose != null) legend += "　绿管=软管(示意)";
            if (envelope.Floors.Count > 0) legend += "　灰框=楼层平面";
            if (envelope.DirectionResolved && envelope.UpstreamEndpoint != null
                && envelope.DownstreamEndpoint != null)
                legend += "　琥珀=上游端 绿=设备端";
            if (envelope.AnyTrayWidthAssumedDefault)
                legend += "　桥架宽未标注按 200 显示";
            return legend;
        }

        /// <summary>D-058：摘要＝线路识别结果 + 轴位摘要 + 未能确认方向的原因。</summary>
        public static string BuildSummary(QuickLineOrbitPreviewEnvelope envelope)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(envelope.RecognitionSummary))
                parts.Add(envelope.RecognitionSummary);
            if (!string.IsNullOrEmpty(envelope.AxisSummary))
                parts.Add(envelope.AxisSummary);
            if (!envelope.DirectionResolved)
            {
                string reason = envelope.DirectionDiagnostics.Count == 0
                    ? "路线端点或轴位不足，柜体和上下游轴位标记已隐藏。"
                    : string.Join("；", envelope.DirectionDiagnostics);
                parts.Add("上下游未确认：" + reason);
            }
            return string.Join("　", parts);
        }

        /// <summary>鼠标操作提示（窗格图例行末尾）。</summary>
        public const string InteractionHint =
            "左键拖动=旋转 · 滚轮=缩放 · 右键拖动=平移 · 双击=复位";

        /// <summary>「14400mm」→「14.4 m」；不是纯毫米数的文字原样返回。</summary>
        internal static string MetricText(string text)
        {
            Match match = MillimetreText.Match(text ?? string.Empty);
            if (!match.Success
                || !double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double millimetres)) return text;
            return (millimetres / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + " m";
        }

        // ---- 楼层平面 ------------------------------------------------------------

        private static void AddFloors(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope, double radius,
            double millimetresPerPixel)
        {
            // 线宽取「对角线比例」与「屏幕像素下限」里较粗的那个。
            double edgeRadius = Math.Max(radius * 0.6, millimetresPerPixel * 1.1);
            double gridRadius = Math.Max(radius * 0.36, millimetresPerPixel * 1.1);
            // 只在最低层和最高层画柱网（中间层只画边框，免得线叠线）。
            for (int index = 0; index < envelope.Floors.Count; index++)
            {
                QuickLineOrbitPreviewEnvelope.EnvelopeFloor floor = envelope.Floors[index];
                bool withGrid = index == 0 || index == envelope.Floors.Count - 1;
                // 3.8.0.1：楼层只画边框和柱网，不画半透明面——WPF 的透明材质也写
                // 深度缓冲，从楼上看会把另一侧的电缆/标签整片挡住（用户现场：
                // 跨层路线「穿过楼层就看不到电缆」）。
                scene.AddRectOutline(QuickLine3dSceneGroupKind.Reference,
                    floor.MinX, floor.MinY, floor.MaxX, floor.MaxY, floor.Z,
                    FloorEdge, edgeRadius);
                if (!withGrid) continue;
                for (int line = 0; line + 1 < floor.GridLines.Count; line += 2)
                {
                    QuickLineOrbitPreviewEnvelope.EnvelopePoint a = floor.GridLines[line];
                    QuickLineOrbitPreviewEnvelope.EnvelopePoint b = floor.GridLines[line + 1];
                    scene.AddTube(QuickLine3dSceneGroupKind.Grid,
                        a.X, a.Y, a.Z, b.X, b.Y, b.Z, gridRadius, GridTint);
                }
            }
        }

        // ---- 路由 / 通道 / 软管 / 柜子 -------------------------------------------

        private static void AddChannels(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope, double radius)
        {
            double railRadius = radius * 0.62;
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
                        TrayTint, 0.30);
                    AddTintedTube(scene, channel.A1, channel.A2, TrayTint, railRadius);
                    AddTintedTube(scene, channel.B1, channel.B2, TrayTint, railRadius);
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
                Color tint = RouteTint;
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

        /// <summary>D-058：软管——从设备端沿末段方向伸出的绿管（长度＝软管毫米）。</summary>
        private static void AddHose(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope, double radius)
        {
            QuickLineOrbitPreviewEnvelope.EnvelopeHose hose = envelope.Hose;
            if (hose == null) return;
            AddTintedTube(scene, hose.Start, hose.End, HoseTint, radius * 1.1);
        }

        /// <summary>
        /// D-059：上游柜。盒子 + 一根从路线端点接到柜面的短连线（端点段是立管时，
        /// 连线接在柜顶附近，表示电缆从柜顶进立管）。
        /// </summary>
        private static void AddCabinet(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope, double radius,
            double millimetresPerPixel)
        {
            QuickLineOrbitPreviewEnvelope.EnvelopeCabinet cabinet = envelope.Cabinet;
            if (cabinet == null) return;
            scene.AddBox(QuickLine3dSceneGroupKind.Reference,
                cabinet.CenterX, cabinet.CenterY, cabinet.BaseZ + cabinet.Height / 2.0,
                cabinet.SizeX, cabinet.SizeY, cabinet.Height,
                CabinetTint, 0.96, CabinetEdge);
            // 3.8.0.1：插座盘底边离地 1 m——只画盒子时现场看着像贴在地面。
            // 从楼层地面到盒底画一根细支架，离地的高度一眼能看出来。
            if (cabinet.Kind == PreviewCabinetKind.SocketPanel
                && cabinet.MountingFloorZ.HasValue
                && cabinet.BaseZ - cabinet.MountingFloorZ.Value > 1.0)
            {
                scene.AddTube(QuickLine3dSceneGroupKind.Reference,
                    cabinet.CenterX, cabinet.CenterY, cabinet.MountingFloorZ.Value,
                    cabinet.CenterX, cabinet.CenterY, cabinet.BaseZ,
                    Math.Max(radius * 0.4, millimetresPerPixel), CabinetTint);
            }
            QuickLineOrbitPreviewEnvelope.EnvelopePoint connection = cabinet.Connection;
            double halfX = cabinet.SizeX / 2.0;
            double halfY = cabinet.SizeY / 2.0;
            double reachX = Math.Abs(connection.X - cabinet.CenterX)
                / Math.Max(halfX, 1.0);
            double reachY = Math.Abs(connection.Y - cabinet.CenterY)
                / Math.Max(halfY, 1.0);
            double faceX = cabinet.CenterX;
            double faceY = cabinet.CenterY;
            if (reachX >= reachY)
                faceX += connection.X >= cabinet.CenterX ? halfX : -halfX;
            else
                faceY += connection.Y >= cabinet.CenterY ? halfY : -halfY;
            double linkZ = Math.Max(cabinet.BaseZ + cabinet.Height * 0.15,
                Math.Min(cabinet.TopZ - cabinet.Height * 0.15, connection.Z));
            scene.AddTube(QuickLine3dSceneGroupKind.Reference,
                connection.X, connection.Y, linkZ, faceX, faceY, linkZ,
                Math.Max(radius * 0.5, millimetresPerPixel * 1.2), CabinetEdge);
        }

        /// <summary>D-058：两端标记——琥珀球＝上游端，绿球＝设备端（两端确认后才画）。</summary>
        private static void AddEndMarkers(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope, double radius,
            double millimetresPerPixel)
        {
            if (!envelope.DirectionResolved || envelope.UpstreamEndpoint == null
                || envelope.DownstreamEndpoint == null) return;
            double marker = Math.Max(radius * 2.2, millimetresPerPixel * 4.0);
            QuickLineOrbitPreviewEnvelope.EnvelopePoint upstream = envelope.UpstreamEndpoint;
            QuickLineOrbitPreviewEnvelope.EnvelopePoint device = envelope.DownstreamEndpoint;
            scene.AddSphere(QuickLine3dSceneGroupKind.Routes,
                upstream.X, upstream.Y, upstream.Z, marker, UpstreamEndTint);
            scene.AddSphere(QuickLine3dSceneGroupKind.Routes,
                device.X, device.Y, device.Z, marker, DeviceEndTint);
        }

        // ---- 标签 ----------------------------------------------------------------

        private static void AddLabels(QuickLine3dScene scene,
            QuickLineOrbitPreviewEnvelope envelope, double radius, double labelHeight)
        {
            // 楼层名 + 相对高度：贴在每块平面的近角外沿。
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeFloor floor in envelope.Floors)
            {
                double width = floor.MaxX - floor.MinX;
                double depth = floor.MaxY - floor.MinY;
                scene.AddLabel(QuickLine3dSceneGroupKind.Reference, "floor",
                    floor.Label + "  " + floor.ElevationText,
                    floor.MinX + width * 0.02, floor.MinY - depth * 0.04, floor.Z);
            }

            // 轴位边标（数字/字母各一排）。
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeLabel label
                in envelope.AxisEdgeLabels)
                scene.AddLabel(QuickLine3dSceneGroupKind.Grid, "axisEdge",
                    label.Text, label.X, label.Y, label.Z);

            // 电缆米数：文字改成米（「14.4 m」），贴在线上——沿线方向排字、放在
            // 线的上侧，不参与防重叠（视角怎么转都只跟着自己的线走）。
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeLabel label
                in envelope.SegmentLabels)
                scene.AddLineLabel(QuickLine3dSceneGroupKind.Routes, "mm",
                    MetricText(label.Text), label.X, label.Y, label.Z,
                    label.DirectionX, label.DirectionY, label.DirectionZ);

            // 桥架/线管整趟标签（贴着宿主电缆、放在线的下侧）、软管标签（贴着软管）。
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeLabel label
                in envelope.RunLabels)
                scene.AddLineLabel(QuickLine3dSceneGroupKind.Routes,
                    label.Text.StartsWith("桥架", StringComparison.Ordinal)
                        ? "tray" : "conduit",
                    label.Text, label.X, label.Y, label.Z,
                    label.DirectionX, label.DirectionY, label.DirectionZ);
            if (envelope.Hose != null)
                scene.AddLineLabel(QuickLine3dSceneGroupKind.Routes, "hose",
                    envelope.Hose.Label,
                    (envelope.Hose.Start.X + envelope.Hose.End.X) / 2.0,
                    (envelope.Hose.Start.Y + envelope.Hose.End.Y) / 2.0,
                    (envelope.Hose.Start.Z + envelope.Hose.End.Z) / 2.0,
                    envelope.Hose.End.X - envelope.Hose.Start.X,
                    envelope.Hose.End.Y - envelope.Hose.Start.Y,
                    envelope.Hose.End.Z - envelope.Hose.Start.Z);

            // 上游柜标签：压在柜子中心（柜子小，牌子居中才看得出是哪台柜；
            // 插座盘下面另画离地支架，别再把牌子放到地面当地注）。
            if (envelope.Cabinet != null)
            {
                QuickLineOrbitPreviewEnvelope.EnvelopeCabinet cabinet = envelope.Cabinet;
                scene.AddLabel(QuickLine3dSceneGroupKind.Reference, "cabinet",
                    cabinet.Label + MountingNote(cabinet),
                    cabinet.CenterX, cabinet.CenterY,
                    cabinet.BaseZ + cabinet.Height * 0.5);
            }

            // 两端：一个端点一块牌子（「上游端 48/V」），不再另挂轴位钉。
            // 上游端有柜子时，牌子抬到柜顶上方，不压在柜子上。
            if (envelope.DirectionResolved && envelope.UpstreamEndpoint != null)
            {
                string upstreamAxis = envelope.EndpointPins.Count > 0
                    ? envelope.EndpointPins[0].Text : string.Empty;
                double lift = labelHeight * 1.6;
                if (envelope.Cabinet != null)
                    lift = Math.Max(lift, envelope.Cabinet.TopZ
                        - envelope.UpstreamEndpoint.Z + labelHeight * 1.2);
                scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "directionUp",
                    "上游端" + (upstreamAxis.Length > 0 ? "  " + upstreamAxis : string.Empty),
                    envelope.UpstreamEndpoint.X, envelope.UpstreamEndpoint.Y,
                    envelope.UpstreamEndpoint.Z + lift);
            }
            if (envelope.DirectionResolved && envelope.DownstreamEndpoint != null)
            {
                string deviceAxis = envelope.EndpointPins.Count > 1
                    ? envelope.EndpointPins[1].Text : string.Empty;
                scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "directionDown",
                    "设备端" + (deviceAxis.Length > 0 ? "  " + deviceAxis : string.Empty),
                    envelope.DownstreamEndpoint.X, envelope.DownstreamEndpoint.Y,
                    envelope.DownstreamEndpoint.Z + labelHeight * 1.6);
            }
        }

        private static string MountingNote(QuickLineOrbitPreviewEnvelope.EnvelopeCabinet cabinet)
        {
            if (cabinet.Label.StartsWith("插座", StringComparison.Ordinal))
                return " · 离地 1 m";
            if (cabinet.Label.StartsWith("I-Line", StringComparison.Ordinal))
                return " · 落地";
            return string.Empty;
        }

        private static Color FromRgb(byte r, byte g, byte b)
        {
            return Color.FromRgb(r, g, b);
        }
    }
}
