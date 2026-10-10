using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLine3dPreviewTests
    {
        [Fact]
        public void AxisGrid_Uses4800MillimetresBetweenWorkbookAxes()
        {
            QuickLineAxisGrid grid = QuickLineAxisGridPlanner.Plan("54/X", "54/W");

            Assert.Equal(4800, grid.SpacingMillimetres);
            Assert.Equal(new[] { "54" }, grid.NumericMarks.Select(item => item.Label));
            Assert.Equal(new[] { "W", "X" },
                grid.AlphabeticMarks.Select(item => item.Label));
            Assert.Equal(new[] { 0d, 4800d },
                grid.AlphabeticMarks.Select(item => item.OffsetMillimetres));
            Assert.Empty(grid.Diagnostics);
        }

        [Fact]
        public void AxisGrid_KeepsEastWestNumericAndNorthSouthAlphabeticDirections()
        {
            QuickLineAxisGrid grid = QuickLineAxisGridPlanner.Plan("56/X", "54/W");

            Assert.Equal(new[] { "54", "55", "56" },
                grid.NumericMarks.Select(item => item.Label));
            Assert.Equal(new[] { 0d, 4800d, 9600d },
                grid.NumericMarks.Select(item => item.OffsetMillimetres));
            Assert.Equal(new[] { "W", "X" },
                grid.AlphabeticMarks.Select(item => item.Label));
            Assert.Equal(new[] { 0d, 4800d },
                grid.AlphabeticMarks.Select(item => item.OffsetMillimetres));
        }

        [Fact]
        public void AxisGrid_ExpandsExcelStyleLetterAxesAcrossZ()
        {
            QuickLineAxisGrid grid = QuickLineAxisGridPlanner.Plan("54/AA", "54/W");

            Assert.Equal(new[] { "W", "X", "Y", "Z", "AA" },
                grid.AlphabeticMarks.Select(item => item.Label));
            Assert.Equal(19200, grid.AlphabeticMarks.Last().OffsetMillimetres);
        }

        [Fact]
        public void AxisGrid_UnparseableWorkbookValueDoesNotInventRange()
        {
            QuickLineAxisGrid grid = QuickLineAxisGridPlanner.Plan("化学实验室", "54/W");

            Assert.Single(grid.NumericMarks);
            Assert.Equal("54", grid.NumericMarks[0].Label);
            Assert.Equal(0, grid.NumericMarks[0].OffsetMillimetres);
            Assert.Single(grid.AlphabeticMarks);
            Assert.Equal("W", grid.AlphabeticMarks[0].Label);
            Assert.NotEmpty(grid.Diagnostics);
        }

        [Fact]
        public void AxisPlacement_AlignsFramesByDownstreamAxesNotSelectionOrder()
        {
            IReadOnlyList<QuickLineAxisPlacement> placements =
                QuickLineAxisPlacementPlanner.Plan(new[]
                {
                    new QuickLineAxisPlacementInput("B", "55/X", "55/Z"),
                    new QuickLineAxisPlacementInput("A", "54/W", "54/Y")
                });

            QuickLineAxisPlacement first = placements.Single(item => item.Id == "A");
            QuickLineAxisPlacement second = placements.Single(item => item.Id == "B");
            Assert.Equal(0, first.OffsetXMillimetres);
            Assert.Equal(0, first.OffsetYMillimetres);
            Assert.Equal(4800, second.OffsetXMillimetres);
            Assert.Equal(4800, second.OffsetYMillimetres);
            Assert.True(first.Resolved);
            Assert.True(second.Resolved);
        }

        [Fact]
        public void AxisPlacement_InvalidAxisGetsVisibleFallback()
        {
            QuickLineAxisPlacement placement = Assert.Single(
                QuickLineAxisPlacementPlanner.Plan(new[]
                {
                    new QuickLineAxisPlacementInput("A", "未配置", "54/X")
                }));

            Assert.False(placement.Resolved);
            Assert.NotEmpty(placement.Diagnostics);
        }

        [Fact]
        public void AxisPlacement_InvalidFrameDoesNotOverlapResolvedOrigin()
        {
            IReadOnlyList<QuickLineAxisPlacement> placements =
                QuickLineAxisPlacementPlanner.Plan(new[]
                {
                    new QuickLineAxisPlacementInput("valid", "54/W", "54/X"),
                    new QuickLineAxisPlacementInput("invalid", "?", "54/X")
                });

            QuickLineAxisPlacement invalid = placements.Single(item => item.Id == "invalid");
            Assert.Equal(4800, invalid.OffsetXMillimetres);
        }

        [Fact]
        public void AxisGrid_CombinesAllSelectedFrameAxes()
        {
            QuickLineAxisGrid grid = QuickLineAxisGridPlanner.PlanCombined(new[]
            {
                new QuickLineAxisPlacementInput("A", "54/W", "54/X"),
                new QuickLineAxisPlacementInput("B", "55/X", "55/Z")
            });

            Assert.Equal(new[] { "54", "55" },
                grid.NumericMarks.Select(item => item.Label));
            Assert.Equal(new[] { "W", "X", "Y", "Z" },
                grid.AlphabeticMarks.Select(item => item.Label));
            Assert.Equal(new[] { 0d, 4800d, 9600d, 14400d },
                grid.AlphabeticMarks.Select(item => item.OffsetMillimetres));
        }

        [Fact]
        public void RouteAnchor_UsesNearestCadEndpoint()
        {
            string node = QuickLineRouteAnchorPlanner.FindNearestNode(
                new[]
                {
                    new QuickLineRouteEndpointCandidate("L1", "N2",
                        new QuickLinePoint(100, 0))
                },
                new QuickLinePoint(96, 3));

            Assert.Equal("N2", node);
        }

        [Fact]
        public void BuildAll_IncludesDisconnectedIsometricRoutes()
        {
            QuickLineSegment first = Polar("A", new QuickLinePoint(0, 0), 30, 100);
            QuickLineSegment second = Polar("B", new QuickLinePoint(500, 500), 150, 100);
            QuickLineGraph graph = QuickLineGraph.Build(new[] { first, second }, 0.001);
            var distances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = 1200,
                ["B"] = 2400
            };

            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, distances,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            Assert.Equal(2, scene.Segments.Count);
            Assert.Equal(4, scene.Nodes.Count);
            Assert.NotEqual(scene.Segments[0].StartNodeId,
                scene.Segments[1].StartNodeId);
        }

        [Fact]
        public void BuildAll_AutoModeIncludesHorizontalOrthographicRoute()
        {
            var horizontal = new QuickLineSegment("H",
                new QuickLinePoint(0, 0), new QuickLinePoint(100, 0));
            var vertical = new QuickLineSegment("V",
                new QuickLinePoint(100, 0), new QuickLinePoint(100, 100));
            QuickLineGraph graph = QuickLineGraph.Build(
                new[] { horizontal, vertical }, 0.001);
            var distances = new Dictionary<string, double>
            {
                ["H"] = 1200,
                ["V"] = 2400
            };

            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, distances, new HashSet<string>());

            Assert.Equal(QuickLineProjectionMode.Orthographic,
                scene.ProjectionMode);
            Assert.Equal(new[] { "H", "V" },
                scene.Segments.Select(item => item.Id).OrderBy(item => item));
            Assert.DoesNotContain(scene.Diagnostics,
                item => item.Contains("已跳过"));
        }

        [Fact]
        public void BuildAll_RejectsAFrameWithoutClassifiableRoutes()
        {
            var line = Polar("D", new QuickLinePoint(0, 0), 10, 100);
            var distances = new Dictionary<string, double> { ["D"] = 1000 };

            Assert.Throws<ArgumentException>(() => QuickLineIsometricSceneBuilder.BuildAll(
                QuickLineGraph.Build(new[] { line }), distances, distances,
                new HashSet<string>()));
        }

        [Fact]
        public void PreviewPayloadNamesCircuitsAndDirectionMarkers()
        {
            string feature = File.ReadAllText(PathOf("src", "UNCAD", "Features",
                "Unl", "QuickLine3dPreviewFeature.cs"));
            string app = File.ReadAllText(PathOf("src", "UNCAD", "Web",
                "QuickLine3D", "app.js"));

            Assert.Contains("[\"circuitName\"]", feature);
            Assert.Contains("[\"directionMarkers\"]", feature);
            Assert.Contains("下游", app);
            Assert.Contains("上游", app);
            Assert.Contains("南北字母轴（向北 +1）", app);
            Assert.Contains("东西数字轴（向东 +1）", app);
            Assert.DoesNotContain("const colors = { X:", app);
        }

        [Fact]
        public void PreviewDecluttersDenseRouteLabels()
        {
            string feature = File.ReadAllText(PathOf("src", "UNCAD", "Features",
                "Unl", "QuickLine3dPreviewFeature.cs"));
            string app = File.ReadAllText(PathOf("src", "UNCAD", "Web",
                "QuickLine3D", "app.js"));
            string html = File.ReadAllText(PathOf("src", "UNCAD", "Web",
                "QuickLine3D", "index.html"));

            Assert.Contains("showRouteLabels", app);
            Assert.Contains("occupied", app);
            Assert.Contains("data.segments || []).length <= 24", app);
            Assert.Contains(">路线标签</button>", html);
            Assert.Contains("new THREE.BoxGeometry", app);
            Assert.Contains("new THREE.SphereGeometry", app);
            Assert.Contains("routeGroupId", app);
            Assert.Contains("const routeGroups = new Map()", app);
            Assert.Contains("directionLabelsButton", File.ReadAllText(PathOf("src",
                "UNCAD", "Web", "QuickLine3D", "index.html")));
            Assert.Contains("PlanCombined", feature);
            Assert.Contains("sceneOffsetX -= downstreamPoint.X", feature);
            Assert.Contains("sceneOffsetY -= downstreamPoint.Y", feature);
            Assert.Contains("selectedRouteIds", app);
            Assert.Contains("applyRouteSelection", app);
            Assert.DoesNotContain("new THREE.ArrowHelper", app);
        }

        private static QuickLineSegment Polar(string id, QuickLinePoint start,
            double degrees, double length)
        {
            double radians = degrees * Math.PI / 180.0;
            return new QuickLineSegment(id, start, new QuickLinePoint(
                start.X + Math.Cos(radians) * length,
                start.Y + Math.Sin(radians) * length));
        }

        private static string PathOf(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}
