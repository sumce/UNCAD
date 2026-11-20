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
            if (double.IsNaN(maximumDistanceMillimetres)
                || maximumDistanceMillimetres < 0.0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDistanceMillimetres));

            double bestDistance = double.PositiveInfinity;
            double secondDistance = double.PositiveInfinity;
            string bestNode = string.Empty;
            var seenNodes = new System.Collections.Generic.HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineRouteEndpointCandidate candidate
                in candidates ?? Array.Empty<QuickLineRouteEndpointCandidate>())
            {
                if (candidate == null || candidate.Point == null
                    || candidate.NodeId.Length == 0
                    || !seenNodes.Add(candidate.NodeId)) continue;
                double distance = candidate.Point.DistanceSquaredTo(anchor);
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
