using System;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.Geometry;

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
        /// U1L / U1Q / U1R 共用。
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
            return SemicircleGeometry.NeedReverse(c.X, c.Y,
                bp1.X, bp1.Y, bp2.X, bp2.Y, radius);
        }

        /// <summary>返回AutoCAD Arc构造函数需要的逆时针起止角，圆心与端点位于WCS XY平面。</summary>
        public static void SemicircleAngles(Point3d center, Point3d bp1, Point3d bp2,
            double radius, out double startAngle, out double endAngle)
        {
            SemicircleGeometry.Angles(center.X, center.Y,
                bp1.X, bp1.Y, bp2.X, bp2.Y, radius, out startAngle, out endAngle);
        }
    }
}
