using System;
using Autodesk.AutoCAD.Geometry;

namespace UNCAD.Cad
{
    /// <summary>
    /// 几何计算（AutoCAD 几何类型封装）——所有功能共用的几何逻辑都收在这里。
    /// </summary>
    public static class GeoMath
    {
        /// <summary>对应 AutoLISP (polar p ang dist)。</summary>
        public static Point3d Polar(Point3d p, double ang, double dist)
        {
            return new Point3d(
                p.X + Math.Cos(ang) * dist,
                p.Y + Math.Sin(ang) * dist,
                p.Z);
        }

        /// <summary>对应 AutoLISP (angle p1 p2)。</summary>
        public static double Angle(Point3d from, Point3d to)
        {
            return from.GetVectorTo(to).AngleOnPlane(new Plane());
        }

        public static Point3d Mid(Point3d a, Point3d b)
        {
            return a + (b - a) / 2.0;
        }

        /// <summary>
        /// 正向阅读角：线段角度翻转到 0~180°（避免文字倒置）。
        /// UNC_LINE / UNC_TRAY / UNC_ARCH 共用。
        /// </summary>
        public static double ReadableAngle(Point3d from, Point3d to)
        {
            double ang = Angle(from, to);
            if (ang > Math.PI * 0.5 && ang <= Math.PI * 1.5) ang += Math.PI;
            return ang;
        }

        /// <summary>侧向垂直方向：true=上方(readAng+90°)，false=下方(readAng-90°)。</summary>
        public static double SideDirection(double readAng, bool above)
        {
            return above ? readAng + Math.PI / 2 : readAng - Math.PI / 2;
        }

        /// <summary>
        /// 半圆弧方向判断（与 LISP 启发式一致）：
        /// true = 需反转绘制顺序（start=BP2, end=BP1），使弧凸向"上方"。
        /// </summary>
        public static bool ArcNeedReverse(Point3d c, Point3d bp1, Point3d bp2, double radius)
        {
            var c2d = new Point3d(c.X, c.Y, 0);
            var bp12d = new Point3d(bp1.X, bp1.Y, 0);
            var bp22d = new Point3d(bp2.X, bp2.Y, 0);

            Point3d mid1 = Polar(c2d, Angle(c2d, bp12d) + Math.PI / 2, radius);
            Point3d mid2 = Polar(c2d, Angle(c2d, bp22d) + Math.PI / 2, radius);

            if (Math.Abs(mid1.Y - mid2.Y) < 1e-9)
                return mid1.X <= mid2.X;
            return mid1.Y < mid2.Y;
        }
    }
}
