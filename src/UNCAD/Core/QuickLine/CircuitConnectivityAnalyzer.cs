using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UNCAD.Core.QuickLine
{
    /// <summary>3.3.0（D-054）：一个连通分量（段 id 集合，输入顺序稳定）。</summary>
    public sealed class CircuitConnectivityComponent
    {
        internal CircuitConnectivityComponent(List<string> segmentIds)
        {
            SegmentIds = segmentIds ?? new List<string>();
        }

        public IReadOnlyList<string> SegmentIds { get; }
        public int Count => SegmentIds.Count;
    }

    /// <summary>3.3.0（D-054）：一个图框（或任意范围）的连通性体检结果。</summary>
    public sealed class CircuitConnectivityReport
    {
        internal CircuitConnectivityReport(
            List<CircuitConnectivityComponent> components,
            List<string> isolatedSegmentIds, int totalSegments,
            IReadOnlyList<QuickLineGapJoin> gapJoins = null)
        {
            Components = components;
            IsolatedSegmentIds = isolatedSegmentIds;
            TotalSegments = totalSegments;
            GapJoins = gapJoins ?? Array.Empty<QuickLineGapJoin>();
        }

        public IReadOnlyList<CircuitConnectivityComponent> Components { get; }
        /// <summary>孤立段（两端都没有邻居，度数=0）。注意：范围里唯一的
        /// 单段路线也度数为 0，但它是合法路线（一条链的退化情形），只在
        /// 范围里还有其它分量时才作为「孤立段」点名。</summary>
        public IReadOnlyList<string> IsolatedSegmentIds { get; }
        public int TotalSegments { get; }
        /// <summary>D-058：靠断口容差才接上的接缝（两侧都是带毫米标的线段）。</summary>
        public IReadOnlyList<QuickLineGapJoin> GapJoins { get; }
        /// <summary>整个范围恰为一个连通分量（单段路线也算一条链）。</summary>
        public bool IsSingleChain => Components.Count == 1 && TotalSegments > 0;

        /// <summary>
        /// 确认窗「连通性」列文案：✔ N 段一条链（单段路线也算一条链）/
        /// ⚠ 孤立段：句柄… / N 段 M 条链（不拦截，报告级——D-054）。
        /// D-058：有断口被容差接上时在后面注明处数。
        /// </summary>
        public string ToDisplayText()
        {
            if (TotalSegments == 0) return "—";
            string gap = GapJoins.Count > 0
                ? "（容错接上 " + GapJoins.Count + " 处断口）" : string.Empty;
            if (Components.Count == 1)
                return "✔ " + TotalSegments + " 段一条链" + gap;
            if (IsolatedSegmentIds.Count > 0)
                return "⚠ 孤立段：" + JoinHandles(IsolatedSegmentIds) + gap;
            return TotalSegments + " 段 " + Components.Count + " 条链" + gap;
        }

        private static string JoinHandles(IReadOnlyList<string> ids)
        {
            const int shown = 4;
            var text = new StringBuilder();
            foreach (string id in ids.Take(shown))
            {
                if (text.Length > 0) text.Append("、");
                text.Append(id);
            }
            if (ids.Count > shown) text.Append(" 等 ").Append(ids.Count).Append(" 段");
            return text.ToString();
        }
    }

    /// <summary>
    /// 3.3.0（D-054）：回路连通性纯分析器——输入段的 id + 起终点，端点容差
    /// 沿用 QuickLineGraph 的口径（U1LX 同款），输出连通分量、孤立段
    /// （度数=0）与「每段至少一端相接」违规清单。只分析、不拦截。
    ///
    /// D-058：可选断口容差——hasMillimetreLabel 与 gapTolerance 都给出时，
    /// 两端都带毫米标且端点距离不超过 gapTolerance 的线段也接成一条链
    /// （接缝记进 <see cref="CircuitConnectivityReport.GapJoins"/>）；没有
    /// 毫米标的线段仍只按 endpointTolerance 相接。
    /// </summary>
    public static class CircuitConnectivityAnalyzer
    {
        public sealed class InputSegment
        {
            public InputSegment(string id, QuickLinePoint start, QuickLinePoint end)
            {
                if (string.IsNullOrWhiteSpace(id))
                    throw new ArgumentException("A segment id is required.", "id");
                Id = id;
                Start = start;
                End = end;
            }

            public string Id { get; }
            public QuickLinePoint Start { get; }
            public QuickLinePoint End { get; }
        }

        /// <summary>分析一组段；tolerance 与 QuickLineGraph.Build 相同语义。</summary>
        public static CircuitConnectivityReport Analyze(
            IEnumerable<InputSegment> segments, double endpointTolerance = 1.0,
            Func<string, bool> hasMillimetreLabel = null, double gapTolerance = 0.0)
        {
            if (segments == null)
                throw new ArgumentNullException(nameof(segments));
            if (double.IsNaN(endpointTolerance) || double.IsInfinity(endpointTolerance)
                || endpointTolerance < 0)
                throw new ArgumentOutOfRangeException(nameof(endpointTolerance),
                    "Endpoint tolerance must be finite and non-negative.");
            if (double.IsNaN(gapTolerance) || double.IsInfinity(gapTolerance)
                || gapTolerance < 0)
                throw new ArgumentOutOfRangeException(nameof(gapTolerance),
                    "Gap tolerance must be finite and non-negative.");

            List<InputSegment> input = segments.Where(item => item != null).ToList();
            QuickLineGraph graph = QuickLineGraph.Build(
                input.Select(ToSegment), endpointTolerance);

            QuickLineRouteGraph tolerant = null;
            if (hasMillimetreLabel != null && gapTolerance > endpointTolerance)
            {
                List<QuickLineSegment> labelled = input
                    .Where(item => hasMillimetreLabel(item.Id))
                    .Select(ToSegment).ToList();
                tolerant = QuickLineRouteConnector.Build(labelled, gapTolerance);
            }

            var adjacency = new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineSegment segment in graph.Segments)
                adjacency[segment.Id] = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineSegment segment in graph.Segments)
                foreach (QuickLineConnection connection in graph.GetConnections(segment.Id))
                    Connect(adjacency, segment.Id, connection.ConnectedSegmentId);
            if (tolerant != null)
                foreach (QuickLineSegment segment in tolerant.Graph.Segments)
                    foreach (QuickLineConnection connection in
                        tolerant.Graph.GetConnections(segment.Id))
                        Connect(adjacency, segment.Id, connection.ConnectedSegmentId);

            var isolated = new List<string>();
            foreach (QuickLineSegment segment in graph.Segments)
                if (adjacency[segment.Id].Count == 0)
                    isolated.Add(segment.Id);

            var components = new List<CircuitConnectivityComponent>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineSegment segment in graph.Segments)
            {
                if (visited.Contains(segment.Id)) continue;
                var memberIds = new List<string>();
                var queue = new Queue<string>();
                visited.Add(segment.Id);
                queue.Enqueue(segment.Id);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    memberIds.Add(current);
                    foreach (string neighbour in adjacency[current])
                        if (visited.Add(neighbour))
                            queue.Enqueue(neighbour);
                }
                components.Add(new CircuitConnectivityComponent(memberIds));
            }
            return new CircuitConnectivityReport(components, isolated,
                graph.Segments.Count, tolerant?.GapJoins);
        }

        private static QuickLineSegment ToSegment(InputSegment item)
            => new QuickLineSegment(item.Id, item.Start, item.End);

        private static void Connect(IDictionary<string, HashSet<string>> adjacency,
            string left, string right)
        {
            if (!adjacency.ContainsKey(left) || !adjacency.ContainsKey(right)) return;
            adjacency[left].Add(right);
            adjacency[right].Add(left);
        }
    }
}
