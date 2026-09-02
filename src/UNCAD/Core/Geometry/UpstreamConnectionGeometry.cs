using System;

namespace UNCAD.Core.Geometry
{
    /// <summary>
    /// Geometry used by the upstream_info/upstream connector writer.  A horizontal
    /// baseline is placed immediately below the info label; the diagonal starts at
    /// the left or right end according to the upstream symbol's direction.
    /// </summary>
    public static class UpstreamConnectionGeometry
    {
        public const double DefaultTolerance = 0.0001;
        public const double DefaultHorizontalGap = 500d;
        public const double DefaultHorizontalLength = 10000d;

        public static bool TryCreatePath(double infoLeft, double infoBottom,
            double infoRight, double infoZ, double upstreamX, double upstreamY,
            double upstreamZ, out ConnectionPath path,
            double horizontalGap = DefaultHorizontalGap,
            double horizontalLength = 0d,
            double tolerance = DefaultTolerance)
        {
            path = default(ConnectionPath);
            double allowed = tolerance < 0 ? 0 : tolerance;
            double leftBound = Math.Min(infoLeft, infoRight);
            double rightBound = Math.Max(infoLeft, infoRight);
            double width = rightBound - leftBound;
            // A positive explicit length keeps the visual scale stable even when
            // the info text is long.  Zero means use the measured info width.
            double length = horizontalLength > allowed
                ? horizontalLength
                : (width > allowed ? width : 0d);
            if (length <= allowed) return false;

            double center = width > allowed
                ? (leftBound + rightBound) / 2d
                : infoLeft;
            double left = center - length / 2d;
            double right = center + length / 2d;
            double y = infoBottom - Math.Max(0d, horizontalGap);

            // A symbol to the left uses the left end; a symbol to the right (or
            // directly below) uses the right end.  The tie-break is deterministic.
            bool useLeft = upstreamX < center - allowed;
            double startX = useLeft ? left : right;
            var stub = new ConnectionSegment(left, y, infoZ, right, y, infoZ);
            var diagonal = new ConnectionSegment(startX, y, infoZ,
                upstreamX, upstreamY, upstreamZ);
            if (SamePoint(diagonal.StartX, diagonal.StartY, diagonal.StartZ,
                diagonal.EndX, diagonal.EndY, diagonal.EndZ, allowed))
                return false;

            path = new ConnectionPath(stub, diagonal, useLeft);
            return true;
        }

        public static bool TryCreate(double infoX, double infoY, double infoZ,
            double upstreamX, double upstreamY, double upstreamZ,
            out ConnectionSegment segment, double tolerance = DefaultTolerance)
        {
            segment = default(ConnectionSegment);
            double allowed = tolerance < 0 ? 0 : tolerance;
            if (SamePoint(infoX, infoY, infoZ, upstreamX, upstreamY, upstreamZ, allowed))
                return false;
            segment = new ConnectionSegment(infoX, infoY, infoZ,
                upstreamX, upstreamY, upstreamZ);
            return true;
        }

        public static bool SameSegment(ConnectionSegment first, ConnectionSegment second,
            double tolerance = DefaultTolerance)
        {
            double allowed = tolerance < 0 ? 0 : tolerance;
            bool forward = Distance(first.StartX, first.StartY, first.StartZ,
                    second.StartX, second.StartY, second.StartZ) <= allowed
                && Distance(first.EndX, first.EndY, first.EndZ,
                    second.EndX, second.EndY, second.EndZ) <= allowed;
            bool reverse = Distance(first.StartX, first.StartY, first.StartZ,
                    second.EndX, second.EndY, second.EndZ) <= allowed
                && Distance(first.EndX, first.EndY, first.EndZ,
                    second.StartX, second.StartY, second.StartZ) <= allowed;
            return forward || reverse;
        }

        private static bool SamePoint(double x1, double y1, double z1,
            double x2, double y2, double z2, double tolerance)
            => Distance(x1, y1, z1, x2, y2, z2) <= tolerance;

        private static double Distance(double x1, double y1, double z1,
            double x2, double y2, double z2)
        {
            double dx = x2 - x1, dy = y2 - y1, dz = z2 - z1;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    public struct ConnectionPath
    {
        public ConnectionPath(ConnectionSegment horizontal, ConnectionSegment diagonal,
            bool usesLeftEnd)
        {
            Horizontal = horizontal;
            Diagonal = diagonal;
            UsesLeftEnd = usesLeftEnd;
        }

        public ConnectionSegment Horizontal { get; }
        public ConnectionSegment Diagonal { get; }
        public bool UsesLeftEnd { get; }
    }

    public struct ConnectionSegment
    {
        public ConnectionSegment(double startX, double startY, double startZ,
            double endX, double endY, double endZ)
        {
            StartX = startX; StartY = startY; StartZ = startZ;
            EndX = endX; EndY = endY; EndZ = endZ;
        }

        public double StartX { get; }
        public double StartY { get; }
        public double StartZ { get; }
        public double EndX { get; }
        public double EndY { get; }
        public double EndZ { get; }
    }
}
