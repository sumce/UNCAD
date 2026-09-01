using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLineIsometricSceneTests
    {
        [Fact]
        public void Build_MapsThirtyNinetyAndOneFiftyToOrthogonalAxes()
        {
            QuickLineSegment a = Polar("A", new QuickLinePoint(0, 0), 30, 100);
            QuickLineSegment b = Polar("B", a.End, 90, 50);
            QuickLineSegment c = Polar("C", b.End, 150, 200);
            QuickLineGraph graph = QuickLineGraph.Build(new[] { a, b, c }, 0.001);

            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.Build(
                graph, "A", Distances(("A", 1000), ("B", 2000), ("C", 3000)));

            Assert.Equal(3, scene.Segments.Count);
            Assert.Equal(QuickLineSpatialAxis.X, Segment(scene, "A").Axis);
            Assert.Equal(QuickLineSpatialAxis.Z, Segment(scene, "B").Axis);
            Assert.Equal(QuickLineSpatialAxis.Y, Segment(scene, "C").Axis);
            AssertPoint(scene, Segment(scene, "A").EndNodeId, 1000, 0, 0);
            AssertPoint(scene, Segment(scene, "B").EndNodeId, 1000, 0, 2000);
            AssertPoint(scene, Segment(scene, "C").EndNodeId, 1000, 3000, 2000);
        }

        [Fact]
        public void Build_UsesLabelDistanceInsteadOfPlanLength()
        {
            QuickLineSegment line = Polar("A", new QuickLinePoint(0, 0), 30, 8500);
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.Build(
                QuickLineGraph.Build(new[] { line }), "A", Distances(("A", 1250)));

            AssertPoint(scene, Segment(scene, "A").EndNodeId, 1250, 0, 0);
        }

        [Fact]
        public void Build_KeepsPlaceholderValueSeparateFromInitialDisplayLength()
        {
            QuickLineSegment line = Polar("A", new QuickLinePoint(0, 0), 30, 8500);
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.Build(
                QuickLineGraph.Build(new[] { line }), "A",
                Distances(("A", 2000)), Distances(("A", line.Length)),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            QuickLineIsometricSegment segment = Segment(scene, "A");
            Assert.Equal(2000, segment.DistanceMillimetres);
            Assert.Equal(8500, segment.DisplayDistanceMillimetres, 6);
            Assert.False(segment.Completed);
            AssertPoint(scene, segment.EndNodeId, 8500, 0, 0);
        }

        [Fact]
        public void Build_AutoDetectsOrthographicHorizontalAndVerticalLines()
        {
            var horizontal = new QuickLineSegment("H",
                new QuickLinePoint(0, 0), new QuickLinePoint(900, 0));
            var vertical = new QuickLineSegment("V", horizontal.End,
                new QuickLinePoint(900, 700));
            QuickLineGraph graph = QuickLineGraph.Build(new[] { horizontal, vertical });

            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.Build(
                graph, "H", Distances(("H", 1800), ("V", 1200)));

            Assert.Equal(QuickLineProjectionMode.Orthographic,
                scene.ProjectionMode);
            Assert.Equal(QuickLineSpatialAxis.X, Segment(scene, "H").Axis);
            Assert.Equal(QuickLineSpatialAxis.Z, Segment(scene, "V").Axis);
            AssertPoint(scene, Segment(scene, "V").EndNodeId, 1800, 0, 1200);
        }

        [Fact]
        public void Build_AutoDetectionUsesSelectedConnectedComponent()
        {
            var horizontal = new QuickLineSegment("H",
                new QuickLinePoint(0, 0), new QuickLinePoint(900, 0));
            var vertical = new QuickLineSegment("V", horizontal.End,
                new QuickLinePoint(900, 700));
            var unrelatedIso = Polar("I", new QuickLinePoint(5000, 5000), 30, 100);
            QuickLineGraph graph = QuickLineGraph.Build(
                new[] { horizontal, vertical, unrelatedIso });

            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.Build(
                graph, "H", Distances(("H", 1800), ("V", 1200), ("I", 300)));

            Assert.Equal(QuickLineProjectionMode.Orthographic,
                scene.ProjectionMode);
            Assert.Equal(2, scene.Segments.Count);
        }

        [Fact]
        public void TryClassifyOrthographic_RejectsIsometricDiagonal()
        {
            QuickLineSegment diagonal = Polar("D",
                new QuickLinePoint(0, 0), 30, 100);
            Assert.False(QuickLineIsometricSceneBuilder.TryClassifyOrthographic(
                diagonal, 3.0, out _, out _, out _));
        }

        [Fact]
        public void Build_PreservesReversedAxisDirection()
        {
            QuickLineSegment forward = Polar("A", new QuickLinePoint(0, 0), 30, 10);
            var reversed = new QuickLineSegment("R", forward.End, forward.Start);

            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.Build(
                QuickLineGraph.Build(new[] { reversed }), "R", Distances(("R", 500)));

            Assert.Equal(-1, Segment(scene, "R").DirectionSign);
            AssertPoint(scene, Segment(scene, "R").EndNodeId, -500, 0, 0);
        }

        [Fact]
        public void Build_IncludesOnlySelectedConnectedComponent()
        {
            QuickLineSegment a = Polar("A", new QuickLinePoint(0, 0), 30, 10);
            QuickLineSegment b = Polar("B", a.End, 90, 10);
            QuickLineSegment other = Polar("X", new QuickLinePoint(500, 500), 30, 10);
            QuickLineGraph graph = QuickLineGraph.Build(new[] { a, b, other });

            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.Build(
                graph, "A", Distances(("A", 10), ("B", 20), ("X", 30)));

            Assert.Equal(new[] { "A", "B" },
                scene.Segments.Select(item => item.Id).OrderBy(item => item).ToArray());
        }

        [Fact]
        public void TryClassify_AcceptsSmallDraftingErrorAndRejectsFrameLine()
        {
            QuickLineSegment almostVertical = Polar("A",
                new QuickLinePoint(0, 0), 90.2, 100);
            QuickLineSegment horizontal = Polar("B",
                new QuickLinePoint(0, 0), 0, 100);

            Assert.True(QuickLineIsometricSceneBuilder.TryClassify(almostVertical,
                3.0, out QuickLineSpatialAxis axis, out int sign, out double angle));
            Assert.Equal(QuickLineSpatialAxis.Z, axis);
            Assert.Equal(1, sign);
            Assert.Equal(90.2, angle, 6);
            Assert.False(QuickLineIsometricSceneBuilder.TryClassify(horizontal,
                3.0, out _, out _, out _));
        }

        [Fact]
        public void Build_ReportsProjectedCycleThatCannotCloseInThreeDimensions()
        {
            double h = Math.Sqrt(3.0) * 50.0;
            var a = new QuickLineSegment("A", new QuickLinePoint(0, 0),
                new QuickLinePoint(h, 50));
            var b = new QuickLineSegment("B", a.End, new QuickLinePoint(0, 100));
            var c = new QuickLineSegment("C", b.End, a.Start);
            QuickLineGraph graph = QuickLineGraph.Build(new[] { a, b, c }, 0.001);

            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.Build(
                graph, "A", Distances(("A", 100), ("B", 100), ("C", 100)));

            Assert.Contains(scene.Diagnostics,
                item => item.Contains("三维闭合偏差"));
        }

        [Fact]
        public void Build_RejectsMissingDistance()
        {
            QuickLineSegment line = Polar("A", new QuickLinePoint(0, 0), 30, 10);
            Assert.Throws<ArgumentException>(() =>
                QuickLineIsometricSceneBuilder.Build(
                    QuickLineGraph.Build(new[] { line }), "A",
                    new Dictionary<string, double>()));
        }

        private static QuickLineSegment Polar(string id, QuickLinePoint start,
            double degrees, double length)
        {
            double radians = degrees * Math.PI / 180.0;
            return new QuickLineSegment(id, start, new QuickLinePoint(
                start.X + Math.Cos(radians) * length,
                start.Y + Math.Sin(radians) * length));
        }

        private static Dictionary<string, double> Distances(
            params (string Id, double Distance)[] values)
            => values.ToDictionary(item => item.Id, item => item.Distance,
                StringComparer.OrdinalIgnoreCase);

        private static QuickLineIsometricSegment Segment(
            QuickLineIsometricScene scene, string id)
            => scene.Segments.Single(item => item.Id == id);

        private static void AssertPoint(QuickLineIsometricScene scene,
            string nodeId, double x, double y, double z)
        {
            QuickLineSpatialPoint point = scene.Nodes.Single(item => item.Id == nodeId)
                .Position;
            Assert.Equal(x, point.X, 6);
            Assert.Equal(y, point.Y, 6);
            Assert.Equal(z, point.Z, 6);
        }
    }
}
