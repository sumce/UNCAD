using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// D-058：一处被断口容差接上的接缝。两端都是带毫米标的线段，端点原来
    /// 差 <see cref="Gap"/> 个图形单位（大于严格容差、不超过容差上限）。
    /// </summary>
    public sealed class QuickLineGapJoin
    {
        internal QuickLineGapJoin(string segmentId, QuickLineEndpoint endpoint,
            string connectedSegmentId, QuickLineEndpoint connectedEndpoint,
            double gap)
        {
            SegmentId = segmentId ?? string.Empty;
            Endpoint = endpoint;
            ConnectedSegmentId = connectedSegmentId ?? string.Empty;
            ConnectedEndpoint = connectedEndpoint;
            Gap = gap;
        }

        public string SegmentId { get; }
        public QuickLineEndpoint Endpoint { get; }
        public string ConnectedSegmentId { get; }
        public QuickLineEndpoint ConnectedEndpoint { get; }
        /// <summary>断口宽度（图形单位）。</summary>
        public double Gap { get; }
    }

    /// <summary>一条路线的端点图：端点在容差内即视为相接。</summary>
    public sealed class QuickLineRouteGraph
    {
        internal QuickLineRouteGraph(QuickLineGraph graph, double gapTolerance,
            IReadOnlyList<QuickLineGapJoin> gapJoins)
        {
            Graph = graph;
            GapTolerance = gapTolerance;
            GapJoins = gapJoins;
        }

        public QuickLineGraph Graph { get; }
        /// <summary>本次使用的断口容差（图形单位；低于严格容差时按严格容差）。</summary>
        public double GapTolerance { get; }
        /// <summary>靠容差才接上的接缝（严格容差内本来就相接的不算）。</summary>
        public IReadOnlyList<QuickLineGapJoin> GapJoins { get; }
        public bool HasGapJoins => GapJoins.Count > 0;
    }

    /// <summary>
    /// D-058：路线断口容差。手绘草图里“看着连着、放大才看出差一点”的线段，
    /// 只要两端都带毫米标，端点距离不超过容差就接成一条路线。
    ///
    /// 只收带毫米标的线段：没有毫米标的线段是不是电缆线都说不准，不参与
    /// 容差相接（它仍然按严格容差参与连通性报告）。U1LX 逐段填写与 Xauto
    /// 重排会改动几何，不走这条容差（它们继续用严格容差）。
    /// </summary>
    public static class QuickLineRouteConnector
    {
        /// <summary>严格容差：U1LX / Xauto / 图层矫正一直沿用的端点容差。</summary>
        public const double StrictTolerance = 1.0;
        /// <summary>断口容差缺省值（图形单位，U1SET「线路识别」可改）。</summary>
        public const double DefaultGapTolerance = 50.0;
        /// <summary>断口容差上限——再大就会把并排的两条线当成相接。</summary>
        public const double MaximumGapTolerance = 500.0;

        /// <summary>
        /// 用带毫米标的线段建路线端点图。gapTolerance 为 0 时等同严格容差。
        /// </summary>
        public static QuickLineRouteGraph Build(IEnumerable<QuickLineSegment> segments,
            double gapTolerance)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            if (double.IsNaN(gapTolerance) || double.IsInfinity(gapTolerance)
                || gapTolerance < 0.0)
                throw new ArgumentOutOfRangeException(nameof(gapTolerance));
            List<QuickLineSegment> list = segments.Where(item => item != null).ToList();
            QuickLineGraph graph = QuickLineGraph.Build(list,
                Math.Max(gapTolerance, StrictTolerance));

            var seenPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var joins = new List<QuickLineGapJoin>();
            foreach (QuickLineSegment segment in graph.Segments)
            {
                foreach (QuickLineConnection connection in graph.GetConnections(segment.Id))
                {
                    if (!seenPairs.Add(PairKey(segment.Id,
                        connection.ConnectedSegmentId))) continue;
                    QuickLinePoint here = segment.PointAt(connection.Endpoint);
                    QuickLinePoint there = graph.GetSegment(connection.ConnectedSegmentId)
                        .PointAt(connection.ConnectedEndpoint);
                    double gap = here.DistanceTo(there);
                    if (gap <= StrictTolerance) continue;
                    joins.Add(new QuickLineGapJoin(segment.Id,
                        connection.Endpoint, connection.ConnectedSegmentId,
                        connection.ConnectedEndpoint, gap));
                }
            }
            joins.Sort(CompareJoins);
            return new QuickLineRouteGraph(graph, gapTolerance, joins);
        }

        /// <summary>把容差归到合法范围内；非法值回落缺省。</summary>
        public static double ClampGapTolerance(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
                return DefaultGapTolerance;
            return Math.Min(value, MaximumGapTolerance);
        }

        private static string PairKey(string left, string right)
            => string.CompareOrdinal(left.ToUpperInvariant(), right.ToUpperInvariant()) <= 0
                ? left + "|" + right : right + "|" + left;

        private static int CompareJoins(QuickLineGapJoin left, QuickLineGapJoin right)
        {
            int order = string.Compare(left.SegmentId, right.SegmentId,
                StringComparison.OrdinalIgnoreCase);
            if (order != 0) return order;
            order = string.Compare(left.ConnectedSegmentId, right.ConnectedSegmentId,
                StringComparison.OrdinalIgnoreCase);
            if (order != 0) return order;
            return left.Endpoint.CompareTo(right.Endpoint);
        }
    }
}
