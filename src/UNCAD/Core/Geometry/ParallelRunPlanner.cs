using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;

namespace UNCAD.Core.Geometry
{
    /// <summary>
    /// 一条待合并的连续段成员（D-050，2026-11-09）。Cad 层把每条源曲线提取成
    /// 有序顶点链（弧段已按矢高细分），Core 只看二维点。Handle 是源实体句柄，
    /// 供写回 UNCAD_PARALLEL 元数据（RUN 字段）用。
    /// </summary>
    public sealed class ParallelRunMember
    {
        public string Handle = "";
        /// <summary>几何长度（毫米）；U1L 长度推算里作为"无标签段"的兜底。</summary>
        public double LengthMm;
        /// <summary>有序顶点（≥2）。首尾顶点即该成员的两个端点。</summary>
        public List<QuickLinePoint> Vertices = new List<QuickLinePoint>();
    }

    /// <summary>
    /// 一"趟"：端点连通的成员链（D-050）。Vertices 是沿链方向拼接后的完整顶点
    /// （弧已细分、junction 点去重）；TotalLengthMm 为成员几何长度之和。
    /// 非链状连通分量（T 形分叉/自环）不强行合并——每个成员退化为单成员趟，
    /// 保持逐段标注的旧行为，不猜。
    /// </summary>
    public sealed class ParallelRun
    {
        public IReadOnlyList<ParallelRunMember> Members = new List<ParallelRunMember>();
        public List<QuickLinePoint> Vertices = new List<QuickLinePoint>();
        public double TotalLengthMm;
        /// <summary>Cad 层的标注长度初值（U1L 标签推算 + 几何兜底，取整到 10）：
        /// 由 ParallelRunGeometry.EstimateRunLengthMm 在调用 LabelFactory 前填入；
        /// -1 = 未推算（调用方自行兜底）。用户生成后仍可手改。</summary>
        public double EstimatedLengthMm = -1;
        public bool Merged => Members.Count > 1;
    }

    /// <summary>
    /// 连续段分趟规划器（D-050）：端点容差连通分组（对齐 U1LX 的 1.0 容差口径）
    /// + 链排序。每个简单链连通分量 = 一趟；分叉分量拆回单成员趟。
    /// 纯 Core，无 AutoCAD 依赖，行为由 ParallelRunPlannerTests 锁定。
    /// </summary>
    public static class ParallelRunPlanner
    {
        /// <summary>默认端点容差：与 U1LX 图遍历一致（QuickLineCadService.EndpointTolerance）。</summary>
        public const double DefaultEndpointTolerance = 1.0;

        public static IReadOnlyList<ParallelRun> Plan(
            IReadOnlyList<ParallelRunMember> members, double endpointTolerance)
        {
            var runs = new List<ParallelRun>();
            if (members == null || members.Count == 0) return runs;
            if (endpointTolerance <= 0) endpointTolerance = DefaultEndpointTolerance;
            List<ParallelRunMember> valid = members.Where(IsValidMember).ToList();
            if (valid.Count == 0) return runs;

            // —— 端点聚类：pairwise 聚类（选集规模典型为几十条，O(n²) 足够）。
            var clusters = new List<List<int>>(); // 每簇=成员端点的 (memberIndex, isStart)
            var endpointAssignments = new int[valid.Count, 2];
            for (int i = 0; i < valid.Count; i++)
            {
                endpointAssignments[i, 0] = AssignEndpoint(clusters, valid, i, 0,
                    endpointTolerance);
                endpointAssignments[i, 1] = AssignEndpoint(clusters, valid, i, 1,
                    endpointTolerance);
            }
            // 自环（两端落进同一簇）按分叉处理：拆回单成员趟。
            bool[] degenerate = new bool[valid.Count];
            for (int i = 0; i < valid.Count; i++)
                degenerate[i] = endpointAssignments[i, 0] == endpointAssignments[i, 1];

            // —— 并查集连通分量。
            int[] parent = Enumerable.Range(0, valid.Count).ToArray();
            for (int c = 0; c < clusters.Count; c++)
            {
                List<int> endpoints = clusters[c];
                if (endpoints.Count < 2) continue;
                int first = endpoints[0] / 2;
                for (int e = 1; e < endpoints.Count; e++)
                    Union(parent, first, endpoints[e] / 2);
            }

            var components = valid.Select((_, i) => i)
                .GroupBy(i => Find(parent, i))
                .OrderBy(g => g.Min()) // 稳定输出顺序：按输入顺序聚合。
                .ToList();
            foreach (IGrouping<int, int> component in components)
            {
                List<int> indices = component.OrderBy(i => i).ToList();
                if (indices.Any(i => degenerate[i]) || !IsSimpleChain(clusters,
                        endpointAssignments, indices))
                {
                    // 分叉/自环：不猜——每个成员自成单段趟（等价旧行为）。
                    runs.AddRange(indices.Select(i => SingleMemberRun(valid[i])));
                    continue;
                }
                runs.Add(BuildChainRun(valid, clusters, endpointAssignments, indices));
            }
            return runs;
        }

        public static IReadOnlyList<ParallelRun> Plan(
            IReadOnlyList<ParallelRunMember> members)
            => Plan(members, DefaultEndpointTolerance);

        private static bool IsValidMember(ParallelRunMember member)
            => member != null && member.Vertices != null && member.Vertices.Count >= 2
                && !string.IsNullOrWhiteSpace(member.Handle);

        /// <summary>把成员端点并入既有簇（容差内取最近簇心）或新建簇。返回簇号。</summary>
        private static int AssignEndpoint(List<List<int>> clusters,
            List<ParallelRunMember> members, int memberIndex, int endpointIndex,
            double tolerance)
        {
            QuickLinePoint point = EndpointOf(members[memberIndex], endpointIndex);
            int best = -1;
            double bestDistance = double.PositiveInfinity;
            for (int c = 0; c < clusters.Count; c++)
            {
                QuickLinePoint centroid = ClusterCentroid(members, clusters[c]);
                double distance = point.DistanceTo(centroid);
                if (distance <= tolerance && distance < bestDistance)
                {
                    best = c;
                    bestDistance = distance;
                }
            }
            if (best < 0)
            {
                clusters.Add(new List<int> { memberIndex * 2 + endpointIndex });
                return clusters.Count - 1;
            }
            clusters[best].Add(memberIndex * 2 + endpointIndex);
            return best;
        }

        private static QuickLinePoint EndpointOf(ParallelRunMember member, int endpointIndex)
            => endpointIndex == 0
                ? member.Vertices[0]
                : member.Vertices[member.Vertices.Count - 1];

        private static QuickLinePoint ClusterCentroid(List<ParallelRunMember> members,
            List<int> cluster)
        {
            double x = 0, y = 0;
            foreach (int endpoint in cluster)
            {
                QuickLinePoint point = EndpointOf(members[endpoint / 2], endpoint % 2);
                x += point.X;
                y += point.Y;
            }
            return new QuickLinePoint(x / cluster.Count, y / cluster.Count);
        }

        private static bool IsSimpleChain(List<List<int>> clusters,
            int[,] endpointAssignments, List<int> indices)
        {
            var memberSet = new HashSet<int>(indices);
            foreach (int index in indices)
            {
                if (endpointAssignments[index, 0] == endpointAssignments[index, 1])
                    return false;
            }
            // 链状：每个簇恰好连接 0 或 2 个"度端点"；分叉=某簇连接 ≥3 个不同成员。
            foreach (int index in indices)
            {
                if (ClusterMemberCount(clusters, endpointAssignments[index, 0],
                        memberSet) > 2
                    || ClusterMemberCount(clusters, endpointAssignments[index, 1],
                        memberSet) > 2)
                    return false;
            }
            return true;
        }

        private static int ClusterMemberCount(List<List<int>> cluster,
            int clusterIndex, HashSet<int> memberSet)
            => cluster[clusterIndex].Select(e => e / 2).Count(memberSet.Contains);

        private static ParallelRun SingleMemberRun(ParallelRunMember member)
            => new ParallelRun
            {
                Members = new List<ParallelRunMember> { member },
                Vertices = new List<QuickLinePoint>(member.Vertices),
                TotalLengthMm = member.LengthMm
            };

        private static ParallelRun BuildChainRun(List<ParallelRunMember> members,
            List<List<int>> clusters, int[,] endpointAssignments, List<int> indices)
        {
            var memberSet = new HashSet<int>(indices);
                // 端簇=只连 1 个成员的簇；都没有则是闭环（随便起，保持首个成员方向）。
                List<int> endClusters = indices
                    .SelectMany(i => new[] { endpointAssignments[i, 0],
                        endpointAssignments[i, 1] })
                    .Distinct()
                    .Where(c => ClusterMemberCount(clusters, c, memberSet) == 1)
                    .ToList();
                int startCluster;
                if (endClusters.Count > 0)
                {
                    // 确定性起向：簇心字典序最小的端簇，保证同一路径重跑结果一致。
                    startCluster = endClusters.OrderBy(c =>
                        ClusterCentroid(members, clusters[c]).X).ThenBy(c =>
                        ClusterCentroid(members, clusters[c]).Y).First();
                }
                else
                {
                    startCluster = endpointAssignments[indices[0], 0];
                }

                var ordered = new List<ParallelRunMember>();
                var vertices = new List<QuickLinePoint>();
                var visited = new HashSet<int>();
                int firstMember = clusters[startCluster].Select(e => e / 2)
                    .First(memberSet.Contains);
                int current = firstMember;
                bool forward = endpointAssignments[current, 0] == startCluster;
                while (true)
                {
                    visited.Add(current);
                    ParallelRunMember member = members[current];
                    ordered.Add(member);
                    AppendVertices(vertices, member.Vertices, forward,
                        vertices.Count == 0);
                    int junction = forward
                        ? endpointAssignments[current, 1]
                        : endpointAssignments[current, 0];
                    int next = -1;
                    foreach (int endpoint in clusters[junction])
                    {
                        int candidate = endpoint / 2;
                        if (!memberSet.Contains(candidate) || visited.Contains(candidate))
                            continue;
                        next = candidate;
                        forward = endpointAssignments[candidate, 0] == junction;
                        break;
                    }
                    if (next < 0) break;
                    current = next;
                }
                return new ParallelRun
                {
                    Members = ordered,
                    Vertices = Dedupe(vertices),
                    TotalLengthMm = ordered.Sum(member => member.LengthMm)
                };
        }

        private static void AppendVertices(List<QuickLinePoint> target,
            List<QuickLinePoint> source, bool forward, bool includeFirst)
        {
            if (forward)
            {
                for (int i = includeFirst ? 0 : 1; i < source.Count; i++)
                    target.Add(source[i]);
            }
            else
            {
                // 反向遍历：入口在 source 末位（已由上一成员提供则跳过），
                // 出口是 source[0]——必须追加到底，否则丢顶点。
                for (int i = source.Count - 1; i >= 0; i--)
                {
                    if (!includeFirst && i == source.Count - 1) continue;
                    target.Add(source[i]);
                }
            }
        }

        private static List<QuickLinePoint> Dedupe(List<QuickLinePoint> points)
        {
            var result = new List<QuickLinePoint>(points.Count);
            foreach (QuickLinePoint point in points)
            {
                if (result.Count > 0
                    && result[result.Count - 1].DistanceTo(point) < 1e-7)
                    continue;
                result.Add(point);
            }
            return result;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[rb] = ra;
        }
    }
}
