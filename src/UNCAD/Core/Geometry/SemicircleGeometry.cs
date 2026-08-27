using System;

namespace UNCAD.Core.Geometry
{
    public static class SemicircleGeometry
    {
        public static bool CanSplit(double totalLength, double diameter,
            double centerDistance, double tolerance = 1e-7)
        {
            if (double.IsNaN(totalLength) || double.IsInfinity(totalLength)
                || double.IsNaN(diameter) || double.IsInfinity(diameter)
                || diameter <= tolerance || totalLength <= diameter + tolerance) return false;
            double radius = diameter / 2.0;
            return centerDistance > radius + tolerance
                && totalLength - centerDistance > radius + tolerance;
        }

        public static bool NeedReverse(double centerX, double centerY,
            double firstX, double firstY, double secondX, double secondY, double radius)
        {
            double angle1 = Math.Atan2(firstY - centerY, firstX - centerX) + Math.PI / 2.0;
            double angle2 = Math.Atan2(secondY - centerY, secondX - centerX) + Math.PI / 2.0;
            double mid1X = centerX + Math.Cos(angle1) * radius;
            double mid1Y = centerY + Math.Sin(angle1) * radius;
            double mid2X = centerX + Math.Cos(angle2) * radius;
            double mid2Y = centerY + Math.Sin(angle2) * radius;
            if (Math.Abs(mid1Y - mid2Y) < 1e-9) return mid1X <= mid2X;
            return mid1Y < mid2Y;
        }

        public static void Angles(double centerX, double centerY,
            double firstX, double firstY, double secondX, double secondY, double radius,
            out double startAngle, out double endAngle)
        {
            bool reverse = NeedReverse(centerX, centerY,
                firstX, firstY, secondX, secondY, radius);
            double startX = reverse ? secondX : firstX;
            double startY = reverse ? secondY : firstY;
            startAngle = Math.Atan2(startY - centerY, startX - centerX);
            while (startAngle < 0) startAngle += Math.PI * 2.0;
            while (startAngle >= Math.PI * 2.0) startAngle -= Math.PI * 2.0;
            endAngle = startAngle + Math.PI;
        }
    }
}
