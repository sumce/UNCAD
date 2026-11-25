using System;
using System.Collections.Generic;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// One CAD route segment paired with the reconstructed scene node at each
    /// endpoint.  Keeping this value in Core lets the feature anchor markers
    /// without exposing AutoCAD geometry to the route planner.
    /// </summary>
    public sealed class QuickLineRouteEndpointCandidate
    {
        public QuickLineRouteEndpointCandidate(string segmentId,
            string nodeId, QuickLinePoint point)
        {
            SegmentId = segmentId ?? string.Empty;
            NodeId = nodeId ?? string.Empty;
            Point = point;
        }

        public string SegmentId { get; }
        public string NodeId { get; }
        public QuickLinePoint Point { get; }
    }

    /// <summary>Nearest route endpoint match, including confidence diagnostics.</summary>
    public sealed class QuickLineRouteAnchorMatch
    {
        internal QuickLineRouteAnchorMatch(string nodeId, double distanceMillimetres,
            bool ambiguous)
        {
            NodeId = nodeId ?? string.Empty;
            DistanceMillimetres = distanceMillimetres;
            IsAmbiguous = ambiguous;
        }

        public string NodeId { get; }
        public double DistanceMillimetres { get; }
        public bool IsAmbiguous { get; }
        public bool IsMatched => NodeId.Length > 0 && !IsAmbiguous;
    }

    /// <summary>Finds the route endpoint nearest a CAD device or upstream block.</summary>
    public static class QuickLineRouteAnchorPlanner
    {
        public static string FindNearestNode(
            IEnumerable<QuickLineRouteEndpointCandidate> candidates,
            QuickLinePoint anchor)
            => FindNearest(candidates, anchor, double.PositiveInfinity).NodeId;

        public static QuickLineRouteAnchorMatch FindNearest(
            IEnumerable<QuickLineRouteEndpointCandidate> candidates,
            QuickLinePoint anchor, double maximumDistanceMillimetres)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            return FindNearest(candidates, new[] { anchor },
                maximumDistanceMillimetres);
        }

        /// <summary>
        /// D-058：同类连接块可能有多个（多台设备 / 多个上游信息块），候选项到
        /// 「这一类块」的距离取到最近那个块位置的距离；其余口径同单锚点重载。
        /// anchors 为空或全是 null 时视为没有可定位的块（不匹配）。
        /// </summary>
        public static QuickLineRouteAnchorMatch FindNearest(
            IEnumerable<QuickLineRouteEndpointCandidate> candidates,
            IReadOnlyList<QuickLinePoint> anchors,
            double maximumDistanceMillimetres)
        {
            if (anchors == null) throw new ArgumentNullException(nameof(anchors));
            if (double.IsNaN(maximumDistanceMillimetres)
                || maximumDistanceMillimetres < 0.0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDistanceMillimetres));
            List<QuickLinePoint> usable = new List<QuickLinePoint>();
            foreach (QuickLinePoint anchor in anchors)
                if (anchor != null) usable.Add(anchor);
            if (usable.Count == 0)
                return new QuickLineRouteAnchorMatch(string.Empty,
                    double.PositiveInfinity, false);

            double bestDistance = double.PositiveInfinity;
            double secondDistance = double.PositiveInfinity;
            string bestNode = string.Empty;
            var seenNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineRouteEndpointCandidate candidate
                in candidates ?? Array.Empty<QuickLineRouteEndpointCandidate>())
            {
                if (candidate == null || candidate.Point == null
                    || candidate.NodeId.Length == 0
                    || !seenNodes.Add(candidate.NodeId)) continue;
                double distance = double.PositiveInfinity;
                foreach (QuickLinePoint anchor in usable)
                {
                    double toAnchor = candidate.Point.DistanceSquaredTo(anchor);
                    if (toAnchor < distance) distance = toAnchor;
                }
                if (distance < bestDistance)
                {
                    secondDistance = bestDistance;
                    bestDistance = distance;
                    bestNode = candidate.NodeId;
                }
                else if (distance < secondDistance)
                    secondDistance = distance;
            }
            double distanceMillimetres = Math.Sqrt(bestDistance);
            if (distanceMillimetres > maximumDistanceMillimetres)
                bestNode = string.Empty;
            bool ambiguous = bestNode.Length > 0
                && Math.Sqrt(secondDistance) - distanceMillimetres <= 1.0;
            return new QuickLineRouteAnchorMatch(
                ambiguous ? string.Empty : bestNode,
                distanceMillimetres, ambiguous);
        }
    }
}
