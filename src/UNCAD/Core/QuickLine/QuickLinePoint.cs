using System;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// Two-dimensional point used by the U1LX line graph.  AutoCAD entities
    /// are projected to the current drawing plane before entering Core.
    /// </summary>
    public struct QuickLinePoint : IEquatable<QuickLinePoint>
    {
        public QuickLinePoint(double x, double y)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)
                || double.IsNaN(y) || double.IsInfinity(y))
                throw new ArgumentOutOfRangeException("x/y", "Point coordinates must be finite.");

            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public double DistanceSquaredTo(QuickLinePoint other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            return dx * dx + dy * dy;
        }

        public double DistanceTo(QuickLinePoint other)
            => Math.Sqrt(DistanceSquaredTo(other));

        public bool Equals(QuickLinePoint other)
            => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object obj)
            => obj is QuickLinePoint && Equals((QuickLinePoint)obj);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString() => "(" + X + ", " + Y + ")";

        public static bool operator ==(QuickLinePoint left, QuickLinePoint right)
            => left.Equals(right);

        public static bool operator !=(QuickLinePoint left, QuickLinePoint right)
            => !left.Equals(right);
    }
}
