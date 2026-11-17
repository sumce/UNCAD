using System;
using System.Collections.Generic;
using UNCAD.Core.QuickLine;

namespace UNCAD.Core.Geometry
{
    /// <summary>轮廓顶点：位置 + 离开该顶点线段的凸度（AutoCAD LWPolyline 口径）。</summary>
    public readonly struct HoseOutlineVertex
    {
        public HoseOutlineVertex(QuickLinePoint point, double bulge)
        {
            Point = point;
            Bulge = bulge;
        }

        public QuickLinePoint Point { get; }
        public double Bulge { get; }
    }

    /// <summary>
    /// 软管平面轮廓规划器（U1B，3.3.3，D-054 Amendment）。纯 Core、无 AutoCAD 依赖。
    ///
    /// 输入：中心线点列 + 管直径。输出：一条**闭合**轮廓顶点序列（LWPolyline 顶点 +
    /// 凸度，可直接 AddVertexAt），形态为平面"管子"——
    /// - 两侧法向偏移 ±直径/2（双线轮廓，中心线本体不保留）；
    /// - 拐弯处：外侧用半径=管半径的圆角弧过渡（切点即偏移线端点，天然相切），
    ///   内侧用两偏移线交点截断（miter；腿长超过 MiterLimitFactor×半径的病态急弯
    ///   退化为 bevel 直接连两端点，与 OffsetPolylineBuilder 同哲学——有界、不炸）；
    /// - 首尾各一个半径=管半径的半圆端帽封口。
    ///
    /// 退化输入：重复点（&lt;1e-7）合并、共线中间点（转角&lt;1e-6 rad）剔除；清理后
    /// 不足 2 点或直径非法返回 false，由调用方礼貌退出。段长小于直径的极端尖角
    /// （物理上管子拐不过来的画法）内侧 miter 会使轮廓自叠——输出仍有界、不抛，
    /// 与 U1Q 偏移的"整体拒绝再兜底"不同：管子轮廓两侧必须共存，不能退化为
    /// 逐段偏移（那会拆开闭合环）。
    /// </summary>
    public static class HoseOutlinePlanner
    {
        private const double Epsilon = 1e-7;
        private const double CollinearToleranceRadians = 1e-6;

        /// <summary>内侧 miter 腿上限（×管半径），超过退化为 bevel（与
        /// OffsetPolylineBuilder.MiterLimitFactor 同口径）。</summary>
        public const double MiterLimitFactor = 4.0;

        /// <summary>
        /// 规划闭合轮廓。outline 为空/直径非法/清理后点数不足 2 → false。
        /// centerlineLength 恒为中心线折线长度（端帽弧长不计——标注长度用）。
        /// </summary>
        public static bool TryPlan(IReadOnlyList<QuickLinePoint> path, double diameter,
            out List<HoseOutlineVertex> outline, out double centerlineLength)
        {
            outline = null;
            centerlineLength = 0;
            if (path == null || path.Count < 2
                || double.IsNaN(diameter) || double.IsInfinity(diameter)
                || diameter <= Epsilon) return false;
            List<QuickLinePoint> chain = Collapse(path);
            if (chain.Count < 2) return false;

            double radius = diameter / 2.0;
            int segmentCount = chain.Count - 1;
            var directions = new QuickLinePoint[segmentCount];
            double length = 0;
            for (int i = 0; i < segmentCount; i++)
            {
                double dx = chain[i + 1].X - chain[i].X;
                double dy = chain[i + 1].Y - chain[i].Y;
                double l = Math.Sqrt(dx * dx + dy * dy);
                if (l < Epsilon) return false;
                directions[i] = new QuickLinePoint(dx / l, dy / l);
                length += l;
            }

            // 逐顶点组装闭合环：起点端帽 → 左链前进 → 终点端帽 → 右链回退 → 闭合。
            // 凸度记在弧起点顶点上（离开该顶点的线段是弧）。
            var result = new List<HoseOutlineVertex>(chain.Count * 2 + 4);
            QuickLinePoint firstRight = Offset(chain[0], directions[0], radius, false);
            QuickLinePoint firstLeft = Offset(chain[0], directions[0], radius, true);
            result.Add(new HoseOutlineVertex(firstRight,
                CapBulge(chain[0], firstRight,
                    new QuickLinePoint(-directions[0].X, -directions[0].Y))));
            result.Add(new HoseOutlineVertex(firstLeft, 0));
            AppendSide(result, chain, directions, radius, true);
            QuickLinePoint lastLeft = Offset(chain[segmentCount], directions[segmentCount - 1],
                radius, true);
            QuickLinePoint lastRight = Offset(chain[segmentCount],
                directions[segmentCount - 1], radius, false);
            result.Add(new HoseOutlineVertex(lastLeft,
                CapBulge(chain[segmentCount], lastLeft,
                    directions[segmentCount - 1])));
            // 端帽弧的落点必须是显式顶点，否则弧会一直画到右链首个拐角点。
            result.Add(new HoseOutlineVertex(lastRight, 0));
            AppendSide(result, chain, directions, radius, false);

            outline = result;
            centerlineLength = length;
            return true;
        }

        /// <summary>沿中心线折线取距离 s 处的点（s 超界取端点）；用于引线起点=长度中点。</summary>
        public static QuickLinePoint PointAtDistance(IReadOnlyList<QuickLinePoint> path,
            double distance)
        {
            if (path == null || path.Count < 2) throw new ArgumentException(
                "At least two points are required.", nameof(path));
            double remaining = Math.Max(0, distance);
            for (int i = 0; i < path.Count - 1; i++)
            {
                double dx = path[i + 1].X - path[i].X;
                double dy = path[i + 1].Y - path[i].Y;
                double l = Math.Sqrt(dx * dx + dy * dy);
                if (l < Epsilon) continue;
                if (remaining <= l)
                    return new QuickLinePoint(path[i].X + dx / l * remaining,
                        path[i].Y + dy / l * remaining);
                remaining -= l;
            }
            return path[path.Count - 1];
        }

        /// <summary>清理：合并重复点、剔除共线中间点（保留首尾）。</summary>
        private static List<QuickLinePoint> Collapse(IReadOnlyList<QuickLinePoint> points)
        {
            var deduped = new List<QuickLinePoint>(points.Count);
            foreach (QuickLinePoint point in points)
            {
                if (deduped.Count > 0
                    && deduped[deduped.Count - 1].DistanceTo(point) < Epsilon)
                    continue;
                deduped.Add(point);
            }
            var result = new List<QuickLinePoint>(deduped.Count);
            for (int i = 0; i < deduped.Count; i++)
            {
                // 中间点与“上一保留点→本点→下一点”方向共线（转角≈0）→ 剔除，
                // 避免零弧/零miter顶点。
                if (i > 0 && i < deduped.Count - 1 && result.Count >= 1)
                {
                    QuickLinePoint previous = result[result.Count - 1];
                    double dx1 = deduped[i].X - previous.X;
                    double dy1 = deduped[i].Y - previous.Y;
                    double dx2 = deduped[i + 1].X - deduped[i].X;
                    double dy2 = deduped[i + 1].Y - deduped[i].Y;
                    double l1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
                    double l2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
                    if (l1 >= Epsilon && l2 >= Epsilon)
                    {
                        double cross = (dx1 / l1) * (dy2 / l2) - (dy1 / l1) * (dx2 / l2);
                        double dot = (dx1 / l1) * (dx2 / l2) + (dy1 / l1) * (dy2 / l2);
                        double angle = Math.Abs(Math.Atan2(cross, dot));
                        if (angle < CollinearToleranceRadians) continue;
                    }
                }
                result.Add(deduped[i]);
            }
            return result;
        }

        /// <summary>
        /// 追加一条侧链。leftSide=true 沿路径方向前进（左链，拐角按 1..n-1 正序），
        /// false 回退（右链，同一些拐角按逆序追加——侧向与弧向都相对**路径方向**
        /// 判定，与行走方向无关）。外侧拐角=圆弧（半径 r、圆心=路径顶点、
        /// 切点=两偏移端点，天然相切）；内侧=两偏移线交点截断（腿长超限 bevel）。
        /// </summary>
        private static void AppendSide(List<HoseOutlineVertex> result,
            List<QuickLinePoint> chain, QuickLinePoint[] directions,
            double radius, bool leftSide)
        {
            int segmentCount = chain.Count - 1;
            for (int index = 1; index <= segmentCount - 1; index++)
            {
                int vertex = leftSide ? index : segmentCount - index;
                QuickLinePoint dIn = directions[vertex - 1];
                QuickLinePoint dOut = directions[vertex];
                QuickLinePoint joint = chain[vertex];
                // 本侧偏移配对端点：arrival=路径序进入段（sIn）贴近本顶点的一端，
                // departure=路径序离开段（sOut）的一端。左链按 arrival→departure
                // 追加，右链（回退行走）按 departure→arrival 追加。
                QuickLinePoint arrival = Offset(joint, dIn, radius, leftSide);
                QuickLinePoint departure = Offset(joint, dOut, radius, leftSide);
                double turn = dIn.X * dOut.Y - dIn.Y * dOut.X;
                bool inner = leftSide ? turn > 0 : turn < 0;
                if (inner)
                {
                    // 内角：两偏移线相交，取交点截断（近折返的病态急弯交点射远，
                    // 腿长超 MiterLimitFactor×半径时退化为 bevel 直连——有界、不炸）。
                    if (TryIntersect(arrival, dIn, departure, dOut, out QuickLinePoint miter)
                        && miter.DistanceTo(joint) <= MiterLimitFactor * radius)
                    {
                        result.Add(new HoseOutlineVertex(miter, 0));
                        continue;
                    }
                    if (leftSide)
                    {
                        result.Add(new HoseOutlineVertex(arrival, 0));
                        result.Add(new HoseOutlineVertex(departure, 0));
                    }
                    else
                    {
                        result.Add(new HoseOutlineVertex(departure, 0));
                        result.Add(new HoseOutlineVertex(arrival, 0));
                    }
                    continue;
                }
                // 外角：圆弧过渡（软管气质），弧记在行走顺序的第一个端点上。
                if (leftSide)
                {
                    result.Add(new HoseOutlineVertex(arrival,
                        ArcBulge(joint, arrival, departure)));
                    result.Add(new HoseOutlineVertex(departure, 0));
                }
                else
                {
                    result.Add(new HoseOutlineVertex(departure,
                        ArcBulge(joint, departure, arrival)));
                    result.Add(new HoseOutlineVertex(arrival, 0));
                }
            }
        }

        /// <summary>法向偏移点：left=true 取行进方向左手（法向=方向逆时针转 90°）。</summary>
        private static QuickLinePoint Offset(QuickLinePoint point, QuickLinePoint direction,
            double distance, bool left)
        {
            double sign = left ? 1.0 : -1.0;
            return new QuickLinePoint(point.X - direction.Y * sign * distance,
                point.Y + direction.X * sign * distance);
        }

        /// <summary>
        /// 端帽半圆凸度（|bulge|=1，弧终点=起点的直径对点）。直径对点的半圆有
        /// 两个（π 弧在劣弧判定下歧义），必须用 awayDirection（起点帽=-首段方向、
        /// 终点帽=末段方向）指定鼓出侧：CCW 半圆的中点方向 = 半径向量逆时针转
        /// 90°，与 away 同向取 +1、反向取 -1。
        /// </summary>
        private static double CapBulge(QuickLinePoint center, QuickLinePoint from,
            QuickLinePoint awayDirection)
        {
            double radiusX = from.X - center.X;
            double radiusY = from.Y - center.Y;
            double midCcwX = -radiusY;
            double midCcwY = radiusX;
            double dot = midCcwX * awayDirection.X + midCcwY * awayDirection.Y;
            return dot >= 0 ? 1.0 : -1.0;
        }

        /// <summary>
        /// 圆弧（圆心 center、起点 from、终点 to，半径隐含）的 LWPolyline 凸度：
        /// 取劣弧，逆时针扫过为正。tan(扫角/4)。
        /// </summary>
        private static double ArcBulge(QuickLinePoint center, QuickLinePoint from,
            QuickLinePoint to)
        {
            double a0 = Math.Atan2(from.Y - center.Y, from.X - center.X);
            double a1 = Math.Atan2(to.Y - center.Y, to.X - center.X);
            double ccw = a1 - a0;
            while (ccw < 0) ccw += Math.PI * 2.0;
            while (ccw >= Math.PI * 2.0) ccw -= Math.PI * 2.0;
            if (ccw <= Math.PI) return Math.Tan(ccw / 4.0);
            double clockwise = Math.PI * 2.0 - ccw;
            return -Math.Tan(clockwise / 4.0);
        }

        /// <summary>两条过定点、沿单位方向的直线的交点；平行/近平行返回 false。</summary>
        private static bool TryIntersect(QuickLinePoint p1, QuickLinePoint d1,
            QuickLinePoint p2, QuickLinePoint d2, out QuickLinePoint intersection)
        {
            double cross = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(cross) < 1e-12)
            {
                intersection = default(QuickLinePoint);
                return false;
            }
            double t = ((p2.X - p1.X) * d2.Y - (p2.Y - p1.Y) * d2.X) / cross;
            intersection = new QuickLinePoint(p1.X + d1.X * t, p1.Y + d1.Y * t);
            return true;
        }
    }
}
