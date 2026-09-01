using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLineSchematicLayoutTests
    {
        [Fact]
        public void Build_CompressesLongAndEnlargesShortWithinOriginalSquare()
        {
            var horizontal = new QuickLineSegment("H",
                new QuickLinePoint(0, 0), new QuickLinePoint(1000, 0));
            var vertical = new QuickLineSegment("V", horizontal.End,
                new QuickLinePoint(1000, 100));
            QuickLineGraph graph = QuickLineGraph.Build(
                new[] { horizontal, vertical });
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.Build(
                graph, "H", Distances(("H", 1000), ("V", 100)));

            var actual = Distances(("H", 10000), ("V", 10));
            QuickLineSchematicLayout layout =
                QuickLineSchematicLayoutBuilder.Build(graph, scene, actual);

            QuickLineSchematicSegment h = Segment(layout, "H");
            QuickLineSchematicSegment v = Segment(layout, "V");
            Assert.Equal(1000, layout.TargetSide, 6);
            Assert.Equal(1000, h.DisplayLength, 6);
            Assert.True(v.DisplayLength > vertical.Length);
            Assert.True(h.DisplayLength < actual["H"]);
            Assert.Equal(10, actual["V"]);
        }

        [Fact]
        public void Build_PreservesDirectionAndDoesNotMutateActualDistances()
        {
            var line = new QuickLineSegment("A",
                new QuickLinePoint(500, 700), new QuickLinePoint(100, 700));
            QuickLineGraph graph = QuickLineGraph.Build(new[] { line });
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.Build(
                graph, "A", Distances(("A", 400)));
            var actual = Distances(("A", 2500));

            QuickLineSchematicLayout layout =
                QuickLineSchematicLayoutBuilder.Build(graph, scene, actual);
            QuickLineSchematicSegment result = Segment(layout, "A");

            Assert.True(result.End.X < result.Start.X);
            Assert.Equal(2500, actual["A"]);
        }

        [Fact]
        public void BuildCreated_ProjectsSoutheastAndFitsExactMaximumSquare()
        {
            var created = new[]
            {
                new QuickLineCreatedSegment("S1", "N1", "N2",
                    QuickLineSpatialAxis.X, 1, 1000000),
                new QuickLineCreatedSegment("S2", "N2", "N3",
                    QuickLineSpatialAxis.Y, 1, 10),
                new QuickLineCreatedSegment("S3", "N3", "N4",
                    QuickLineSpatialAxis.Z, 1, 1000)
            };

            QuickLineSchematicLayout layout =
                QuickLineSchematicLayoutBuilder.BuildCreated(created);
            double minX = layout.Segments.SelectMany(item => new[]
                { item.Start.X, item.End.X }).Min();
            double maxX = layout.Segments.SelectMany(item => new[]
                { item.Start.X, item.End.X }).Max();
            double minY = layout.Segments.SelectMany(item => new[]
                { item.Start.Y, item.End.Y }).Min();
            double maxY = layout.Segments.SelectMany(item => new[]
                { item.Start.Y, item.End.Y }).Max();

            Assert.True(maxX - minX <= 78021.0 + 1e-6);
            Assert.True(maxY - minY <= 78021.0 + 1e-6);
            Assert.True(Math.Abs((maxX - minX) - 78021.0) <= 1e-6
                || Math.Abs((maxY - minY) - 78021.0) <= 1e-6);
            Assert.True(layout.Segments[0].End.X > layout.Segments[0].Start.X);
            Assert.True(layout.Segments[1].End.X < layout.Segments[1].Start.X);
            Assert.Equal(1000000, created[0].DistanceMillimetres);
            Assert.Equal(10, created[1].DistanceMillimetres);
        }

        private static Dictionary<string, double> Distances(
            params (string Id, double Distance)[] values)
            => values.ToDictionary(item => item.Id, item => item.Distance,
                StringComparer.OrdinalIgnoreCase);

        private static QuickLineSchematicSegment Segment(
            QuickLineSchematicLayout layout, string id)
            => layout.Segments.Single(item => item.Id == id);
    }
}
