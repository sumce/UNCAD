using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLineTraversalTests
    {
        [Fact]
        public void Build_ConnectsOnlyEndpointsWithinTolerance()
        {
            var graph = QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 10, 0),
                Line("B", 10.0005, 0, 20, 0),
                Line("C", 20.002, 0, 30, 0),
                // This crosses A's interior but has no endpoint at A.
                Line("X", 5, -5, 5, 5)
            }, endpointTolerance: 0.001);

            Assert.Single(graph.GetConnections("A", QuickLineEndpoint.End));
            Assert.Equal("B", graph.GetConnections("A", QuickLineEndpoint.End)[0].ConnectedSegmentId);
            Assert.Empty(graph.GetConnections("A", QuickLineEndpoint.Start));
            Assert.Empty(graph.GetConnections("X"));
        }

        [Fact]
        public void EndpointPick_TraversesAwayFromClickedEndpoint()
        {
            var graph = QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 10, 0),
                Line("B", 10, 0, 20, 0),
                Line("C", 20, 0, 30, 0)
            });

            QuickLineTraversalPlan plan = QuickLineTraversal.CreatePlan(
                graph, "A", new QuickLinePoint(0, 0));

            Assert.Equal(QuickLineClickRegion.StartEndpoint, plan.ClickRegion);
            Assert.Equal(QuickLineEndpoint.End, plan.ExitEndpoint);
            Assert.Equal(new[] { "A", "B", "C" },
                plan.Steps.Select(step => step.SegmentId).ToArray());
            Assert.Equal(QuickLineEndpoint.Start, plan.Steps[0].EntryEndpoint);
            Assert.Equal(QuickLineEndpoint.End, plan.Steps[0].ExitEndpoint);
            Assert.Equal(QuickLineEndpoint.Start, plan.Steps[1].EntryEndpoint);
        }

        [Fact]
        public void EndpointPickAtEnd_TraversesTowardStart()
        {
            var graph = QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 10, 0),
                Line("B", 10, 0, 20, 0),
                Line("C", 20, 0, 30, 0)
            });

            QuickLineTraversalPlan plan = QuickLineTraversal.CreatePlan(
                graph, "B", new QuickLinePoint(20, 0));

            Assert.Equal(QuickLineClickRegion.EndEndpoint, plan.ClickRegion);
            Assert.Equal(QuickLineEndpoint.Start, plan.ExitEndpoint);
            Assert.Equal(new[] { "B", "A" },
                plan.Steps.Select(step => step.SegmentId).ToArray());
            Assert.Equal(QuickLineEndpoint.End, plan.Steps[0].EntryEndpoint);
            Assert.Equal(QuickLineEndpoint.Start, plan.Steps[0].ExitEndpoint);
        }

        [Fact]
        public void InteriorPick_ChoosesSideWithMoreReachableSegments()
        {
            var graph = QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 10, 0),
                Line("B", 10, 0, 20, 0),
                Line("C", 20, 0, 30, 0),
                Line("D", 30, 0, 40, 0),
                Line("E", 40, 0, 50, 0),
                Line("R", -10, 0, 0, 0)
            });

            QuickLineTraversalPlan plan = QuickLineTraversal.CreatePlan(
                graph, "C", new QuickLinePoint(25, 0));

            Assert.Equal(QuickLineClickRegion.Interior, plan.ClickRegion);
            Assert.Equal(3, plan.StartSideCount); // B, A, R
            Assert.Equal(2, plan.EndSideCount);   // D, E
            Assert.Equal(QuickLineEndpoint.Start, plan.ExitEndpoint);
            Assert.Equal(new[] { "C", "B", "A", "R" },
                plan.Steps.Select(step => step.SegmentId).ToArray());
        }

        [Fact]
        public void InteriorPick_IgnoresAlreadyVisitedSegmentsWhenChoosingSide()
        {
            var graph = QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 10, 0),
                Line("B", 10, 0, 20, 0),
                Line("C", 20, 0, 30, 0),
                Line("D", 30, 0, 40, 0)
            });
            var alreadyVisited = new HashSet<string>(new[] { "A", "B" });

            QuickLineTraversalPlan plan = QuickLineTraversal.CreatePlan(
                graph, "C", new QuickLinePoint(25, 0), alreadyVisited);

            Assert.Equal(0, plan.StartSideCount);
            Assert.Equal(1, plan.EndSideCount);
            Assert.Equal(new[] { "C", "D" },
                plan.Steps.Select(step => step.SegmentId).ToArray());
        }

        [Fact]
        public void Walk_DoesNotRepeatSegmentsWhenGraphContainsCycle()
        {
            var graph = QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 10, 0),
                Line("B", 10, 0, 10, 10),
                Line("C", 10, 10, 0, 10),
                Line("D", 0, 10, 0, 0)
            });

            IReadOnlyList<QuickLineTraversalStep> steps = QuickLineTraversal.Walk(
                graph, "A", new QuickLinePoint(0, 0));

            Assert.Equal(new[] { "A", "B", "C", "D" },
                steps.Select(step => step.SegmentId).ToArray());
            Assert.Equal(steps.Count, steps.Select(step => step.SegmentId)
                .Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void Walk_AtBranchChoosesNeighborWithMoreUnvisitedDownstreamSegments()
        {
            // A's end is a junction.  B has one downstream segment while C
            // has two, so the route should take C even though B sorts first.
            var graph = QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 10, 0),
                Line("B", 10, 0, 20, 0),
                Line("B1", 20, 0, 30, 0),
                Line("C", 10, 0, 10, 10),
                Line("C1", 10, 10, 10, 20),
                Line("C2", 10, 20, 10, 30)
            });

            IReadOnlyList<QuickLineTraversalStep> steps = QuickLineTraversal.Walk(
                graph, "A", new QuickLinePoint(0, 0));

            Assert.Equal(new[] { "A", "C", "C1", "C2" },
                steps.Select(step => step.SegmentId).ToArray());
        }

        [Fact]
        public void Walk_AtBranchSkipsCompletedNeighborAndUsesRemainingNeighbor()
        {
            var graph = QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 10, 0),
                Line("B", 10, 0, 20, 0),
                Line("B1", 20, 0, 30, 0),
                Line("C", 10, 0, 10, 10),
                Line("C1", 10, 10, 10, 20)
            });
            var completed = new HashSet<string>(new[] { "B", "B1" });

            IReadOnlyList<QuickLineTraversalStep> steps = QuickLineTraversal.Walk(
                graph, "A", new QuickLinePoint(0, 0), completed);

            Assert.Equal(new[] { "A", "C", "C1" },
                steps.Select(step => step.SegmentId).ToArray());
        }

        [Fact]
        public void Walk_CanCrossCompletedSegmentsWithoutPromptingAgain()
        {
            var graph = QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 10, 0),
                Line("B", 10, 0, 20, 0),
                Line("C", 20, 0, 30, 0),
                Line("D", 30, 0, 40, 0)
            });
            var completed = new HashSet<string>(new[] { "B" });

            IReadOnlyList<QuickLineTraversalStep> steps = QuickLineTraversal.Walk(
                graph, "A", new QuickLinePoint(0, 0), completed,
                traverseAlreadyVisited: true);

            Assert.Equal(new[] { "A", "C", "D" },
                steps.Select(step => step.SegmentId).ToArray());
        }

        [Fact]
        public void InteriorPick_WithEqualRemainingSidesUsesStableStartTieBreak()
        {
            var graph = QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 10, 0),
                Line("B", 10, 0, 20, 0),
                Line("D", -10, 0, 0, 0)
            });

            QuickLineTraversalPlan plan = QuickLineTraversal.CreatePlan(
                graph, "A", new QuickLinePoint(5, 0));

            Assert.Equal(QuickLineClickRegion.Interior, plan.ClickRegion);
            Assert.Equal(plan.StartSideCount, plan.EndSideCount);
            Assert.Equal(QuickLineEndpoint.Start, plan.ExitEndpoint);
            Assert.Equal(new[] { "A", "D" },
                plan.Steps.Select(step => step.SegmentId).ToArray());
        }

        [Fact]
        public void ClassifyClick_WhenNearBothEndpointsChoosesNearestEndpoint()
        {
            var segment = Line("A", 0, 0, 1, 0);
            QuickLineEndpoint endpoint;

            QuickLineClickRegion region = QuickLineTraversal.ClassifyClick(
                segment, new QuickLinePoint(0.8, 0), 1.0, out endpoint);

            Assert.Equal(QuickLineClickRegion.EndEndpoint, region);
            Assert.Equal(QuickLineEndpoint.End, endpoint);
        }

        [Fact]
        public void PickNearEndpoint_UsesConfiguredTolerance()
        {
            var graph = QuickLineGraph.Build(new[] { Line("A", 0, 0, 100, 0) }, 0.01);

            QuickLineEndpoint endpoint;
            QuickLineClickRegion region = QuickLineTraversal.ClassifyClick(
                graph.GetSegment("A"), new QuickLinePoint(0.009, 0),
                graph.EndpointTolerance, out endpoint);

            Assert.Equal(QuickLineClickRegion.StartEndpoint, region);
            Assert.Equal(QuickLineEndpoint.Start, endpoint);
        }

        [Fact]
        public void Build_RejectsDuplicateIds()
        {
            Assert.Throws<System.ArgumentException>(() => QuickLineGraph.Build(new[]
            {
                Line("A", 0, 0, 1, 0),
                Line("a", 2, 0, 3, 0)
            }));
        }

        [Fact]
        public void CreatePlan_RejectsNegativeToleranceOtherThanDefaultSentinel()
        {
            var graph = QuickLineGraph.Build(new[] { Line("A", 0, 0, 1, 0) });

            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                QuickLineTraversal.CreatePlan(graph, "A",
                    new QuickLinePoint(0.5, 0), endpointTolerance: -0.5));
        }

        private static QuickLineSegment Line(string id,
            double x1, double y1, double x2, double y2)
            => new QuickLineSegment(id,
                new QuickLinePoint(x1, y1),
                new QuickLinePoint(x2, y2));
    }
}
