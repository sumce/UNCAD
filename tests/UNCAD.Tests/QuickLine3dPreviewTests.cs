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
        public void RouteList_UsesFixedViewportHeight()
        {
            string styles = File.ReadAllText(PathOf("src", "UNCAD", "Web",
                "QuickLine3D", "styles.css"));

            Assert.Contains("height: 220px", styles);
            Assert.Contains("min-height: 220px", styles);
            Assert.Contains("max-height: 220px", styles);
            Assert.Contains("overflow: auto", styles);
        }

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
