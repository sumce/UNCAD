using System;
using System.Collections.Generic;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// Restores labels whose original U1L entity was deleted by a join operation.
    /// A match is accepted only when one path is clearly nearest and within range.
    /// </summary>
    public static class QuickLineStaleLabelMatcher
    {
        public static int FindUniqueNearestPath(QuickLinePoint point,
            IReadOnlyList<IReadOnlyList<QuickLinePoint>> paths,
            double maximumDistance, double ambiguityTolerance)
        {
            if (double.IsNaN(maximumDistance) || double.IsInfinity(maximumDistance)
                || maximumDistance < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumDistance));
            if (double.IsNaN(ambiguityTolerance)
                || double.IsInfinity(ambiguityTolerance)
                || ambiguityTolerance < 0)
                throw new ArgumentOutOfRangeException(nameof(ambiguityTolerance));

            int bestIndex = -1;
            double bestDistance = double.PositiveInfinity;
            double secondDistance = double.PositiveInfinity;
            for (int pathIndex = 0; paths != null && pathIndex < paths.Count; pathIndex++)
            {
                IReadOnlyList<QuickLinePoint> path = paths[pathIndex];
                double pathDistance = DistanceToPath(point, path);
                if (pathDistance < bestDistance)
                {
                    secondDistance = bestDistance;
                    bestDistance = pathDistance;
                    bestIndex = pathIndex;
                }
                else if (pathDistance < secondDistance)
                {
                    secondDistance = pathDistance;
                }
            }

            return bestIndex >= 0 && bestDistance <= maximumDistance
                && secondDistance - bestDistance > ambiguityTolerance
                    ? bestIndex : -1;
        }

        private static double DistanceToPath(QuickLinePoint point,
            IReadOnlyList<QuickLinePoint> path)
        {
            double bestSquared = double.PositiveInfinity;
            for (int index = 0; path != null && index < path.Count - 1; index++)
            {
                QuickLinePoint start = path[index];
                QuickLinePoint end = path[index + 1];
                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double lengthSquared = dx * dx + dy * dy;
                if (lengthSquared <= 1e-12) continue;

                double t = ((point.X - start.X) * dx
                    + (point.Y - start.Y) * dy) / lengthSquared;
                t = Math.Max(0, Math.Min(1, t));
                double nearestX = start.X + dx * t;
                double nearestY = start.Y + dy * t;
                double offsetX = point.X - nearestX;
                double offsetY = point.Y - nearestY;
                double distanceSquared = offsetX * offsetX + offsetY * offsetY;
                if (distanceSquared < bestSquared) bestSquared = distanceSquared;
            }
            return Math.Sqrt(bestSquared);
        }
    }
}
