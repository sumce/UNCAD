using System;

namespace UNCAD.Core.Geometry
{
    /// <summary>
    /// Immutable axis-aligned frame boundary used by CAD collection and pure geometry tests.
    /// Coordinates are normalized so callers do not depend on block insertion direction.
    /// </summary>
    public sealed class FrameRectangle
    {
        public FrameRectangle(string key, double x1, double y1, double x2, double y2)
        {
            Key = key ?? "";
            MinX = Math.Min(x1, x2);
            MinY = Math.Min(y1, y2);
            MaxX = Math.Max(x1, x2);
            MaxY = Math.Max(y1, y2);
        }

        public string Key { get; }
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;

        public bool Contains(double x, double y)
        {
            double tolerance = Tolerance();
            return x >= MinX - tolerance && x <= MaxX + tolerance
                && y >= MinY - tolerance && y <= MaxY + tolerance;
        }

        public bool Intersects(double minX, double minY, double maxX, double maxY)
        {
            double tolerance = Tolerance();
            return maxX >= MinX - tolerance && minX <= MaxX + tolerance
                && maxY >= MinY - tolerance && minY <= MaxY + tolerance;
        }

        private double Tolerance()
            => Math.Max(0.000001, Math.Max(Width, Height) * 0.000000001);

        public static int CompareReadingOrder(FrameRectangle left, FrameRectangle right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;
            int vertical = right.MaxY.CompareTo(left.MaxY);
            return vertical != 0 ? vertical : left.MinX.CompareTo(right.MinX);
        }
    }
}
