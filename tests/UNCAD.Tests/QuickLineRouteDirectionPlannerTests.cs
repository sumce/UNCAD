using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class QuickLineRouteDirectionPlannerTests
    {
        [Fact]
        public void Resolve_MultiDirectionRouteUsesAxisAtActualEndsNotXOrder()
        {
            const double xStep = 4156.921938165306;
            const double yStep = 2400.0;
            QuickLineIsometricScene scene = BuildScene(new[]
            {
                new QuickLineSegment("a", new QuickLinePoint(0, 0),
                    new QuickLinePoint(-xStep, -yStep)),
                new QuickLineSegment("b", new QuickLinePoint(-xStep, -yStep),
                    new QuickLinePoint(-2 * xStep, 0))
            });

            QuickLineRouteDirection direction =
                QuickLineRouteDirectionPlanner.Resolve(scene, "48/V", "47/U");

            Assert.True(direction.IsResolved);
            QuickLineSpatialPoint upstream = scene.Nodes.Single(node =>
                node.Id == direction.UpstreamNodeId).Position;
            QuickLineSpatialPoint downstream = scene.Nodes.Single(node =>
                node.Id == direction.DownstreamNodeId).Position;
            Assert.True(upstream.X > downstream.X,
                "This route's upstream end is to the right of its downstream end.");

            QuickLineOrbitPreviewModel model = QuickLineOrbitPreviewModel.FromScene(
                scene, "上游 48/V ↔ 下游 47/U", "48/V", "47/U",
                upstreamEndpoint: upstream, downstreamEndpoint: downstream,
                directionResolved: true,
                directionDiagnostics: direction.Diagnostics);
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model);

            Assert.True(envelope.DirectionResolved);
            Assert.Equal(upstream.X, envelope.UpstreamEndpoint.X, 6);
            Assert.Equal(downstream.X, envelope.DownstreamEndpoint.X, 6);
            Assert.Contains(envelope.EndpointPins, pin => pin.Text == "48/V"
                && Math.Abs(pin.X - upstream.X) < 0.001);
            Assert.Contains(envelope.EndpointPins, pin => pin.Text == "47/U"
                && Math.Abs(pin.X - downstream.X) < 0.001);
        }

        [Fact]
        public void Resolve_InvalidAxisDoesNotGuessAndReportsWhy()
        {
            QuickLineIsometricScene scene = BuildScene(new[]
            {
                new QuickLineSegment("a", new QuickLinePoint(0, 0),
                    new QuickLinePoint(4800, 0))
            });

            QuickLineRouteDirection direction =
                QuickLineRouteDirectionPlanner.Resolve(scene, "上游轴", "47/U");

            Assert.False(direction.IsResolved);
            Assert.Contains(direction.Diagnostics,
                text => text.IndexOf("数字/字母", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void Resolve_NonParallelRouteCanMatchBothAxisRegions()
        {
            QuickLineRouteDirection direction =
                QuickLineRouteDirectionPlanner.Resolve(new[]
                {
                    new QuickLineRouteDirectionEndpoint("a",
                        new QuickLineSpatialPoint(0, 0, 0)),
                    new QuickLineRouteDirectionEndpoint("b",
                        new QuickLineSpatialPoint(2000, 100, 0))
                }, null, null, false, false, "48/V", "47/V");

            Assert.True(direction.IsResolved);
            Assert.Equal("a", direction.UpstreamNodeId);
            Assert.Equal("b", direction.DownstreamNodeId);
            Assert.Contains(direction.Diagnostics,
                text => text.IndexOf("轴位区域", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void FittedGridShowsBothPinsInsideTheirNamedBays()
        {
            var up = new QuickLineSpatialPoint(0, 0, 8000);
            var bend = new QuickLineSpatialPoint(2000, 0, 8000);
            var down = new QuickLineSpatialPoint(2000, 100, 8000);
            var envelope = QuickLineOrbitPreviewEnvelope.Build(new QuickLineOrbitPreviewModel(
                new[] {
                    new QuickLineOrbitPreviewSegment("1", up, bend, QuickLineSpatialAxis.X, 2000, true),
                    new QuickLineOrbitPreviewSegment("2", bend, down, QuickLineSpatialAxis.Y, 100, true)
                }, "", null, "48/V", "47/V", up, down, directionResolved: true));

            var upCenter = envelope.NumericTicks.Single(label => label.Text == "48").Y;
            var downCenter = envelope.NumericTicks.Single(label => label.Text == "47").Y;
            var letterCenter = envelope.LetterLabels.Single(label => label.Text == "V").X;
            Assert.Equal(4800, downCenter - upCenter);
            Assert.Equal(letterCenter, envelope.GridAnchor.X);
            Assert.Equal(upCenter, envelope.GridAnchor.Y);
            foreach (var pin in envelope.EndpointPins)
            {
                Assert.InRange(Math.Abs(pin.X - letterCenter), 0, 2400);
                Assert.InRange(Math.Abs(pin.Y - (pin.Text == "48/V" ? upCenter : downCenter)), 0, 2400);
            }
            Assert.Equal(2, envelope.EndpointPins.Count);
        }

        [Fact]
        public void Resolve_SeparateLoopAndOpenPathDoNotShareAnAxisAssignment()
        {
            var scene = BuildScene(new[] {
                new QuickLineSegment("path", new QuickLinePoint(0, 0), new QuickLinePoint(100, 0)),
                new QuickLineSegment("1", new QuickLinePoint(200, 0), new QuickLinePoint(300, 0)),
                new QuickLineSegment("2", new QuickLinePoint(300, 0), new QuickLinePoint(300, 100)),
                new QuickLineSegment("3", new QuickLinePoint(300, 100), new QuickLinePoint(200, 100)),
                new QuickLineSegment("4", new QuickLinePoint(200, 100), new QuickLinePoint(200, 0))
            });
            Assert.False(QuickLineRouteDirectionPlanner.Resolve(scene, "48/U", "48/V").IsResolved);
        }

        [Fact]
        public void Resolve_BothEndpointAssignmentsFitAxisRegionsDoesNotGuess()
        {
            QuickLineRouteDirection direction =
                QuickLineRouteDirectionPlanner.Resolve(new[]
                {
                    new QuickLineRouteDirectionEndpoint("a",
                        new QuickLineSpatialPoint(0, 0, 0)),
                    new QuickLineRouteDirectionEndpoint("b",
                        new QuickLineSpatialPoint(100, 0, 0))
                }, null, null, false, false, "48/V", "47/V");

            Assert.False(direction.IsResolved);
            Assert.Contains(direction.Diagnostics,
                text => text.IndexOf("歧义", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void Resolve_EndpointsOutsideAxisRegionsDoesNotGuess()
        {
            QuickLineRouteDirection direction =
                QuickLineRouteDirectionPlanner.Resolve(new[]
                {
                    new QuickLineRouteDirectionEndpoint("a",
                        new QuickLineSpatialPoint(0, 0, 0)),
                    new QuickLineRouteDirectionEndpoint("b",
                        new QuickLineSpatialPoint(14400, 0, 0))
                }, null, null, false, false, "48/V", "47/V");

            Assert.False(direction.IsResolved);
            Assert.Contains(direction.Diagnostics,
                text => text.IndexOf("轴位区域", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void Resolve_BranchDoesNotChooseAnyOfItsThreeEnds()
        {
            const double x = 4156.921938165306;
            QuickLineIsometricScene scene = BuildScene(new[]
            {
                new QuickLineSegment("a", new QuickLinePoint(0, 0),
                    new QuickLinePoint(x, 2400)),
                new QuickLineSegment("b", new QuickLinePoint(0, 0),
                    new QuickLinePoint(-x, 2400)),
                new QuickLineSegment("c", new QuickLinePoint(0, 0),
                    new QuickLinePoint(0, -4800))
            });

            QuickLineRouteDirection direction =
                QuickLineRouteDirectionPlanner.Resolve(scene, "48/V", "47/U");

            Assert.False(direction.IsResolved);
            Assert.Contains(direction.Diagnostics,
                text => text.IndexOf("分支", StringComparison.Ordinal) >= 0);
        }

        private static QuickLineIsometricScene BuildScene(
            IEnumerable<QuickLineSegment> segments)
        {
            QuickLineGraph graph = QuickLineGraph.Build(segments, 1.0);
            var lengths = graph.Segments.ToDictionary(segment => segment.Id,
                _ => 4800.0, StringComparer.OrdinalIgnoreCase);
            return QuickLineIsometricSceneBuilder.BuildMetricPreview(graph,
                lengths, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }
}
