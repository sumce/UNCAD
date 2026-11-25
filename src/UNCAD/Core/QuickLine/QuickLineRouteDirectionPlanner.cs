using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>A terminal route node in reconstructed scene coordinates.</summary>
    public sealed class QuickLineRouteDirectionEndpoint
    {
        public QuickLineRouteDirectionEndpoint(string nodeId,
            QuickLineSpatialPoint position)
        {
            NodeId = nodeId ?? string.Empty;
            Position = position;
        }

        public string NodeId { get; }
        public QuickLineSpatialPoint Position { get; }
    }

    /// <summary>Resolved electrical direction and any reasons it could not be trusted.</summary>
    public sealed class QuickLineRouteDirection
    {
        internal QuickLineRouteDirection(string upstreamNodeId,
            string downstreamNodeId, IEnumerable<string> diagnostics)
        {
            UpstreamNodeId = upstreamNodeId ?? string.Empty;
            DownstreamNodeId = downstreamNodeId ?? string.Empty;
            Diagnostics = (diagnostics ?? Enumerable.Empty<string>()).ToArray();
        }

        public string UpstreamNodeId { get; }
        public string DownstreamNodeId { get; }
        public bool IsResolved => UpstreamNodeId.Length > 0
            && DownstreamNodeId.Length > 0
            && !string.Equals(UpstreamNodeId, DownstreamNodeId,
                StringComparison.OrdinalIgnoreCase);
        public IReadOnlyList<string> Diagnostics { get; }
    }

    /// <summary>
    /// Assigns route ends from CAD block anchors first. With no anchors, a
    /// two-terminal route may be oriented when both terminal regions match the
    /// workbook axes using the established drawing coordinate convention.
    /// </summary>
    public static class QuickLineRouteDirectionPlanner
    {
        /// <summary>
        /// Finds the route's two degree-one nodes and orients them only when
        /// each end fits its workbook axis region.
        /// </summary>
        public static QuickLineRouteDirection Resolve(
            QuickLineIsometricScene scene, string upstreamAxis,
            string downstreamAxis,
            double spacingMillimetres = QuickLineAxisGridPlanner
                .DefaultSpacingMillimetres)
        {
            if (double.IsNaN(spacingMillimetres)
                || double.IsInfinity(spacingMillimetres)
                || spacingMillimetres <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(spacingMillimetres));
            if (scene == null)
                return new QuickLineRouteDirection(null, null,
                    new[] { "没有可用路线场景；上下游方向未确认。" });
            if (scene.ComponentCount != 1)
                return new QuickLineRouteDirection(null, null,
                    new[] { "路线不连通；不能把多个回路当作一条路线判断上下游。" });

            var positions = scene.Nodes.ToDictionary(node => node.Id,
                node => node.Position, StringComparer.OrdinalIgnoreCase);
            var degrees = positions.Keys.ToDictionary(nodeId => nodeId,
                _ => 0, StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineIsometricSegment segment in scene.Segments)
            {
                if (degrees.ContainsKey(segment.StartNodeId))
                    degrees[segment.StartNodeId]++;
                if (degrees.ContainsKey(segment.EndNodeId))
                    degrees[segment.EndNodeId]++;
            }

            if (degrees.Values.Any(degree => degree > 2))
                return new QuickLineRouteDirection(null, null,
                    new[] { "路线存在分支；轴位不能唯一对应上下游端点，方向未确认。" });

            var endpoints = degrees.Where(pair => pair.Value == 1)
                .Select(pair => new QuickLineRouteDirectionEndpoint(
                    pair.Key, positions[pair.Key]));
            return Resolve(endpoints, null, null, false, false,
                upstreamAxis, downstreamAxis, spacingMillimetres);
        }

        /// <summary>
        /// D-058：按连接块就近判定路线两端——上游端是离上游块最近的路线末端，
        /// 设备端是离设备块最近的路线末端。只有一端有块时，另一端按两端点
        /// 路线的唯一剩余末端确定；两个块都没有时退回工作簿轴位推断（同
        /// <see cref="Resolve(QuickLineIsometricScene, string, string, double)"/>）。
        /// terminals 是路线的开口末端；闭合回路没有开口末端时用 allEndpoints。
        /// </summary>
        public static QuickLineRouteDirection ResolveFromAnchors(
            QuickLineIsometricScene scene,
            IEnumerable<QuickLineRouteEndpointCandidate> terminals,
            IEnumerable<QuickLineRouteEndpointCandidate> allEndpoints,
            IReadOnlyList<QuickLinePoint> upstreamAnchors,
            IReadOnlyList<QuickLinePoint> deviceAnchors,
            string upstreamAxis, string downstreamAxis,
            double spacingMillimetres = QuickLineAxisGridPlanner
                .DefaultSpacingMillimetres)
        {
            if (double.IsNaN(spacingMillimetres)
                || double.IsInfinity(spacingMillimetres)
                || spacingMillimetres <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(spacingMillimetres));
            if (scene == null)
                return new QuickLineRouteDirection(null, null,
                    new[] { "没有可用路线场景；上下游方向未确认。" });

            bool hasUpstream = HasAnyPoint(upstreamAnchors);
            bool hasDevice = HasAnyPoint(deviceAnchors);
            if (!hasUpstream && !hasDevice)
                return Resolve(scene, upstreamAxis, downstreamAxis,
                    spacingMillimetres);

            var diagnostics = new List<string>();
            List<QuickLineRouteEndpointCandidate> openEnds = Usable(terminals);
            List<QuickLineRouteEndpointCandidate> pool = openEnds.Count > 0
                ? openEnds : Usable(allEndpoints);
            if (pool.Count == 0)
                return new QuickLineRouteDirection(null, null,
                    new[] { "路线没有可用端点；上下游方向未确认。" });
            if (openEnds.Count == 0)
                diagnostics.Add("路线没有开口末端（闭合回路），已按全部线段端点就近匹配连接块。");

            string upstreamNode = null;
            string deviceNode = null;
            if (hasUpstream)
            {
                QuickLineRouteAnchorMatch match = QuickLineRouteAnchorPlanner
                    .FindNearest(pool, upstreamAnchors, double.PositiveInfinity);
                if (match.IsMatched)
                {
                    upstreamNode = match.NodeId;
                    diagnostics.Add("上游端＝离上游块最近的路线末端（图面距离 "
                        + Math.Round(match.DistanceMillimetres) + "）。");
                }
                else
                    diagnostics.Add(match.IsAmbiguous
                        ? "两个路线末端到上游块的距离几乎相同，上游端未确认。"
                        : "上游块未能匹配到路线末端。");
            }
            if (hasDevice)
            {
                QuickLineRouteAnchorMatch match = QuickLineRouteAnchorPlanner
                    .FindNearest(pool, deviceAnchors, double.PositiveInfinity);
                if (match.IsMatched)
                {
                    deviceNode = match.NodeId;
                    diagnostics.Add("设备端＝离设备块最近的路线末端（图面距离 "
                        + Math.Round(match.DistanceMillimetres) + "）。");
                }
                else
                    diagnostics.Add(match.IsAmbiguous
                        ? "两个路线末端到设备块的距离几乎相同，设备端未确认。"
                        : "设备块未能匹配到路线末端。");
            }

            var positions = scene.Nodes.ToDictionary(node => node.Id,
                node => node.Position, StringComparer.OrdinalIgnoreCase);
            List<QuickLineRouteDirectionEndpoint> endpoints = pool
                .Where(candidate => positions.ContainsKey(candidate.NodeId))
                .GroupBy(candidate => candidate.NodeId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(candidate => new QuickLineRouteDirectionEndpoint(
                    candidate.NodeId, positions[candidate.NodeId]))
                .ToList();
            QuickLineRouteDirection direction = Resolve(endpoints, upstreamNode,
                deviceNode, hasUpstream, hasDevice, upstreamAxis, downstreamAxis,
                spacingMillimetres);
            diagnostics.AddRange(direction.Diagnostics);
            return new QuickLineRouteDirection(direction.UpstreamNodeId,
                direction.DownstreamNodeId, diagnostics);
        }

        private static bool HasAnyPoint(IReadOnlyList<QuickLinePoint> points)
        {
            if (points == null) return false;
            foreach (QuickLinePoint point in points)
                if (point != null) return true;
            return false;
        }

        private static List<QuickLineRouteEndpointCandidate> Usable(
            IEnumerable<QuickLineRouteEndpointCandidate> candidates)
            => (candidates ?? Enumerable.Empty<QuickLineRouteEndpointCandidate>())
                .Where(item => item != null && item.Point != null
                    && item.NodeId.Length > 0)
                .ToList();

        public static QuickLineRouteDirection Resolve(
            IEnumerable<QuickLineRouteDirectionEndpoint> endpoints,
            string upstreamAnchorNodeId, string downstreamAnchorNodeId,
            bool upstreamAnchorPresent, bool downstreamAnchorPresent,
            string upstreamAxis, string downstreamAxis,
            double spacingMillimetres = QuickLineAxisGridPlanner
                .DefaultSpacingMillimetres)
        {
            if (double.IsNaN(spacingMillimetres)
                || double.IsInfinity(spacingMillimetres)
                || spacingMillimetres <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(spacingMillimetres));

            List<QuickLineRouteDirectionEndpoint> terminals = (endpoints
                ?? Enumerable.Empty<QuickLineRouteDirectionEndpoint>())
                .Where(item => item != null && item.NodeId.Length > 0)
                .GroupBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var diagnostics = new List<string>();

            if (upstreamAnchorPresent && downstreamAnchorPresent)
            {
                if (IsValidPair(upstreamAnchorNodeId, downstreamAnchorNodeId,
                        terminals))
                {
                    AddAxisConflictDiagnostic(diagnostics,
                        Find(terminals, upstreamAnchorNodeId).Position,
                        Find(terminals, downstreamAnchorNodeId).Position,
                        upstreamAxis, downstreamAxis, spacingMillimetres);
                    return new QuickLineRouteDirection(upstreamAnchorNodeId,
                        downstreamAnchorNodeId, diagnostics);
                }

                diagnostics.Add("上游块与设备块未能定位到两个不同的路线端点；上下游方向未确认。");
                return new QuickLineRouteDirection(null, null, diagnostics);
            }

            string anchoredNode = upstreamAnchorPresent
                ? upstreamAnchorNodeId
                : downstreamAnchorPresent ? downstreamAnchorNodeId : null;
            if (anchoredNode != null)
            {
                if (terminals.Count == 2
                    && terminals.Any(item => string.Equals(item.NodeId,
                        anchoredNode, StringComparison.OrdinalIgnoreCase)))
                {
                    string other = terminals.First(item => !string.Equals(
                        item.NodeId, anchoredNode,
                        StringComparison.OrdinalIgnoreCase)).NodeId;
                    diagnostics.Add("仅定位到一端连接块；另一端按两端点路线的唯一剩余端点确定。");
                    string up = upstreamAnchorPresent ? anchoredNode : other;
                    string down = downstreamAnchorPresent ? anchoredNode : other;
                    AddAxisConflictDiagnostic(diagnostics,
                        Find(terminals, up).Position, Find(terminals, down).Position,
                        upstreamAxis, downstreamAxis, spacingMillimetres);
                    return upstreamAnchorPresent
                        ? new QuickLineRouteDirection(anchoredNode, other, diagnostics)
                        : new QuickLineRouteDirection(other, anchoredNode, diagnostics);
                }

                diagnostics.Add("连接块未能唯一匹配到两端点路线；上下游方向未确认。");
                return new QuickLineRouteDirection(null, null, diagnostics);
            }

            if (upstreamAnchorPresent || downstreamAnchorPresent)
            {
                diagnostics.Add("存在连接块，但其位置无法可靠匹配路线端点；上下游方向未确认。");
                return new QuickLineRouteDirection(null, null, diagnostics);
            }

            if (terminals.Count != 2)
            {
                diagnostics.Add("没有可用的上下游连接块，且路线末端数量不是 2；上下游方向未确认。");
                return new QuickLineRouteDirection(null, null, diagnostics);
            }
            if (!QuickLineAxisGridPlanner.TryParseAxis(upstreamAxis,
                    out QuickLineAxisCoordinate upstream)
                || !QuickLineAxisGridPlanner.TryParseAxis(downstreamAxis,
                    out QuickLineAxisCoordinate downstream))
            {
                diagnostics.Add("没有可用的上下游连接块，且轴位不是“数字/字母”格式；上下游方向未确认。");
                return new QuickLineRouteDirection(null, null, diagnostics);
            }

            QuickLineRouteDirectionEndpoint first = terminals[0];
            QuickLineRouteDirectionEndpoint second = terminals[1];
            bool firstIsUpstream = TryPlaceAxisRegions(first.Position, second.Position,
                upstream, downstream, spacingMillimetres, out _);
            bool secondIsUpstream = TryPlaceAxisRegions(second.Position, first.Position,
                upstream, downstream, spacingMillimetres, out _);
            if (firstIsUpstream == secondIsUpstream)
            {
                diagnostics.Add(firstIsUpstream
                    ? "路线两个端点都能匹配上下游轴位区域；方向有歧义，未确认。"
                    : "路线两个端点不能分别落入工作簿上下游轴位区域；方向未确认。");
                return new QuickLineRouteDirection(null, null, diagnostics);
            }

            diagnostics.Add("未找到上下游连接块；两个路线端点已按工作簿轴位区域和“等测图右上为北”约定匹配，请核对。");
            return firstIsUpstream
                ? new QuickLineRouteDirection(first.NodeId, second.NodeId, diagnostics)
                : new QuickLineRouteDirection(second.NodeId, first.NodeId, diagnostics);
        }

        private static void AddAxisConflictDiagnostic(
            ICollection<string> diagnostics,
            QuickLineSpatialPoint upstreamPosition,
            QuickLineSpatialPoint downstreamPosition,
            string upstreamAxis, string downstreamAxis,
            double spacingMillimetres)
        {
            if (!QuickLineAxisGridPlanner.TryParseAxis(upstreamAxis,
                    out QuickLineAxisCoordinate upstream)
                || !QuickLineAxisGridPlanner.TryParseAxis(downstreamAxis,
                    out QuickLineAxisCoordinate downstream)) return;
            if (!TryPlaceAxisRegions(upstreamPosition, downstreamPosition,
                    upstream, downstream, spacingMillimetres, out _))
                diagnostics.Add("图中连接块端点与工作簿上下游轴位区域不匹配；已按连接块定位，请核对轴位。");
        }

        // Without a surveyed origin the grid may translate, never rotate or scale.
        // Split the offset error equally so both ends are inside their signed bays.
        internal static bool TryPlaceAxisRegions(QuickLineSpatialPoint upPoint,
            QuickLineSpatialPoint downPoint, QuickLineAxisCoordinate upstream,
            QuickLineAxisCoordinate downstream, double spacingMillimetres,
            out QuickLineSpatialPoint upstreamCenter)
        {
            double expectedX = (downstream.LetterIndex - upstream.LetterIndex)
                * spacingMillimetres;
            double expectedY = -(downstream.Number - upstream.Number)
                * spacingMillimetres;
            double errorX = downPoint.X - upPoint.X - expectedX;
            double errorY = downPoint.Y - upPoint.Y - expectedY;
            upstreamCenter = new QuickLineSpatialPoint(upPoint.X + errorX / 2,
                upPoint.Y + errorY / 2, 0);
            return Math.Abs(errorX) <= spacingMillimetres
                && Math.Abs(errorY) <= spacingMillimetres;
        }

        private static QuickLineRouteDirectionEndpoint Find(
            IEnumerable<QuickLineRouteDirectionEndpoint> endpoints,
            string nodeId)
            => endpoints.FirstOrDefault(item => string.Equals(item.NodeId,
                nodeId, StringComparison.OrdinalIgnoreCase));

        private static bool IsValidPair(string upstreamNodeId,
            string downstreamNodeId,
            IReadOnlyList<QuickLineRouteDirectionEndpoint> endpoints)
        {
            if (string.IsNullOrWhiteSpace(upstreamNodeId)
                || string.IsNullOrWhiteSpace(downstreamNodeId)
                || string.Equals(upstreamNodeId, downstreamNodeId,
                    StringComparison.OrdinalIgnoreCase)) return false;
            return endpoints.Any(item => string.Equals(item.NodeId,
                    upstreamNodeId, StringComparison.OrdinalIgnoreCase))
                && endpoints.Any(item => string.Equals(item.NodeId,
                    downstreamNodeId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
