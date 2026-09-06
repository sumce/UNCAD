using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace UNCAD.Core.QuickLine
{
    /// <summary>Direction from the selected segment toward the next segment.</summary>
    public enum QuickLineTraversalDirection
    {
        TowardStart = 1,
        TowardEnd = 2
    }

    /// <summary>One segment and the endpoint through which it is traversed.</summary>
    public sealed class QuickLineTraversalStep
    {
        internal QuickLineTraversalStep(QuickLineSegment segment,
            QuickLineEndpoint entryEndpoint, QuickLineEndpoint exitEndpoint)
        {
            Segment = segment;
            SegmentId = segment.Id;
            EntryEndpoint = entryEndpoint;
            ExitEndpoint = exitEndpoint;
        }

        public QuickLineSegment Segment { get; }
        public string SegmentId { get; }
        public QuickLineEndpoint EntryEndpoint { get; }
        public QuickLineEndpoint ExitEndpoint { get; }
    }

    /// <summary>Result of choosing a side and preparing a no-repeat walk.</summary>
    public sealed class QuickLineTraversalPlan
    {
        internal QuickLineTraversalPlan(QuickLineClickRegion clickRegion,
            QuickLineEndpoint clickedEndpoint, QuickLineEndpoint exitEndpoint,
            int startSideCount, int endSideCount,
            IReadOnlyList<QuickLineTraversalStep> steps)
        {
            ClickRegion = clickRegion;
            ClickedEndpoint = clickedEndpoint;
            ExitEndpoint = exitEndpoint;
            StartSideCount = startSideCount;
            EndSideCount = endSideCount;
            Steps = steps;
        }

        public QuickLineClickRegion ClickRegion { get; }
        public QuickLineEndpoint ClickedEndpoint { get; }
        public QuickLineEndpoint ExitEndpoint { get; }
        public QuickLineTraversalDirection Direction
            => ExitEndpoint == QuickLineEndpoint.Start
                ? QuickLineTraversalDirection.TowardStart
                : QuickLineTraversalDirection.TowardEnd;
        public int StartSideCount { get; }
        public int EndSideCount { get; }
        public IReadOnlyList<QuickLineTraversalStep> Steps { get; }
    }

    /// <summary>
    /// Pure selection and traversal logic for U1LX.  The generated plan can
    /// be consumed one step at a time by the interactive CAD/UI layer: write
    /// the current mm label, center the view, then move to the next step.
    /// </summary>
    public static class QuickLineTraversal
    {
        /// <summary>
        /// Builds a route beginning with startSegmentId. A click at an endpoint
        /// traverses away from that endpoint, unless that would immediately
        /// stop while the clicked side is connected. An interior click chooses the
        /// side with more reachable, unprocessed segments; equal sides choose
        /// Start for deterministic behavior. Pass -1 for endpointTolerance to
        /// use the graph's configured tolerance; all other values must be
        /// finite and non-negative. When traverseAlreadyVisited is true,
        /// completed segments are crossed but omitted from the returned steps.
        /// </summary>
        public static QuickLineTraversalPlan CreatePlan(
            QuickLineGraph graph,
            string startSegmentId,
            QuickLinePoint click,
            ISet<string> alreadyVisited = null,
            double endpointTolerance = -1,
            bool traverseAlreadyVisited = false)
        {
            if (graph == null) throw new ArgumentNullException("graph");
            QuickLineSegment startSegment = graph.GetSegment(startSegmentId);
            // -1 is the documented optional-argument sentinel.  Do not treat
            // every negative value as a default: silently accepting -0.5 (or
            // -infinity, which is caught below) would make a caller's invalid
            // tolerance look like a valid graph configuration.
            double tolerance = endpointTolerance == -1
                ? graph.EndpointTolerance
                : endpointTolerance;
            ValidateTolerance(tolerance);

            QuickLineEndpoint clickedEndpoint;
            QuickLineClickRegion clickRegion = startSegment.ClassifyClick(
                click, tolerance, out clickedEndpoint);

            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (alreadyVisited != null)
                foreach (string id in alreadyVisited)
                    if (id != null) blocked.Add(id);
            // The selected segment is always processed now, even when a caller
            // accidentally includes it in its session's completed set.
            blocked.Remove(startSegment.Id);

            int startSideCount = graph.CountReachableFrom(
                startSegment.Id, QuickLineEndpoint.Start, blocked,
                traverseAlreadyVisited);
            int endSideCount = graph.CountReachableFrom(
                startSegment.Id, QuickLineEndpoint.End, blocked,
                traverseAlreadyVisited);

            QuickLineEndpoint exitEndpoint;
            if (clickRegion != QuickLineClickRegion.Interior)
            {
                exitEndpoint = startSegment.OtherEndpoint(clickedEndpoint);
                int exitSideCount = exitEndpoint == QuickLineEndpoint.Start
                    ? startSideCount : endSideCount;
                int clickedSideCount = clickedEndpoint == QuickLineEndpoint.Start
                    ? startSideCount : endSideCount;
                if (exitSideCount == 0 && clickedSideCount > 0)
                    exitEndpoint = clickedEndpoint;
            }
            else
                exitEndpoint = startSideCount >= endSideCount
                    ? QuickLineEndpoint.Start
                    : QuickLineEndpoint.End;

            var steps = new List<QuickLineTraversalStep>();
            var visited = traverseAlreadyVisited
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(blocked, StringComparer.OrdinalIgnoreCase);
            QuickLineSegment current = startSegment;
            QuickLineEndpoint currentExit = exitEndpoint;
            QuickLineEndpoint currentEntry = clickRegion == QuickLineClickRegion.Interior
                ? QuickLineEndpoint.None
                : startSegment.OtherEndpoint(currentExit);

            while (true)
            {
                if (!visited.Add(current.Id)) break;
                if (!blocked.Contains(current.Id))
                    steps.Add(new QuickLineTraversalStep(current, currentEntry, currentExit));

                QuickLineConnection nextConnection = SelectNextConnection(
                    graph, current, currentExit, visited, blocked,
                    traverseAlreadyVisited);
                if (nextConnection == null) break;

                current = graph.GetSegment(nextConnection.ConnectedSegmentId);
                currentEntry = nextConnection.ConnectedEndpoint;
                currentExit = current.OtherEndpoint(currentEntry);
            }

            return new QuickLineTraversalPlan(clickRegion, clickedEndpoint,
                exitEndpoint, startSideCount, endSideCount,
                new ReadOnlyCollection<QuickLineTraversalStep>(steps));
        }

        public static IReadOnlyList<QuickLineTraversalStep> Walk(
            QuickLineGraph graph,
            string startSegmentId,
            QuickLinePoint click,
            ISet<string> alreadyVisited = null,
            double endpointTolerance = -1,
            bool traverseAlreadyVisited = false)
            => CreatePlan(graph, startSegmentId, click,
                alreadyVisited, endpointTolerance, traverseAlreadyVisited).Steps;

        /// <summary>Exposes endpoint classification for Cad pick handling.</summary>
        public static QuickLineClickRegion ClassifyClick(
            QuickLineSegment segment, QuickLinePoint click,
            double endpointTolerance, out QuickLineEndpoint endpoint)
        {
            if (segment == null) throw new ArgumentNullException("segment");
            return segment.ClassifyClick(click, endpointTolerance, out endpoint);
        }

        private static QuickLineConnection SelectNextConnection(
            QuickLineGraph graph,
            QuickLineSegment current,
            QuickLineEndpoint exitEndpoint,
            ISet<string> visited,
            ISet<string> excluded,
            bool traverseExcluded)
        {
            QuickLineConnection selected = null;
            int selectedScore = int.MinValue;

            foreach (QuickLineConnection connection in graph.GetConnections(
                current.Id, exitEndpoint))
            {
                if (visited.Contains(connection.ConnectedSegmentId)) continue;

                QuickLineSegment neighbor = graph.GetSegment(connection.ConnectedSegmentId);
                QuickLineEndpoint neighborExit = neighbor.OtherEndpoint(
                    connection.ConnectedEndpoint);
                int score = graph.CountReachableFrom(
                    neighbor.Id, neighborExit, excluded, traverseExcluded);
                if (selected == null
                    || score > selectedScore
                    || (score == selectedScore
                        && string.Compare(connection.ConnectedSegmentId,
                            selected.ConnectedSegmentId, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    selected = connection;
                    selectedScore = score;
                }
            }
            return selected;
        }

        private static void ValidateTolerance(double tolerance)
        {
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)
                throw new ArgumentOutOfRangeException("endpointTolerance");
        }
    }
}
