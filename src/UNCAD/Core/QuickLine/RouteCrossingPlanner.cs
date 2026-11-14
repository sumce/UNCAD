using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>One line in its final (post-relayout) position, with the
    /// camera closeness reconstructed by proportion planning when known.</summary>
    public sealed class RouteCrossingSegment
    {
        public RouteCrossingSegment(string id, QuickLinePoint start,
            QuickLinePoint end, double? closeness,
            QuickLineProjectionMode mode)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A segment id is required.", "id");
            Id = id;
            Start = start;
            End = end;
            Closeness = closeness;
            Mode = mode;
        }

        public string Id { get; }
        public QuickLinePoint Start { get; }
        public QuickLinePoint End { get; }
        /// <summary>null when no isometric depth could be reconstructed.</summary>
        public double? Closeness { get; }
        public QuickLineProjectionMode Mode { get; }
        public double Length => Start.DistanceTo(End);
    }

    /// <summary>Hop openings planned for one line, ordered from its start.</summary>
    public sealed class RouteHopPlan
    {
        internal RouteHopPlan(string segmentId, IReadOnlyList<double> centers)
        {
            SegmentId = segmentId;
            Centers = centers;
        }

        public string SegmentId { get; }
        /// <summary>Distances from the line start to each hop centre.</summary>
        public IReadOnlyList<double> Centers { get; }
    }

    /// <summary>Crossing detection and hop assignment for a final layout.</summary>
    public sealed class RouteCrossingPlan
    {
        internal RouteCrossingPlan(IReadOnlyList<RouteHopPlan> hops,
            IReadOnlyList<string> skipped,
            IReadOnlyList<string> droppedHops)
        {
            Hops = hops;
            Skipped = skipped;
            DroppedHops = droppedHops;
        }

        public IReadOnlyList<RouteHopPlan> Hops { get; }
        /// <summary>Crossings left without a hop, named by position.</summary>
        public IReadOnlyList<string> Skipped { get; }
        /// <summary>Hops dropped by spacing/endpoint clearance rules.</summary>
        public IReadOnlyList<string> DroppedHops { get; }
        public int HopCount => Hops.Sum(item => item.Centers.Count);
    }

    /// <summary>
    /// Detects strict interior crossings between the final 2D lines and
    /// decides which line hops: the segment nearer to the camera (larger
    /// z−x−y closeness) opens the semicircle bridge, matching the isometric
    /// convention that the visually front cable jumps over the rear one.
    ///
    /// Crossings whose depth cannot be decided — either side lacks a
    /// reconstructed closeness, the two sides use different projection
    /// families, or both land at the same depth (a true 3D intersection) —
    /// are skipped and named so the user can hop them manually with U1R.
    /// Intersections at or near a shared endpoint are junctions, not
    /// crossings, and never receive a hop.
    /// </summary>
    public static class RouteCrossingPlanner
    {
        private const double ParallelEpsilon = 1e-9;

        public static RouteCrossingPlan Plan(
            IReadOnlyList<RouteCrossingSegment> segments, double diameter,
            double endpointTolerance = 1.0, double closenessTolerance = 1.0)
        {
            if (segments == null)
                throw new ArgumentNullException("segments");
            if (double.IsNaN(diameter) || double.IsInfinity(diameter)
                || diameter <= 0)
                throw new ArgumentOutOfRangeException("diameter");

            var candidates = segments.Where(item => item != null
                && item.Length > ParallelEpsilon).ToList();
            var centers = new Dictionary<string, List<double>>(
                StringComparer.OrdinalIgnoreCase);
            var skipped = new List<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    RouteCrossingSegment left = candidates[i];
                    RouteCrossingSegment right = candidates[j];
                    if (!TryIntersect(left, right, endpointTolerance,
                            out QuickLinePoint point, out double leftCenter,
                            out double rightCenter)) continue;

                    string where = FormatPoint(point);
                    if (!left.Closeness.HasValue || !right.Closeness.HasValue
                        || left.Mode != right.Mode)
                    {
                        skipped.Add("交叉 " + left.Id + "×" + right.Id
                            + " 于 " + where + " 缺少等测深度，未开洞，请手动 U1R。");
                        continue;
                    }
                    double difference = left.Closeness.Value
                        - right.Closeness.Value;
                    if (Math.Abs(difference) <= closenessTolerance)
                    {
                        skipped.Add("交叉 " + left.Id + "×" + right.Id
                            + " 于 " + where + " 前后深度相同（三维同点），未开洞。");
                        continue;
                    }

                    string hopId = difference > 0 ? left.Id : right.Id;
                    double hopCenter = difference > 0 ? leftCenter : rightCenter;
                    if (!centers.TryGetValue(hopId, out List<double> list))
                    {
                        list = new List<double>();
                        centers[hopId] = list;
                    }
                    list.Add(hopCenter);
                }
            }

            var hops = new List<RouteHopPlan>();
            var dropped = new List<string>();
            foreach (RouteCrossingSegment segment in candidates)
            {
                if (!centers.TryGetValue(segment.Id, out List<double> list))
                    continue;
                list.Sort();
                // 逐洞校验：端点留半径（SemicircleGeometry.CanSplit 口径），
                // 相邻洞距 ≥ 直径；放不下的洞丢弃并点名。
                double radius = diameter / 2.0;
                var kept = new List<double>();
                double lastKept = double.NaN;
                foreach (double center in list)
                {
                    bool fits = center > radius
                        && segment.Length - center > radius
                        && (kept.Count == 0 || center - lastKept >= diameter);
                    if (!fits)
                    {
                        dropped.Add("线段 " + segment.Id + " 洞位 "
                            + center.ToString("0.#", CultureInfo.InvariantCulture)
                            + " 太靠近端点或相邻洞，已丢弃。");
                        continue;
                    }
                    kept.Add(center);
                    lastKept = center;
                }
                if (kept.Count > 0)
                    hops.Add(new RouteHopPlan(segment.Id, kept));
            }
            return new RouteCrossingPlan(hops, skipped, dropped);
        }

        /// <summary>
        /// Strict interior intersection: the crossing point must clear both
        /// segments' endpoints by the endpoint tolerance, so shared endpoints
        /// and T-junctions are junctions rather than crossings.
        /// </summary>
        private static bool TryIntersect(RouteCrossingSegment left,
            RouteCrossingSegment right, double endpointTolerance,
            out QuickLinePoint point, out double leftCenter, out double rightCenter)
        {
            point = default(QuickLinePoint);
            leftCenter = 0.0;
            rightCenter = 0.0;

            double ldx = left.End.X - left.Start.X;
            double ldy = left.End.Y - left.Start.Y;
            double rdx = right.End.X - right.Start.X;
            double rdy = right.End.Y - right.Start.Y;
            double denominator = ldx * rdy - ldy * rdx;
            if (Math.Abs(denominator) < ParallelEpsilon) return false;

            double wx = right.Start.X - left.Start.X;
            double wy = right.Start.Y - left.Start.Y;
            double t = (wx * rdy - wy * rdx) / denominator;
            double u = (wx * ldy - wy * ldx) / denominator;
            if (t <= 0.0 || t >= 1.0 || u <= 0.0 || u >= 1.0) return false;

            point = new QuickLinePoint(left.Start.X + ldx * t,
                left.Start.Y + ldy * t);
            leftCenter = t * left.Length;
            rightCenter = u * right.Length;

            double toleranceSquared = endpointTolerance * endpointTolerance;
            return point.DistanceSquaredTo(left.Start) > toleranceSquared
                && point.DistanceSquaredTo(left.End) > toleranceSquared
                && point.DistanceSquaredTo(right.Start) > toleranceSquared
                && point.DistanceSquaredTo(right.End) > toleranceSquared;
        }

        private static string FormatPoint(QuickLinePoint point)
            => "(" + point.X.ToString("0.#", CultureInfo.InvariantCulture) + ", "
                + point.Y.ToString("0.#", CultureInfo.InvariantCulture) + ")";
    }
}
