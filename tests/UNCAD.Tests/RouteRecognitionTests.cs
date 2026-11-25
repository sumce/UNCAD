using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-058 线路识别锁死：连通性断口容差只作用于带毫米标的线段；路线两端按
    /// 「离上游块/设备块最近的末端」判定；桥架/线管文字的识别与就近绑定；
    /// 紧贴带通道电缆线段的平行线认作标注线。
    /// </summary>
    public sealed class RouteRecognitionTests
    {
        private static QuickLineSegment Segment(string id, double x1, double y1,
            double x2, double y2)
            => new QuickLineSegment(id, new QuickLinePoint(x1, y1),
                new QuickLinePoint(x2, y2));

        private static CircuitConnectivityAnalyzer.InputSegment Input(
            string id, double x1, double y1, double x2, double y2)
            => new CircuitConnectivityAnalyzer.InputSegment(id,
                new QuickLinePoint(x1, y1), new QuickLinePoint(x2, y2));

        // ---- 连通性：断口容差 --------------------------------------------------

        [Fact]
        public void Connectivity_TolerantJoinOnlyBetweenLabelledSegments()
        {
            var segments = new[]
            {
                Input("a", 0, 0, 3000, 0),        // 带毫米标
                Input("b", 3030, 0, 6000, 0),     // 带毫米标，与 a 差 30
                Input("c", 0, 25, 3000, 25),      // 没有毫米标，离 a 只有 25
                Input("d", 6030, 0, 9000, 0)      // 没有毫米标，离 b 只有 30
            };
            var labelled = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" };

            CircuitConnectivityReport report = CircuitConnectivityAnalyzer.Analyze(
                segments, 1.0, labelled.Contains, 50.0);

            // a–b 接成一条链；没有毫米标的 c、d 不参与容差，仍是孤立段。
            Assert.Equal(3, report.Components.Count);
            Assert.True(report.Components.Any(component =>
                component.SegmentIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(new[] { "a", "b" }, StringComparer.OrdinalIgnoreCase)),
                "a 与 b 应接成同一条链");
            Assert.Contains("c", report.IsolatedSegmentIds);
            Assert.Contains("d", report.IsolatedSegmentIds);
            QuickLineGapJoin join = Assert.Single(report.GapJoins);
            Assert.Equal(30.0, join.Gap, 6);
        }

        [Fact]
        public void Connectivity_ReportsGapCountInDisplayText()
        {
            CircuitConnectivityReport report = CircuitConnectivityAnalyzer.Analyze(new[]
            {
                Input("a", 0, 0, 3000, 0),
                Input("b", 3030, 0, 6000, 0)
            }, 1.0, id => true, 50.0);
            Assert.Equal("✔ 2 段一条链（容错接上 1 处断口）", report.ToDisplayText());

            CircuitConnectivityReport strict = CircuitConnectivityAnalyzer.Analyze(new[]
            {
                Input("a", 0, 0, 3000, 0),
                Input("b", 3030, 0, 6000, 0)
            }, 1.0, id => true, 0.0);
            // 容差为 0：两段各不相接，按孤立段点名（原有口径不变）。
            Assert.StartsWith("⚠ 孤立段：", strict.ToDisplayText(), StringComparison.Ordinal);
            Assert.Empty(strict.GapJoins);
        }

        // ---- 路线两端：离上游块/设备块最近的末端 ----------------------------------

        private static QuickLineIsometricScene ThreeSegmentScene()
        {
            double cosine = Math.Cos(Math.PI / 6);
            double sine = Math.Sin(Math.PI / 6);
            QuickLinePoint a = new QuickLinePoint(0, 0);
            QuickLinePoint b = new QuickLinePoint(3000 * cosine, 3000 * sine);
            QuickLinePoint c = new QuickLinePoint(3000 * cosine, 3000 * sine + 2000);
            QuickLinePoint d = new QuickLinePoint(c.X - 2000 * cosine, c.Y + 2000 * sine);
            QuickLineGraph graph = QuickLineGraph.Build(new[]
            {
                new QuickLineSegment("a", a, b),
                new QuickLineSegment("b", b, c),
                new QuickLineSegment("c", c, d)
            }, 1.0);
            return QuickLineIsometricSceneBuilder.BuildMetricPreview(graph,
                new Dictionary<string, double> { ["a"] = 4800.0, ["b"] = 3000.0, ["c"] = 6000.0 },
                new HashSet<string>());
        }

        private static List<QuickLineRouteEndpointCandidate> Endpoints(
            QuickLineIsometricScene scene, QuickLineGraph graph,
            bool terminalsOnly)
        {
            var result = new List<QuickLineRouteEndpointCandidate>();
            foreach (QuickLineSegment segment in graph.Segments)
            {
                QuickLineIsometricScene sceneForIds = scene;
                QuickLineIsometricSegment mapped = sceneForIds.Segments
                    .First(item => item.Id == segment.Id);
                if (!terminalsOnly || graph.GetConnections(segment.Id, QuickLineEndpoint.Start).Count == 0)
                    result.Add(new QuickLineRouteEndpointCandidate(segment.Id,
                        mapped.StartNodeId, segment.Start));
                if (!terminalsOnly || graph.GetConnections(segment.Id, QuickLineEndpoint.End).Count == 0)
                    result.Add(new QuickLineRouteEndpointCandidate(segment.Id,
                        mapped.EndNodeId, segment.End));
            }
            return result;
        }

        private static QuickLineGraph ThreeSegmentGraph()
        {
            double cosine = Math.Cos(Math.PI / 6);
            double sine = Math.Sin(Math.PI / 6);
            QuickLinePoint a = new QuickLinePoint(0, 0);
            QuickLinePoint b = new QuickLinePoint(3000 * cosine, 3000 * sine);
            QuickLinePoint c = new QuickLinePoint(3000 * cosine, 3000 * sine + 2000);
            QuickLinePoint d = new QuickLinePoint(c.X - 2000 * cosine, c.Y + 2000 * sine);
            return QuickLineGraph.Build(new[]
            {
                new QuickLineSegment("a", a, b),
                new QuickLineSegment("b", b, c),
                new QuickLineSegment("c", c, d)
            }, 1.0);
        }

        private static QuickLinePoint StartOf(QuickLineGraph graph, string id)
            => graph.GetSegment(id).Start;
        private static QuickLinePoint EndOf(QuickLineGraph graph, string id)
            => graph.GetSegment(id).End;

        [Fact]
        public void Anchors_UpstreamBlockNearestStart_DeviceBlockNearestEnd()
        {
            QuickLineGraph graph = ThreeSegmentGraph();
            QuickLineIsometricScene scene = ThreeSegmentScene();
            QuickLinePoint start = StartOf(graph, "a");
            QuickLinePoint end = EndOf(graph, "c");

            QuickLineRouteDirection direction = QuickLineRouteDirectionPlanner
                .ResolveFromAnchors(scene, Endpoints(scene, graph, true),
                    Endpoints(scene, graph, false),
                    new[] { new QuickLinePoint(start.X - 200, start.Y - 100) },
                    new[] { new QuickLinePoint(end.X - 100, end.Y + 200) },
                    null, null);

            Assert.True(direction.IsResolved);
            Assert.Equal(scene.Segments.First(item => item.Id == "a").StartNodeId,
                direction.UpstreamNodeId);
            Assert.Equal(scene.Segments.First(item => item.Id == "c").EndNodeId,
                direction.DownstreamNodeId);
            Assert.True(direction.Diagnostics.Any(text => text.StartsWith(
                    "上游端＝离上游块最近的路线末端", StringComparison.Ordinal)),
                string.Join("；", direction.Diagnostics));
            Assert.True(direction.Diagnostics.Any(text => text.StartsWith(
                    "设备端＝离设备块最近的路线末端", StringComparison.Ordinal)),
                string.Join("；", direction.Diagnostics));
        }

        [Fact]
        public void Anchors_DeviceBlockNearestStart_FlipsTheEnds()
        {
            // 设备块在起点一侧：起点就是设备端，另一头是上游端（不看线的绘制方向）。
            QuickLineGraph graph = ThreeSegmentGraph();
            QuickLineIsometricScene scene = ThreeSegmentScene();
            QuickLinePoint start = StartOf(graph, "a");
            QuickLinePoint end = EndOf(graph, "c");

            QuickLineRouteDirection direction = QuickLineRouteDirectionPlanner
                .ResolveFromAnchors(scene, Endpoints(scene, graph, true),
                    Endpoints(scene, graph, false),
                    new[] { new QuickLinePoint(end.X + 500, end.Y + 500) },
                    new[] { new QuickLinePoint(start.X - 100, start.Y - 100) },
                    null, null);

            Assert.True(direction.IsResolved);
            Assert.Equal(scene.Segments.First(item => item.Id == "a").StartNodeId,
                direction.DownstreamNodeId);
            Assert.Equal(scene.Segments.First(item => item.Id == "c").EndNodeId,
                direction.UpstreamNodeId);
        }

        [Fact]
        public void Anchors_OnlyDeviceBlock_OtherEndIsUpstream()
        {
            QuickLineGraph graph = ThreeSegmentGraph();
            QuickLineIsometricScene scene = ThreeSegmentScene();
            QuickLinePoint end = EndOf(graph, "c");

            QuickLineRouteDirection direction = QuickLineRouteDirectionPlanner
                .ResolveFromAnchors(scene, Endpoints(scene, graph, true),
                    Endpoints(scene, graph, false),
                    new QuickLinePoint[0],
                    new[] { new QuickLinePoint(end.X, end.Y + 300) },
                    null, null);

            Assert.True(direction.IsResolved);
            Assert.Equal(scene.Segments.First(item => item.Id == "c").EndNodeId,
                direction.DownstreamNodeId);
            Assert.Equal(scene.Segments.First(item => item.Id == "a").StartNodeId,
                direction.UpstreamNodeId);
        }

        [Fact]
        public void Anchors_SameTerminalForBothBlocks_IsUnresolved()
        {
            // 两个块都离同一个末端最近：说不清，不猜。
            QuickLineGraph graph = ThreeSegmentGraph();
            QuickLineIsometricScene scene = ThreeSegmentScene();
            QuickLinePoint start = StartOf(graph, "a");

            QuickLineRouteDirection direction = QuickLineRouteDirectionPlanner
                .ResolveFromAnchors(scene, Endpoints(scene, graph, true),
                    Endpoints(scene, graph, false),
                    new[] { new QuickLinePoint(start.X - 300, start.Y) },
                    new[] { new QuickLinePoint(start.X + 300, start.Y) },
                    null, null);

            Assert.False(direction.IsResolved);
        }

        [Fact]
        public void Anchors_NoBlocks_FallsBackToWorkbookAxes()
        {
            QuickLineGraph graph = ThreeSegmentGraph();
            QuickLineIsometricScene scene = ThreeSegmentScene();
            QuickLineRouteDirection byAxes = QuickLineRouteDirectionPlanner
                .Resolve(scene, "48/V", "47/V");
            Assert.True(byAxes.IsResolved);

            QuickLineRouteDirection direction = QuickLineRouteDirectionPlanner
                .ResolveFromAnchors(scene, Endpoints(scene, graph, true),
                    Endpoints(scene, graph, false),
                    new QuickLinePoint[0], new QuickLinePoint[0],
                    "48/V", "47/V");

            Assert.True(direction.IsResolved);
            Assert.Equal(byAxes.UpstreamNodeId, direction.UpstreamNodeId);
            Assert.Equal(byAxes.DownstreamNodeId, direction.DownstreamNodeId);
        }

        [Fact]
        public void Anchors_AmbiguousDistances_AreNotGuessed()
        {
            // 只有一段线、两个末端离设备块一样远：设备端定不下来。
            QuickLineGraph graph = QuickLineGraph.Build(new[]
            {
                Segment("a", 0, 0, 4000, 0)
            }, 1.0);
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder
                .BuildMetricPreview(graph,
                    new Dictionary<string, double> { ["a"] = 4800.0 },
                    new HashSet<string>());

            QuickLineRouteDirection direction = QuickLineRouteDirectionPlanner
                .ResolveFromAnchors(scene, Endpoints(scene, graph, true),
                    Endpoints(scene, graph, false),
                    new QuickLinePoint[0],
                    new[] { new QuickLinePoint(2000, 0) },
                    null, null);

            Assert.False(direction.IsResolved);
            Assert.True(direction.Diagnostics.Any(text =>
                    text.IndexOf("几乎相同", StringComparison.Ordinal) >= 0),
                string.Join("；", direction.Diagnostics));
        }

        [Fact]
        public void Anchors_MultipleBlocks_UseTheNearestBlock()
        {
            // 同类块有多个：按最近那个块位置计距。
            QuickLineGraph graph = QuickLineGraph.Build(new[]
            {
                Segment("a", 0, 0, 4000, 0)
            }, 1.0);
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder
                .BuildMetricPreview(graph,
                    new Dictionary<string, double> { ["a"] = 4800.0 },
                    new HashSet<string>());

            QuickLineRouteDirection direction = QuickLineRouteDirectionPlanner
                .ResolveFromAnchors(scene, Endpoints(scene, graph, true),
                    Endpoints(scene, graph, false),
                    new[] { new QuickLinePoint(-5000, 9000), new QuickLinePoint(-200, 0) },
                    new[] { new QuickLinePoint(5000, 9000), new QuickLinePoint(4200, 0) },
                    null, null);

            Assert.True(direction.IsResolved);
            Assert.Equal(scene.Segments[0].StartNodeId, direction.UpstreamNodeId);
            Assert.Equal(scene.Segments[0].EndNodeId, direction.DownstreamNodeId);
        }

        // ---- 桥架/线管文字 ------------------------------------------------------

        [Theory]
        [InlineData("桥架200*100 2500mm", RouteAnnotationTextKind.Tray)]
        [InlineData("桥架200*100 10格", RouteAnnotationTextKind.Tray)]
        [InlineData("桥架200*100", RouteAnnotationTextKind.Tray)]
        [InlineData("⌀20线管 2000mm", RouteAnnotationTextKind.Conduit)]
        [InlineData("线管25 3000mm", RouteAnnotationTextKind.Conduit)]
        [InlineData("Ø38线管", RouteAnnotationTextKind.Conduit)]
        [InlineData("镀锌桥架200*100\\P2500mm", RouteAnnotationTextKind.Tray)]
        [InlineData("镀锌穿线管38mm\\P4000mm", RouteAnnotationTextKind.Conduit)]
        [InlineData("2000mm", RouteAnnotationTextKind.None)]
        [InlineData("", RouteAnnotationTextKind.None)]
        [InlineData("100mm软管:2500mm", RouteAnnotationTextKind.None)]
        [InlineData("桥架 200*100 2500mm 预留", RouteAnnotationTextKind.None)]
        // 3.8.0.3：单行、长度写在行尾的旧写法（现场「梯形桥架200Wx100H-1300mm」
        // 被报成「毫米标没有归属线段」）。
        [InlineData("梯形桥架200Wx100H-1300mm", RouteAnnotationTextKind.Tray)]
        [InlineData("梯形桥架200Wx100H 1300mm", RouteAnnotationTextKind.Tray)]
        [InlineData("镀锌穿线管SC25-3000mm", RouteAnnotationTextKind.Conduit)]
        // 关键字后面是别的说明（没有规格）：不算标注。
        [InlineData("桥架内穿管 20mm", RouteAnnotationTextKind.None)]
        // 规格以数字结尾时，长度前必须有分隔符——不能把 2500 拆成「250 + 0mm」。
        [InlineData("桥架250 0mm", RouteAnnotationTextKind.None)]
        // 用户口径（2026-11-25）「共用桥架不用管」：共用桥架维持原样，不进这条规则。
        [InlineData("共用桥架200*100 3000mm", RouteAnnotationTextKind.None)]
        public void Text_ClassifiesTrayAndConduitLabels(string text,
            RouteAnnotationTextKind expected)
        {
            Assert.Equal(expected, RouteAnnotationTextPlanner.Classify(text));
        }

        private static RouteAnnotationTextStamp Text(string id, string content,
            double x, double y, double? rotation, bool owned = false,
            RouteAnnotationTextKind? kind = null)
            => new RouteAnnotationTextStamp(id, content, new QuickLinePoint(x, y),
                rotation, owned,
                kind ?? RouteAnnotationTextPlanner.Classify(content));

        [Fact]
        public void Text_BindsToTheNearestParallelLabelledSegment()
        {
            var labelled = new List<QuickLineSegment>
            {
                Segment("s1", 0, 0, 4000, 0),
                Segment("s2", 0, 600, 4000, 600)
            };
            IReadOnlyList<RouteAnnotationTextBinding> bindings = RouteAnnotationTextPlanner.Bind(
                new[]
                {
                    Text("t1", "桥架200*100 2500mm", 2000, 30, 0.0),
                    Text("t2", "⌀20线管 2000mm", 2000, 330, 0.0)
                }, labelled);

            Assert.Equal(2, bindings.Count);
            RouteAnnotationTextBinding tray = bindings.Single(item => item.Kind == RouteAnnotationTextKind.Tray);
            RouteAnnotationTextBinding conduit = bindings.Single(item => item.Kind == RouteAnnotationTextKind.Conduit);
            Assert.Equal("s1", tray.SegmentId);
            Assert.Equal(200.0, tray.TrayWidthMillimetres);
            Assert.False(tray.TrayWidthAssumedDefault);
            Assert.Equal("s2", conduit.SegmentId);
        }

        [Fact]
        public void Text_TwoLineCatalogLabel_BindsWithAssumedWidth()
        {
            // 两行清单型号 + 长度：解析不出规格宽度，按缺省 200 并标记。
            IReadOnlyList<RouteAnnotationTextBinding> bindings = RouteAnnotationTextPlanner.Bind(
                new[] { Text("t1", "镀锌桥架\\P2500mm", 2000, 30, 0.0) },
                new[] { Segment("s1", 0, 0, 4000, 0) });
            RouteAnnotationTextBinding binding = Assert.Single(bindings);
            Assert.Null(binding.TrayWidthMillimetres);
            Assert.True(binding.TrayWidthAssumedDefault);
        }

        [Fact]
        public void Text_WithOwnershipMetadata_IsLeftToTheExactOwnership()
        {
            IReadOnlyList<RouteAnnotationTextBinding> bindings = RouteAnnotationTextPlanner.Bind(
                new[] { Text("t1", "桥架200*100 2500mm", 2000, 30, 0.0, owned: true) },
                new[] { Segment("s1", 0, 0, 4000, 0) });
            Assert.Empty(bindings);
        }

        [Fact]
        public void Text_DoesNotOverrideASegmentThatAlreadyHasAChannel()
        {
            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "s1" };
            IReadOnlyList<RouteAnnotationTextBinding> bindings = RouteAnnotationTextPlanner.Bind(
                new[] { Text("t1", "桥架200*100 2500mm", 2000, 30, 0.0) },
                new[] { Segment("s1", 0, 0, 4000, 0) }, owned);
            Assert.Empty(bindings);
        }

        [Fact]
        public void Text_AmbiguousFarOrSkewed_IsNotBound()
        {
            var labelled = new List<QuickLineSegment>
            {
                Segment("s1", 0, 0, 4000, 0),
                Segment("s2", 0, 600, 4000, 600)
            };
            // 两条线正中间、离线太远、文字与线不平行、文字落在线段两端之外。
            RouteAnnotationTextStamp[] texts =
            {
                Text("mid", "桥架200*100 2500mm", 2000, 300, 0.0),
                Text("far", "桥架200*100 2500mm", 2000, 2000, 0.0),
                Text("skew", "桥架200*100 2500mm", 2000, 30, Math.PI / 4),
                Text("beyond", "桥架200*100 2500mm", 9000, 30, 0.0)
            };
            Assert.Empty(RouteAnnotationTextPlanner.Bind(texts, labelled));
        }

        // ---- 标注线 ------------------------------------------------------------

        [Fact]
        public void OffsetLine_ParallelNeighbourOfAChannelHostIsAnAnnotationLine()
        {
            var host = new[] { Segment("host", 0, 0, 4000, 0) };
            Assert.Equal("trayLine", Assert.Single(RouteAnnotationLineClassifier.FindOffsetLines(
                new[] { Segment("trayLine", 10, 15, 3990, 15) }, host)));

            Assert.Empty(RouteAnnotationLineClassifier.FindOffsetLines(
                new[] { Segment("far", 0, 300, 4000, 300) }, host));              // 离得太远
            Assert.Empty(RouteAnnotationLineClassifier.FindOffsetLines(
                new[] { Segment("noOverlap", -3000, 15, -500, 15) }, host));      // 不重叠
            Assert.Empty(RouteAnnotationLineClassifier.FindOffsetLines(
                new[] { Segment("tooLong", 0, 15, 9000, 15) }, host));            // 大幅超出
            Assert.Empty(RouteAnnotationLineClassifier.FindOffsetLines(
                new[] { Segment("tilted", 0, 0, 4000, 400) }, host));             // 不平行
        }
    }
}
