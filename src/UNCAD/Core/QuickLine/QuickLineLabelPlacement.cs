using System;

namespace UNCAD.Core.QuickLine
{
    /// <summary>毫米标相对其线段的所在侧（按文字自己的阅读方向计）。</summary>
    public enum QuickLineLabelSide
    {
        /// <summary>文字压在线上。</summary>
        OnLine = 0,
        /// <summary>阅读方向的左侧（水平线＝线上方）。</summary>
        Left = 1,
        /// <summary>阅读方向的右侧（水平线＝线下方）。</summary>
        Right = -1
    }

    /// <summary>一次毫米标归位的计算结果（图纸 XY 坐标，弧度）。</summary>
    public sealed class QuickLineLabelPlacement
    {
        internal QuickLineLabelPlacement(double anchorX, double anchorY,
            double rotation, QuickLineLabelSide side, bool readsReversed)
        {
            AnchorX = anchorX;
            AnchorY = anchorY;
            Rotation = rotation;
            Side = side;
            ReadsReversed = readsReversed;
        }

        /// <summary>新的定位点（左＝底中，右＝顶中，压线＝中心）。</summary>
        public double AnchorX { get; }
        public double AnchorY { get; }
        /// <summary>新的旋转角：与线段平行，保持文字原有的阅读方向。</summary>
        public double Rotation { get; }
        /// <summary>保持的所在侧。</summary>
        public QuickLineLabelSide Side { get; }
        /// <summary>文字原本是倒着读的（与线段正向阅读角相差约 180°）——保持倒读。</summary>
        public bool ReadsReversed { get; }
    }

    /// <summary>
    /// D-058（U1UL）：毫米标归位的纯几何规划——只改「离线的距离」和「角度」，
    /// 所在侧沿用毫米标现在的位置（用户把它挪到另一侧就留在那一侧），沿线
    /// 方向的位置也保持（用户沿线拖动过的位置不被拉回中点）。
    ///
    /// 与 U1L 生成毫米标的口径一致：阅读角＝线段角度翻转到可读方向；上方
    /// （阅读方向左侧）用底中对齐，下方（右侧）用顶中对齐，文字背离线段展开。
    /// </summary>
    public static class QuickLineLabelPlacementPlanner
    {
        /// <summary>只靠位置匹配时的角度差上限（用户口径：差 5° 是正常的）。</summary>
        public const double PositionMatchAngleToleranceRadians = 10.0 * Math.PI / 180.0;
        /// <summary>只靠位置匹配时的离线上限（字高的倍数）。</summary>
        public const double PositionMatchHeightFactor = 4.0;
        /// <summary>只靠位置匹配时的离线下限（图形单位）。</summary>
        public const double PositionMatchMinimumDistance = 300.0;

        /// <summary>
        /// D-059（U1UL）：一个没有关联元数据的毫米标是不是贴在这条线段旁——沿线
        /// 范围之内（两端各让 2%）、离线不超过 max(4 倍字高, 300)、与线段夹角
        /// 10° 以内。用户会沿线挪毫米标、换侧、角度差几度，这些都不影响认领。
        /// </summary>
        public static bool SitsBesideLine(double anchorX, double anchorY,
            double height, double rotation, QuickLinePoint lineStart,
            QuickLinePoint lineEnd)
        {
            if (lineStart == null) throw new ArgumentNullException(nameof(lineStart));
            if (lineEnd == null) throw new ArgumentNullException(nameof(lineEnd));
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 1e-18) return false;
            double length = Math.Sqrt(lengthSquared);
            double offsetX = anchorX - lineStart.X;
            double offsetY = anchorY - lineStart.Y;
            double parameter = (offsetX * dx + offsetY * dy) / lengthSquared;
            if (parameter < -0.02 || parameter > 1.02) return false;
            double perpendicular = Math.Abs(offsetX * dy - offsetY * dx) / length;
            if (perpendicular > Math.Max(height * PositionMatchHeightFactor,
                PositionMatchMinimumDistance)) return false;
            return IsAngleWithinTolerance(Math.Atan2(dy, dx), rotation,
                PositionMatchAngleToleranceRadians);
        }

        /// <summary>无方向线段的夹角判定（与 U1LX 的相邻标口径一致）。</summary>
        private static bool IsAngleWithinTolerance(double lineAngle, double labelAngle,
            double toleranceRadians)
        {
            if (double.IsNaN(lineAngle) || double.IsInfinity(lineAngle)
                || double.IsNaN(labelAngle) || double.IsInfinity(labelAngle))
                return false;
            double difference = Math.Abs(NormalizeHalfTurn(lineAngle)
                - NormalizeHalfTurn(labelAngle));
            difference = Math.Min(difference, Math.PI - difference);
            return difference <= toleranceRadians;
        }

        private static double NormalizeHalfTurn(double angle)
        {
            double normalized = angle % Math.PI;
            if (normalized < 0.0) normalized += Math.PI;
            return normalized;
        }

        /// <summary>线段正向阅读角（弧度，(-90°, 90°]），与 GeoMath.ReadableAngle 同口径。</summary>
        public static double ReadableAngle(QuickLinePoint lineStart, QuickLinePoint lineEnd)
        {
            if (lineStart == null) throw new ArgumentNullException(nameof(lineStart));
            if (lineEnd == null) throw new ArgumentNullException(nameof(lineEnd));
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;
            if (double.IsNaN(dx) || double.IsInfinity(dx)
                || double.IsNaN(dy) || double.IsInfinity(dy))
                throw new ArgumentException("线段坐标必须是有限数。", nameof(lineEnd));
            double angle = Math.Atan2(dy, dx);
            if (angle < 0.0) angle += Math.PI * 2.0;
            if (angle > Math.PI * 0.5 && angle <= Math.PI * 1.5) angle += Math.PI;
            return Normalize(angle);
        }

        /// <summary>
        /// 规划一个毫米标的归位。anchor 是毫米标当前的定位点，currentRotation
        /// 是它当前的旋转角，offset 是目标离线距离，defaultAbove 是文字压在
        /// 线上时采用的缺省侧（U1SET 位置＝上侧为 true）。
        /// </summary>
        public static QuickLineLabelPlacement Plan(QuickLinePoint lineStart,
            QuickLinePoint lineEnd, QuickLinePoint anchor, double currentRotation,
            double offset, bool defaultAbove)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            if (double.IsNaN(offset) || double.IsInfinity(offset) || offset < 0.0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (double.IsNaN(currentRotation) || double.IsInfinity(currentRotation))
                throw new ArgumentOutOfRangeException(nameof(currentRotation));
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 1e-18)
                throw new ArgumentException("线段长度为零，无法归位毫米标。", nameof(lineEnd));

            // 目标角度：与线段平行；文字原本倒着读（与正向阅读角相差超过 90°）
            // 就保持倒读，不替用户翻回来。
            double readable = ReadableAngle(lineStart, lineEnd);
            double difference = Normalize(currentRotation - readable);
            bool readsReversed = Math.Abs(difference) > Math.PI * 0.5;
            double rotation = readsReversed ? Normalize(readable + Math.PI) : readable;

            // 所在侧：按文字自己的阅读方向算左右（阅读方向的左侧＝上方）。
            double directionX = Math.Cos(rotation);
            double directionY = Math.Sin(rotation);
            double cross = directionX * (anchor.Y - lineStart.Y)
                - directionY * (anchor.X - lineStart.X);
            double onLineTolerance = Math.Max(1e-12, Math.Sqrt(lengthSquared) * 1e-9);
            QuickLineLabelSide side;
            if (Math.Abs(cross) <= onLineTolerance)
                side = QuickLineLabelSide.OnLine;
            else
                side = cross > 0.0 ? QuickLineLabelSide.Left : QuickLineLabelSide.Right;
            int sideSign = side == QuickLineLabelSide.OnLine
                ? (defaultAbove ? 1 : -1)
                : (int)side;

            // 沿线位置保持：把当前定位点投影到线上（夹在线段两端之间）。
            double parameter = ((anchor.X - lineStart.X) * dx
                + (anchor.Y - lineStart.Y) * dy) / lengthSquared;
            if (double.IsNaN(parameter)) parameter = 0.5;
            if (parameter < 0.0) parameter = 0.0;
            if (parameter > 1.0) parameter = 1.0;
            double footX = lineStart.X + dx * parameter;
            double footY = lineStart.Y + dy * parameter;
            double normalX = -directionY;
            double normalY = directionX;
            return new QuickLineLabelPlacement(
                footX + normalX * offset * sideSign,
                footY + normalY * offset * sideSign,
                rotation, side, readsReversed);
        }

        private static double Normalize(double angle)
        {
            double normalized = angle % (Math.PI * 2.0);
            if (normalized > Math.PI) normalized -= Math.PI * 2.0;
            if (normalized <= -Math.PI) normalized += Math.PI * 2.0;
            return normalized;
        }
    }
}
