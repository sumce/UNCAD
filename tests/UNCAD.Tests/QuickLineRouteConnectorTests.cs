using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-058 线路识别的纯逻辑锁死：断口容差相接（只有带毫米标的线段参与）、
    /// 连通性报告带接缝计数、无元数据桥架/线管文字就近绑定、标注线识别。
    /// </summary>
    public sealed class QuickLineRouteConnectorTests
    {
        private static QuickLineSegment Segment(string id, double x1, double y1,
            double x2, double y2)
            => new QuickLineSegment(id, new QuickLinePoint(x1, y1),
                new QuickLinePoint(x2, y2));

        [Fact]
        public void GapWithinTolerance_JoinsLabelledSegmentsAndIsReported()
        {
            // 两段看着连着，放大有 30 的缝。
            QuickLineRouteGraph route = QuickLineRouteConnector.Build(new[]
            {
                Segment("a", 0, 0, 3000, 0),
                Segment("b", 3030, 0, 6000, 0)
            }, 50.0);

            // GetConnections 只列从 a 出发的接缝（a 的终点 → b 的起点）。
            Assert.Single(route.Graph.GetConnections("a"));
            QuickLineGapJoin join = Assert.Single(route.GapJoins);
            Assert.Equal("a", join.SegmentId);
            Assert.Equal(QuickLineEndpoint.End, join.Endpoint);
            Assert.Equal("b", join.ConnectedSegmentId);
            Assert.Equal(QuickLineEndpoint.Start, join.ConnectedEndpoint);
            Assert.Equal(30.0, join.Gap, 6);
        }

        [Fact]
        public void GapBeyondTolerance_StaysOpen()
        {
            QuickLineRouteGraph route = QuickLineRouteConnector.Build(new[]
            {
                Segment("a", 0, 0, 3000, 0),
                Segment("b", 3060, 0, 6000, 0)
            }, 50.0);

            Assert.Empty(route.Graph.GetConnections("a"));
            Assert.Empty(route.GapJoins);
        }

        [Fact]
        public void ExactlyTouching_IsJoinedButNotReportedAsGap()
        {
            QuickLineRouteGraph route = QuickLineRouteConnector.Build(new[]
            {
                Segment("a", 0, 0, 3000, 0),
                Segment("b", 3000.4, 0, 6000, 0)
            }, 50.0);

            Assert.Single(route.Graph.GetConnections("a"));
            Assert.Empty(route.GapJoins);
        }

        [Fact]
        public void ZeroTolerance_KeepsStrictBehaviour()
        {
            QuickLineRouteGraph route = QuickLineRouteConnector.Build(new[]
            {
                Segment("a", 0, 0, 3000, 0),
                Segment("b", 3030, 0, 6000, 0)
            }, 0.0);

            Assert.Empty(route.Graph.GetConnections("a"));
            Assert.Empty(route.GapJoins);
        }

        [Fact]
        public void ThreeWayCorner_WithGapsBecomesOneNode()
        {
            // 拐角处三段都差一点才碰上：容差内并成一个节点。
            QuickLineRouteGraph route = QuickLineRouteConnector.Build(new[]
            {
                Segment("a", 0, 0, 3000, 0),
                Segment("b", 3030, 20, 3030, 5000),
                Segment("c", 3010, 10, 3010, -4000)
            }, 50.0);

            Assert.Equal(2, route.Graph.GetConnections("a").Count);
            Assert.Equal(2, route.Graph.GetConnections("b").Count);
            Assert.Equal(2, route.Graph.GetConnections("c").Count);
            Assert.Equal(3, route.GapJoins.Count);
        }

        [Fact]
        public void SceneBuilder_AcceptsTolerantGraph()
        {
            // 30° 斜线之后接一段竖线，拐角差 30 没碰上：接上之后整条路线进得了
            // 3D 场景（一个分支、两段、三个节点）。
            double cosine = Math.Cos(Math.PI / 6);
            double sine = Math.Sin(Math.PI / 6);
            QuickLineRouteGraph route = QuickLineRouteConnector.Build(new[]
            {
                Segment("a", 0, 0, 3000 * cosine, 3000 * sine),
                Segment("b", 3030 * cosine, 3030 * sine,
                    3030 * cosine, 3030 * sine + 2000)
            }, 50.0);
            Assert.Single(route.GapJoins);

            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildMetricPreview(
                route.Graph,
                new Dictionary<string, double> { ["a"] = 4800.0, ["b"] = 2500.0 },
                new HashSet<string>());
            Assert.Equal(1, scene.ComponentCount);
            Assert.Equal(2, scene.Segments.Count);
            Assert.Equal(3, scene.Nodes.Count);
        }

        [Fact]
        public void ClampGapTolerance_FallsBackAndCaps()
        {
            Assert.Equal(QuickLineRouteConnector.DefaultGapTolerance,
                QuickLineRouteConnector.ClampGapTolerance(double.NaN));
            Assert.Equal(QuickLineRouteConnector.DefaultGapTolerance,
                QuickLineRouteConnector.ClampGapTolerance(-5.0));
            Assert.Equal(0.0, QuickLineRouteConnector.ClampGapTolerance(0.0));
            Assert.Equal(80.0, QuickLineRouteConnector.ClampGapTolerance(80.0));
            Assert.Equal(QuickLineRouteConnector.MaximumGapTolerance,
                QuickLineRouteConnector.ClampGapTolerance(99999.0));
        }

        [Fact]
        public void InvalidTolerance_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                QuickLineRouteConnector.Build(new[] { Segment("a", 0, 0, 1, 0) }, -1.0));
            Assert.Throws<ArgumentNullException>(() =>
                QuickLineRouteConnector.Build(null, 50.0));
        }

        [Fact]
        public void RouteSettings_DefaultZeroCapAndGarbage()
        {
            // 没设 = 缺省 50；显式 0 保留（= 只用严格容差）；越界封顶；乱填/负数回落缺省。
            Assert.Equal(QuickLineRouteConnector.DefaultGapTolerance,
                UNCAD.Infra.QuickLineRouteSettings.ParseGapTolerance(null));
            Assert.Equal(QuickLineRouteConnector.DefaultGapTolerance,
                UNCAD.Infra.QuickLineRouteSettings.ParseGapTolerance("  "));
            Assert.Equal(0.0,
                UNCAD.Infra.QuickLineRouteSettings.ParseGapTolerance("0"));
            Assert.Equal(80.0,
                UNCAD.Infra.QuickLineRouteSettings.ParseGapTolerance(" 80 "));
            Assert.Equal(QuickLineRouteConnector.MaximumGapTolerance,
                UNCAD.Infra.QuickLineRouteSettings.ParseGapTolerance("99999"));
            Assert.Equal(QuickLineRouteConnector.DefaultGapTolerance,
                UNCAD.Infra.QuickLineRouteSettings.ParseGapTolerance("abc"));
            Assert.Equal(QuickLineRouteConnector.DefaultGapTolerance,
                UNCAD.Infra.QuickLineRouteSettings.ParseGapTolerance("-3"));
        }
    }
}
