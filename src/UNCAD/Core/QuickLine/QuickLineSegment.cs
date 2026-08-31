using System;

namespace UNCAD.Core.QuickLine
{
    /// <summary>Endpoint of a line segment in its stored orientation.</summary>
    public enum QuickLineEndpoint
    {
        None = 0,
        Start = 1,
        End = 2
    }

    /// <summary>How the user's pick relates to the selected line.</summary>
    public enum QuickLineClickRegion
    {
        Interior = 0,
        StartEndpoint = 1,
        EndEndpoint = 2
    }

    /// <summary>
    /// CAD-independent representation of one U1L Line entity.  Id is opaque
    /// to Core and is normally the AutoCAD ObjectId handle supplied by Cad.
    /// </summary>
    public sealed class QuickLineSegment
    {
        public QuickLineSegment(string id, QuickLinePoint start, QuickLinePoint end)
        {
            if (id == null) throw new ArgumentNullException("id");
            if (id.Trim().Length == 0) throw new ArgumentException("A line segment needs a stable id.", "id");

            Id = id;
            Start = start;
            End = end;
        }

        public string Id { get; }
        public QuickLinePoint Start { get; }
        public QuickLinePoint End { get; }
        public QuickLinePoint Midpoint => new QuickLinePoint(
            (Start.X + End.X) / 2.0,
            (Start.Y + End.Y) / 2.0);
        public double Length => Start.DistanceTo(End);

        public QuickLinePoint PointAt(QuickLineEndpoint endpoint)
        {
            switch (endpoint)
            {
                case QuickLineEndpoint.Start: return Start;
                case QuickLineEndpoint.End: return End;
                default: throw new ArgumentException("An endpoint is required.", "endpoint");
            }
        }

        public QuickLineEndpoint OtherEndpoint(QuickLineEndpoint endpoint)
        {
            switch (endpoint)
            {
                case QuickLineEndpoint.Start: return QuickLineEndpoint.End;
                case QuickLineEndpoint.End: return QuickLineEndpoint.Start;
                default: throw new ArgumentException("An endpoint is required.", "endpoint");
            }
        }

        /// <summary>
        /// Classifies a pick as an endpoint only when it is within tolerance.
        /// A pick in the middle deliberately returns Interior even when it is
        /// close to the line's midpoint; no projection is needed by traversal.
        /// </summary>
        public QuickLineClickRegion ClassifyClick(
            QuickLinePoint pick, double endpointTolerance,
            out QuickLineEndpoint endpoint)
        {
            ValidateTolerance(endpointTolerance);
            double toleranceSquared = endpointTolerance * endpointTolerance;
            double startDistance = pick.DistanceSquaredTo(Start);
            double endDistance = pick.DistanceSquaredTo(End);

            bool nearStart = startDistance <= toleranceSquared;
            bool nearEnd = endDistance <= toleranceSquared;
            if (nearStart && nearEnd)
            {
                // This is only possible for a very short/degenerate segment.
                // Choose the genuinely nearer side; Start is a stable tie break.
                if (endDistance < startDistance)
                {
                    endpoint = QuickLineEndpoint.End;
                    return QuickLineClickRegion.EndEndpoint;
                }
                endpoint = QuickLineEndpoint.Start;
                return QuickLineClickRegion.StartEndpoint;
            }
            if (nearStart)
            {
                endpoint = QuickLineEndpoint.Start;
                return QuickLineClickRegion.StartEndpoint;
            }
            if (nearEnd)
            {
                endpoint = QuickLineEndpoint.End;
                return QuickLineClickRegion.EndEndpoint;
            }

            endpoint = QuickLineEndpoint.None;
            return QuickLineClickRegion.Interior;
        }

        private static void ValidateTolerance(double tolerance)
        {
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)
                throw new ArgumentOutOfRangeException("endpointTolerance");
        }
    }
}
