using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1L3D/U13D 场景关键路径的性能回归：端点图连接视图必须复用
    /// （遍历算法每次调用不再新建集合），并且大规模场景输出与
    /// 单组件构建完全等价。
    /// </summary>
    public sealed class QuickLinePerformanceTests
    {
        [Fact]
        public void Graph_ConnectionViewsAreCachedPerSegmentAndEndpoint()
        {
            QuickLineGraph graph = QuickLineGraph.Build(Chain(4), 1.0);

            IReadOnlyList<QuickLineConnection> first = graph.GetConnections("S0");
            IReadOnlyList<QuickLineConnection> second = graph.GetConnections("S0");
            Assert.Same(first, second);

            Assert.Same(graph.GetConnections("S0", QuickLineEndpoint.Start),
                graph.GetConnections("S0", QuickLineEndpoint.Start));
            Assert.Same(graph.GetConnections("S0", QuickLineEndpoint.End),
                graph.GetConnections("S0", QuickLineEndpoint.End));
            Assert.Same(graph.GetConnections("missing"), graph.GetConnections("also-missing"));

            // 端点过滤视图与整表语义一致。
            Assert.Equal(graph.GetConnections("S1").Count,
                graph.GetConnections("S1", QuickLineEndpoint.Start).Count
                + graph.GetConnections("S1", QuickLineEndpoint.End).Count);
        }

        [Fact]
        public void BuildAll_LargeRouteKeepsEverySegmentAndIsDeterministic()
        {
            List<QuickLineSegment> segments = Chain(1000);
            QuickLineGraph graph = QuickLineGraph.Build(segments, 1.0);
            Dictionary<string, double> distances = Distances(segments);

            QuickLineIsometricScene first = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, distances, new HashSet<string>());
            QuickLineIsometricScene second = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, distances, new HashSet<string>());

            Assert.Equal(segments.Count, first.Segments.Count);
            Assert.Equal(segments.Count + 1, first.Nodes.Count);
            Assert.Equal(first.Segments.Select(item => item.Id),
                second.Segments.Select(item => item.Id));
            Assert.Equal(first.Nodes.Select(item => item.Id),
                second.Nodes.Select(item => item.Id));
            Assert.Equal(first.Nodes.Select(item => item.Position),
                second.Nodes.Select(item => item.Position));
            Assert.Equal(segments.Count, first.Segments.Select(item => item.Id).Distinct().Count());
        }

        [Fact]
        public void BuildAll_MatchesSingleComponentBuild()
        {
            List<QuickLineSegment> segments = Chain(12);
            QuickLineGraph graph = QuickLineGraph.Build(segments, 1.0);
            Dictionary<string, double> distances = Distances(segments);
            QuickLineIsometricScene component = QuickLineIsometricSceneBuilder.Build(
                graph, segments[0].Id, distances);
            QuickLineIsometricScene all = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, distances, new HashSet<string>());

            Assert.Equal(component.Segments.Select(item => item.Id),
                all.Segments.Select(item => item.Id));
            for (int index = 0; index < component.Segments.Count; index++)
            {
                Assert.Equal(component.Segments[index].Axis, all.Segments[index].Axis);
                Assert.Equal(component.Segments[index].DirectionSign,
                    all.Segments[index].DirectionSign);
                Assert.Equal(component.Segments[index].DistanceMillimetres,
                    all.Segments[index].DistanceMillimetres, 8);
            }
            Assert.Equal(component.Nodes.Count, all.Nodes.Count);
            // BuildAll 给节点加 C1_ 前缀，位置必须在第一个分支上保持不变。
            foreach (QuickLineIsometricNode node in component.Nodes)
            {
                QuickLineIsometricNode mapped = all.Nodes.Single(item =>
                    item.Id == "C1_" + node.Id);
                Assert.Equal(node.Position, mapped.Position);
            }
        }

        [Fact]
        public void Traversal_PlanOnLongChainCoversEverySegmentOnce()
        {
            List<QuickLineSegment> segments = Chain(500);
            QuickLineGraph graph = QuickLineGraph.Build(segments, 1.0);

            QuickLineTraversalPlan plan = QuickLineTraversal.CreatePlan(graph,
                segments[0].Id, new QuickLinePoint(0, 0));

            Assert.Equal(segments.Count, plan.Steps.Count);
            Assert.Equal(segments.Count,
                plan.Steps.Select(step => step.SegmentId).Distinct().Count());
        }

        private static List<QuickLineSegment> Chain(int count)
        {
            var segments = new List<QuickLineSegment>(count);
            for (int index = 0; index < count; index++)
            {
                double x = index * 100.0 * Math.Cos(Math.PI / 6.0);
                double y = index * 100.0 * Math.Sin(Math.PI / 6.0);
                segments.Add(new QuickLineSegment("S" + index,
                    new QuickLinePoint(x, y),
                    new QuickLinePoint(x + 100.0 * Math.Cos(Math.PI / 6.0),
                        y + 100.0 * Math.Sin(Math.PI / 6.0))));
            }
            return segments;
        }

        private static Dictionary<string, double> Distances(
            IEnumerable<QuickLineSegment> segments)
        {
            var distances = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineSegment segment in segments)
                distances[segment.Id] = 1000.0;
            return distances;
        }
    }
}
