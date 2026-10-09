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

    /// <summary>Finds the route endpoint nearest a CAD device or upstream block.</summary>
    public static class QuickLineRouteAnchorPlanner
    {
        public static string FindNearestNode(
            IEnumerable<QuickLineRouteEndpointCandidate> candidates,
            QuickLinePoint anchor)
        {
            double bestDistance = double.PositiveInfinity;
            string bestNode = string.Empty;
            foreach (QuickLineRouteEndpointCandidate candidate
                in candidates ?? Array.Empty<QuickLineRouteEndpointCandidate>())
            {
                if (candidate == null) continue;
                double distance = candidate.Point.DistanceSquaredTo(anchor);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestNode = candidate.NodeId;
                }
            }
            return bestNode;
        }
    }
}
