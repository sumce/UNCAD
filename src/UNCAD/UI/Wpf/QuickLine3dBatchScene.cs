using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace UNCAD.UI
{
    /// <summary>
    /// 3.4.0：U13D 批量信封（字典形态，QuickLine3dPreviewFeature 构造）
    /// → 原生场景 + 信息面板数据。几何/文案公式与退役的 Web/QuickLine3D
    /// 页面逐条对齐：路由浅蓝管、节点白球、下游绿球/上游琥珀盒、轴位线
    /// （numeric=X 向竖线/alphabetic=Y 向横线）、东西南北罗盘、正交相机
    /// 方位 (1.35,-1.55,1.15)、视口高度 1.55×场景尺寸；批量模式下路由/端点
    /// 标签超过 24 条自动隐藏（原页面同口径）。
    /// </summary>
    internal sealed class QuickLine3dBatchScene
    {
        private static readonly Color RouteTint = FromRgb(0x9B, 0xC2, 0xDF);
        private static readonly Color NodeTint = FromRgb(0xE7, 0xEE, 0xF7);
        private static readonly Color DownstreamTint = FromRgb(0x63, 0xC9, 0xA5);
        private static readonly Color UpstreamTint = FromRgb(0xE0, 0xB0, 0x64);
        private static readonly Color GridLineTint = FromRgb(0x41, 0x50, 0x5F);
        private static readonly Color OuterGridTint = FromRgb(0x34, 0x43, 0x52);

        private QuickLine3dBatchScene()
        {
        }

        internal QuickLine3dScene Scene { get; private set; }
        internal bool BatchMode { get; private set; }
        internal bool ShowRouteLabels { get; private set; }
        internal bool ShowDirectionLabels { get; private set; }

        // ---- 信息面板（原页面右侧卡片的数据） ----------------------------------
        internal string MachineId { get; private set; }
        internal string DeviceName { get; private set; }
        internal string FrameCount { get; private set; }
        internal string DeviceFloor { get; private set; }
        internal string PanelFloor { get; private set; }
        internal string ProjectionMode { get; private set; }
        internal string UpstreamAxis { get; private set; }
        internal string DownstreamAxis { get; private set; }
        internal string Spacing { get; private set; }
        internal string AxisNumericLine { get; private set; }
        internal string AxisAlphabeticLine { get; private set; }
        internal string SegmentCount { get; private set; }
        internal string RouteLength { get; private set; }
        internal string RouteMeters { get; private set; }
        internal string StatusText { get; private set; }
        internal List<RouteRow> Routes { get; } = new List<RouteRow>();
        internal List<BridgeRow> Bridges { get; } = new List<BridgeRow>();
        internal List<FrameRow> Frames { get; } = new List<FrameRow>();
        internal List<string> Diagnostics { get; } = new List<string>();

        internal sealed class RouteRow
        {
            internal string Title;
            internal string Detail;
            internal string Distance;
        }

        internal sealed class BridgeRow
        {
            internal string Spec;
            internal bool Associated;
            internal string Length;
        }

        internal sealed class FrameRow
        {
            internal string MachineId;
            internal string Circuit;
            internal string Axis;
            internal string Offset;
        }

        /// <summary>适配（envelope 为 null/无节点时返回 null）。</summary>
        public static QuickLine3dBatchScene Build(
            IDictionary<string, object> envelope)
        {
            if (envelope == null) return null;
            IDictionary<string, object> sceneValue =
                GetDict(envelope, "scene");
            if (sceneValue == null) return null;
            var nodes = ReadNodes(sceneValue);
            if (nodes.Count == 0) return null;
            var segments = GetList(sceneValue, "segments");
            var markers = GetList(sceneValue, "directionMarkers");
            var grid = GetDict(envelope, "axisGrid");
            var metadata = GetDict(envelope, "metadata");

            var result = new QuickLine3dBatchScene
            {
                BatchMode = string.Equals(GetStr(envelope, "mode"), "batch",
                    StringComparison.Ordinal),
                Scene = new QuickLine3dScene()
            };
            QuickLine3dScene scene3d = result.Scene;

            // 场景规模（路由+标记的包络，取景与线径基准）。
            double minx = double.MaxValue, miny = double.MaxValue;
            double minz = double.MaxValue;
            double maxx = double.MinValue, maxy = double.MinValue;
            double maxz = double.MinValue;
            foreach (NodePosition node in nodes.Values)
            {
                minx = Math.Min(minx, node.X); maxx = Math.Max(maxx, node.X);
                miny = Math.Min(miny, node.Y); maxy = Math.Max(maxy, node.Y);
                minz = Math.Min(minz, node.Z); maxz = Math.Max(maxz, node.Z);
            }
            var markerPoints = new List<NodePosition>();
            foreach (object rawMarker in markers)
            {
                IDictionary<string, object> marker = rawMarker as IDictionary<string, object>;
                NodePosition point = ReadPoint(GetDict(marker, "position"));
                if (point == null) continue;
                markerPoints.Add(point);
                minx = Math.Min(minx, point.X); maxx = Math.Max(maxx, point.X);
                miny = Math.Min(miny, point.Y); maxy = Math.Max(maxy, point.Y);
                minz = Math.Min(minz, point.Z); maxz = Math.Max(maxz, point.Z);
            }
            if (minx > maxx)
            {
                minx = miny = minz = 0.0;
                maxx = maxy = maxz = 1000.0;
            }
            double sizex = maxx - minx, sizey = maxy - miny, sizez = maxz - minz;
            double extent = Math.Max(Math.Max(sizex, Math.Max(sizey, sizez)),
                1000.0);
            double diagonal = Math.Sqrt(sizex * sizex + sizey * sizey
                + sizez * sizez);
            scene3d.Diagonal = Math.Max(diagonal, 1.0);
            double radius = Math.Max(extent * 0.0019, 40.0);

            // 路由管 + 每条路由组的聚合标签。
            double labelHeight = Math.Max(extent * 0.014, 300.0);
            var routeGroups = new Dictionary<string, RouteAggregate>(
                StringComparer.Ordinal);
            int routeSegmentTotal = 0;
            double routeLengthTotal = 0.0;
            foreach (object rawSegment in segments)
            {
                IDictionary<string, object> segment =
                    rawSegment as IDictionary<string, object>;
                NodePosition start;
                if (!nodes.TryGetValue(GetStr(segment, "startNodeId"),
                        out start)) continue;
                NodePosition end;
                if (!nodes.TryGetValue(GetStr(segment, "endNodeId"), out end))
                    continue;
                scene3d.AddTube(QuickLine3dSceneGroupKind.Routes,
                    start.X, start.Y, start.Z, end.X, end.Y, end.Z,
                    radius, RouteTint);
                double distance = GetDouble(segment, "distanceMm");
                string key = GetStr(segment, "routeGroupId");
                if (string.IsNullOrEmpty(key)) key = GetStr(segment, "id");
                if (!routeGroups.TryGetValue(key, out RouteAggregate group))
                {
                    group = new RouteAggregate
                    {
                        FrameLabel = GetStr(segment, "frameLabel"),
                        CircuitName = GetStr(segment, "circuitName")
                    };
                    routeGroups.Add(key, group);
                }
                group.DistanceMm += distance;
                group.SegmentCount++;
                group.Midx += (start.X + end.X) / 2.0;
                group.Midy += (start.Y + end.Y) / 2.0;
                group.Midz += (start.Z + end.Z) / 2.0;
                routeSegmentTotal++;
                routeLengthTotal += distance;
            }
            foreach (KeyValuePair<string, RouteAggregate> pair in routeGroups)
            {
                RouteAggregate group = pair.Value;
                if (group.SegmentCount <= 0) continue;
                scene3d.AddLabel(QuickLine3dSceneGroupKind.Routes, "route",
                    RouteLabel(pair.Key, group),
                    group.Midx / group.SegmentCount,
                    group.Midy / group.SegmentCount,
                    group.Midz / group.SegmentCount + labelHeight);
            }

            // 节点白球。
            double nodeRadius = Math.Max(extent * 0.004, 38.0);
            foreach (NodePosition node in nodes.Values)
            {
                scene3d.AddSphere(QuickLine3dSceneGroupKind.Routes,
                    node.X, node.Y, node.Z, nodeRadius, NodeTint);
            }

            // 方向标记：下游=绿球，上游=琥珀盒。
            foreach (object rawMarker in markers)
            {
                IDictionary<string, object> marker =
                    rawMarker as IDictionary<string, object>;
                if (marker == null) continue;
                NodePosition point = ReadPoint(GetDict(marker, "position"));
                if (point == null) continue;
                bool downstream = string.Equals(GetStr(marker, "kind"),
                    "downstream", StringComparison.Ordinal);
                if (downstream)
                {
                    scene3d.AddSphere(QuickLine3dSceneGroupKind.Direction,
                        point.X, point.Y, point.Z,
                        Math.Max(extent * 0.008, 125.0), DownstreamTint);
                }
                else
                {
                    double size = Math.Max(extent * 0.014, 220.0);
                    scene3d.AddBox(QuickLine3dSceneGroupKind.Direction,
                        point.X, point.Y, point.Z, size, size, size,
                        UpstreamTint, 1.0, Colors.Transparent);
                }
                scene3d.AddLabel(QuickLine3dSceneGroupKind.Direction,
                    downstream ? "directionDown" : "directionUp",
                    GetStr(marker, "title") + " " + GetStr(marker, "axis"),
                    point.X, point.Y, point.Z);
            }

            // 轴位网格 + 罗盘（Grid 组——「柱网」开关）。
            BuildAxisGrid(result, scene3d, grid, nodes, markerPoints, extent,
                radius);

            // 相机：正交，方位 (1.35,-1.55,1.15)，视口高度 1.55×尺寸。
            scene3d.Orthographic = true;
            scene3d.Centerx = (minx + maxx) / 2.0;
            scene3d.Centery = (miny + maxy) / 2.0;
            scene3d.Centerz = (minz + maxz) / 2.0;
            scene3d.Orbitx = 1.35;
            scene3d.Orbitsy = -1.55;
            scene3d.Orbitz = 1.15;
            scene3d.OrthoViewHeight = extent * 1.55;
            scene3d.CameraDistance = extent * 4.0;
            scene3d.Near = Math.Max(extent * 0.001, 0.1);
            scene3d.Far = Math.Max(100000.0, extent * 20.0);

            // 标签默认显隐：批量模式下超 24 条自动隐藏（原页面同口径——
            // 按段数/标记数计，不是路由组数）。
            result.ShowRouteLabels = !result.BatchMode || segments.Count <= 24;
            result.ShowDirectionLabels = !result.BatchMode
                || markers.Count <= 24;

            FillPanel(result, envelope, sceneValue, grid, metadata, segments,
                routeGroups, routeSegmentTotal, routeLengthTotal, markers);
            return result;
        }

        private static void BuildAxisGrid(QuickLine3dBatchScene result,
            QuickLine3dScene scene3d, IDictionary<string, object> grid,
            Dictionary<string, NodePosition> nodes,
            List<NodePosition> markerPoints, double extent, double radius)
        {
            double lineRadius = Math.Max(radius * 0.4, 8.0);
            double spacing = Math.Max(GetDouble(grid, "spacingMillimetres"),
                4800.0);
            var numericMarks = GetList(grid, "numericMarks");
            var alphabeticMarks = GetList(grid, "alphabeticMarks");
            foreach (object raw in numericMarks)
            {
                double offset = GetDouble(raw as IDictionary<string, object>,
                    "offsetMillimetres");
                string label = GetStr(raw as IDictionary<string, object>, "label");
                scene3d.AddTube(QuickLine3dSceneGroupKind.Grid,
                    offset, -extent, 0.0, offset, extent, 0.0,
                    lineRadius, GridLineTint);
                scene3d.AddLabel(QuickLine3dSceneGroupKind.Grid, "axis",
                    label, offset, -extent, 0.0);
            }
            foreach (object raw in alphabeticMarks)
            {
                double offset = GetDouble(raw as IDictionary<string, object>,
                    "offsetMillimetres");
                string label = GetStr(raw as IDictionary<string, object>, "label");
                scene3d.AddTube(QuickLine3dSceneGroupKind.Grid,
                    -extent, offset, 0.0, extent, offset, 0.0,
                    lineRadius, GridLineTint);
                scene3d.AddLabel(QuickLine3dSceneGroupKind.Grid, "axis",
                    label, extent, offset, 0.0);
            }
            scene3d.AddLabel(QuickLine3dSceneGroupKind.Grid, "compass", "东 →",
                extent, 0.0, 0.0);
            scene3d.AddLabel(QuickLine3dSceneGroupKind.Grid, "compass", "← 西",
                -extent, 0.0, 0.0);
            scene3d.AddLabel(QuickLine3dSceneGroupKind.Grid, "compass", "北 ↑",
                0.0, extent, 0.0);
            scene3d.AddLabel(QuickLine3dSceneGroupKind.Grid, "compass", "南 ↓",
                0.0, -extent, 0.0);

            // 外圈方格网（原页面 GridHelper；格数过多时省略防网格淹没）。
            int divisions = (int)Math.Round(extent * 2.0 / spacing);
            if (divisions >= 2 && divisions <= 60)
            {
                for (int index = 0; index <= divisions; index++)
                {
                    double position = -extent + extent * 2.0 * index / divisions;
                    scene3d.AddTube(QuickLine3dSceneGroupKind.Grid,
                        position, -extent, 0.0, position, extent, 0.0,
                        lineRadius * 0.5, OuterGridTint);
                    scene3d.AddTube(QuickLine3dSceneGroupKind.Grid,
                        -extent, position, 0.0, extent, position, 0.0,
                        lineRadius * 0.5, OuterGridTint);
                }
            }
        }

        private static void FillPanel(QuickLine3dBatchScene result,
            IDictionary<string, object> envelope,
            IDictionary<string, object> sceneValue,
            IDictionary<string, object> grid,
            IDictionary<string, object> metadata, List<object> segments,
            Dictionary<string, RouteAggregate> routeGroups,
            int segmentTotal, double lengthTotal, List<object> markers)
        {
            bool batch = result.BatchMode;
            result.MachineId = batch ? "多图框"
                : Text(metadata, "machineId");
            result.DeviceName = batch ? "统一轴位预览"
                : Text(metadata, "deviceName");
            result.FrameCount = batch
                ? Format(GetDouble(metadata, "frameCount"))
                : "1";
            result.DeviceFloor = Text(metadata, "deviceFloor");
            result.PanelFloor = Text(metadata, "panelFloor");
            result.ProjectionMode = Text(sceneValue, "projectionMode");
            result.UpstreamAxis = batch ? "见场景端点标记"
                : Text(grid, "upstreamAxis");
            result.DownstreamAxis = batch ? "见场景端点标记"
                : Text(grid, "downstreamAxis");
            result.Spacing = "相邻轴位 "
                + Format(GetDouble(grid, "spacingMillimetres")) + " mm";
            result.AxisNumericLine = "东西数字轴（向东 +1）："
                + MarksLine(grid, "numericMarks");
            result.AxisAlphabeticLine = "南北字母轴（向北 +1）："
                + MarksLine(grid, "alphabeticMarks");
            result.SegmentCount = segmentTotal.ToString(CultureInfo.InvariantCulture);
            result.RouteLength = Format(lengthTotal);
            result.RouteMeters = Format(lengthTotal / 1000.0);
            result.StatusText = batch ? "已载入多图框只读预览" : "已载入只读预览";

            // 回路列表（批量按 图框×回路 归组）。
            var rows = new Dictionary<string, RouteRowAggregate>(
                StringComparer.Ordinal);
            foreach (object raw in segments)
            {
                var segment = raw as IDictionary<string, object>;
                string circuit = Text(segment, "circuitName");
                string frame = batch ? Text(segment, "frameLabel") : "";
                string key = frame + "\u0000" + circuit;
                if (!rows.TryGetValue(key, out RouteRowAggregate row))
                {
                    row = new RouteRowAggregate
                    {
                        Circuit = circuit,
                        Frame = frame,
                        TitleCircuit = Text(segment, "circuitName"),
                        RouteIds = new List<string>()
                    };
                    rows.Add(key, row);
                    row.RouteIds.Add(RouteDisplayName(
                        GetStr(segment, "routeGroupId"),
                        GetStr(segment, "id")));
                }
                else
                {
                    string routeId = RouteDisplayName(
                        GetStr(segment, "routeGroupId"),
                        GetStr(segment, "id"));
                    if (!row.RouteIds.Contains(routeId))
                        row.RouteIds.Add(routeId);
                }
                row.DistanceMm += GetDouble(segment, "distanceMm");
                row.SegmentCount++;
            }
            foreach (RouteRowAggregate row in rows.Values)
            {
                string owner = batch && !string.IsNullOrEmpty(row.Frame)
                    ? row.Frame + " · " : "";
                result.Routes.Add(new RouteRow
                {
                    Title = owner + (row.TitleCircuit == "-"
                        ? "未识别回路" : row.TitleCircuit),
                    Detail = row.RouteIds.Count + "条路由 · "
                        + row.SegmentCount + "段"
                        + (row.RouteIds.Count <= 4
                            ? " · " + string.Join("、", row.RouteIds.ToArray())
                            : ""),
                    Distance = Format(row.DistanceMm) + " mm"
                });
            }

            foreach (object raw in GetList(envelope, "bridges"))
            {
                var bridge = raw as IDictionary<string, object>;
                if (bridge == null) continue;
                double valueM = GetDouble(bridge, "lengthM");
                if (valueM <= 0) valueM = GetDouble(bridge, "lengthMm") / 1000.0;
                result.Bridges.Add(new BridgeRow
                {
                    Spec = Text(bridge, "spec"),
                    Associated = string.Equals(GetStr(bridge, "associated"),
                        "True", StringComparison.OrdinalIgnoreCase)
                        || Equals(GetStr(bridge, "associated"), bool.TrueString)
                        || GetBool(bridge, "associated"),
                    Length = Format(valueM) + " m"
                });
            }

            foreach (object raw in GetList(envelope, "frames"))
            {
                var frame = raw as IDictionary<string, object>;
                if (frame == null) continue;
                result.Frames.Add(new FrameRow
                {
                    MachineId = Text(frame, "machineId"),
                    Circuit = "回路：" + Text(frame, "deviceName"),
                    Axis = Text(frame, "downstreamAxis") + " → "
                        + Text(frame, "upstreamAxis"),
                    Offset = "(" + Format(GetDouble(frame, "offsetXMillimetres"))
                        + ", " + Format(GetDouble(frame, "offsetYMillimetres"))
                        + ") mm"
                });
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in GetStrings(sceneValue, "diagnostics"))
                if (seen.Add(value)) result.Diagnostics.Add(value);
            foreach (string value in GetStrings(grid, "diagnostics"))
                if (seen.Add(value)) result.Diagnostics.Add(value);
            foreach (string value in GetStrings(envelope, "diagnostics"))
                if (seen.Add(value)) result.Diagnostics.Add(value);
        }

        // ---- 文案格式（对齐原页面 routeLabel/compact/routeName/format） ---------

        private static string RouteLabel(string routeGroupId,
            RouteAggregate group)
        {
            string circuit = Blank(group.CircuitName, "-");
            string frame = Compact(group.FrameLabel);
            string id = RouteShortName(routeGroupId);
            string owner = frame == "-" ? "" : frame + " · ";
            return owner
                + (circuit == "-" ? "未识别回路" : Compact(circuit))
                + " · 路由" + id + " " + Format(group.DistanceMm)
                + "mm · " + group.SegmentCount + "段";
        }

        private static string RouteDisplayName(string routeGroupId, string id)
        {
            string name = RouteShortName(string.IsNullOrEmpty(routeGroupId)
                ? id : routeGroupId);
            return string.IsNullOrEmpty(name) ? "路由" : "路由" + name;
        }

        private static string RouteShortName(string value)
        {
            string id = value ?? "";
            int underscore = id.IndexOf('_');
            if (id.StartsWith("F", StringComparison.Ordinal) && underscore > 1
                && IsDigits(id.Substring(1, underscore - 1)))
                id = id.Substring(underscore + 1);
            return id;
        }

        private static bool IsDigits(string value)
        {
            foreach (char c in value)
            {
                if (c < '0' || c > '9') return false;
            }
            return value.Length > 0;
        }

        private static string Compact(string value)
        {
            string normalized = Blank(value, "-");
            return normalized.Length > 18
                ? normalized.Substring(0, 17) + "…" : normalized;
        }

        private static string MarksLine(IDictionary<string, object> grid,
            string key)
        {
            var parts = new List<string>();
            foreach (object raw in GetList(grid, key))
            {
                var mark = raw as IDictionary<string, object>;
                parts.Add(GetStr(mark, "label") + " ("
                    + Format(GetDouble(mark, "offsetMillimetres")) + "mm)");
            }
            return parts.Count == 0 ? "-" : string.Join(" · ", parts.ToArray());
        }

        // ---- 字典安全读取 ---------------------------------------------------------

        private sealed class RouteAggregate
        {
            internal string FrameLabel;
            internal string CircuitName;
            internal double DistanceMm;
            internal int SegmentCount;
            internal double Midx;
            internal double Midy;
            internal double Midz;
        }

        private sealed class RouteRowAggregate
        {
            internal string Frame;
            internal string Circuit;
            internal string TitleCircuit;
            internal List<string> RouteIds;
            internal double DistanceMm;
            internal int SegmentCount;
        }

        private sealed class NodePosition
        {
            internal double X;
            internal double Y;
            internal double Z;
        }

        private static Dictionary<string, NodePosition> ReadNodes(
            IDictionary<string, object> sceneValue)
        {
            var nodes = new Dictionary<string, NodePosition>(
                StringComparer.Ordinal);
            foreach (object raw in GetList(sceneValue, "nodes"))
            {
                var node = raw as IDictionary<string, object>;
                string id = GetStr(node, "id");
                NodePosition point = ReadPoint(GetDict(node, "position"));
                if (string.IsNullOrEmpty(id) || point == null) continue;
                nodes[id] = point;
            }
            return nodes;
        }

        private static NodePosition ReadPoint(IDictionary<string, object> point)
        {
            if (point == null) return null;
            return new NodePosition
            {
                X = GetDouble(point, "x"),
                Y = GetDouble(point, "y"),
                Z = GetDouble(point, "z")
            };
        }

        private static IDictionary<string, object> GetDict(
            IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value)
                ? value as IDictionary<string, object>
                : null;
        }

        private static List<object> GetList(IDictionary<string, object> source,
            string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value))
                return new List<object>();
            object[] array = value as object[];
            if (array != null) return new List<object>(array);
            List<object> list = value as List<object>;
            return list ?? new List<object>();
        }

        private static string GetStr(IDictionary<string, object> source,
            string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value)
                || value == null) return string.Empty;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static double GetDouble(IDictionary<string, object> source,
            string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value)
                || value == null) return 0.0;
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0.0;
            }
        }

        private static bool GetBool(IDictionary<string, object> source,
            string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value)
                || value == null) return false;
            return Equals(value, true) || string.Equals(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                bool.TrueString, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetStrings(
            IDictionary<string, object> source, string key)
        {
            var result = new List<string>();
            foreach (object raw in GetList(source, key) ??
                new List<object>())
            {
                if (raw != null)
                    result.Add(Convert.ToString(raw,
                        CultureInfo.InvariantCulture));
            }
            return result;
        }

        private static string Text(IDictionary<string, object> source,
            string key)
        {
            string value = GetStr(source, key);
            return value.Trim().Length == 0 ? "-" : value;
        }

        private static string Blank(string value, string fallback)
        {
            return value == null || value.Trim().Length == 0
                ? fallback : value;
        }

        private static string Format(double value)
        {
            return Math.Round(value, 2)
                .ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static Color FromRgb(byte r, byte g, byte b)
        {
            return Color.FromRgb(r, g, b);
        }
    }
}
