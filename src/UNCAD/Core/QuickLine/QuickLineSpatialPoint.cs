using System;

namespace UNCAD.Core.QuickLine
{
    /// <summary>CAD-independent point in the reconstructed orthogonal route.</summary>
    public struct QuickLineSpatialPoint : IEquatable<QuickLineSpatialPoint>
    {
        public QuickLineSpatialPoint(double x, double y, double z)
        {
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
                throw new ArgumentOutOfRangeException("x/y/z",
                    "Spatial point coordinates must be finite.");
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public double DistanceTo(QuickLineSpatialPoint other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            double dz = Z - other.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public bool Equals(QuickLineSpatialPoint other)
            => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj)
            => obj is QuickLineSpatialPoint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }

        public static QuickLineSpatialPoint operator +(
            QuickLineSpatialPoint point, QuickLineSpatialVector vector)
            => new QuickLineSpatialPoint(point.X + vector.X,
                point.Y + vector.Y, point.Z + vector.Z);

        public static QuickLineSpatialPoint operator -(
            QuickLineSpatialPoint point, QuickLineSpatialVector vector)
            => new QuickLineSpatialPoint(point.X - vector.X,
                point.Y - vector.Y, point.Z - vector.Z);

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Single-axis displacement used while embedding a route.</summary>
    public struct QuickLineSpatialVector
    {
        public QuickLineSpatialVector(double x, double y, double z)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)
                || double.IsNaN(y) || double.IsInfinity(y)
                || double.IsNaN(z) || double.IsInfinity(z))
                throw new ArgumentOutOfRangeException("x/y/z");
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static QuickLineSpatialVector operator *(
            QuickLineSpatialVector vector, double factor)
            => new QuickLineSpatialVector(vector.X * factor,
                vector.Y * factor, vector.Z * factor);
    }
}
