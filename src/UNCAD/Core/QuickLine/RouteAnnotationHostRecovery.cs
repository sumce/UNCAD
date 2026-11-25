using System;
using System.Collections.Generic;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// D-059：桥架/线管标注的宿主按红线几何恢复。标注的元数据宿主对不上时
    /// （源线被合并/删除、旧图纸没有元数据），把红线（一根或多根相连的多段线）
    /// 拆成一段一段，每一小段找旁边最近、与之平行、带毫米标的电缆线段——找到的
    /// 电缆线段合起来就是这一趟的宿主；标注上的长度是**整趟**的长度，不是每段
    /// 各有这么长。
    /// </summary>
    public static class RouteAnnotationHostRecovery
    {
        /// <summary>红线离电缆的最大垂距（图形单位）：U1Q/U1C 的红线紧贴电缆。</summary>
        public const double DefaultMaximumOffset = 600.0;
        /// <summary>红线小段与电缆的最大夹角（弧度，按无方向线段计）。</summary>
        public const double DefaultAngleToleranceRadians = Math.PI / 12.0;

        /// <summary>
        /// redLinePieces：每根红线的顶点序列（直线=两点；多段线=各顶点，弧按弦）。
        /// cables：带毫米标的电缆线段。返回归属到的电缆线段 id（去重，按首次
        /// 命中顺序）；一段都对不上时返回空表——调用方照旧报警，不猜。
        /// </summary>
        public static IReadOnlyList<string> ResolveHosts(
            IEnumerable<IReadOnlyList<QuickLinePoint>> redLinePieces,
            IEnumerable<QuickLineSegment> cables,
            double maximumOffset = DefaultMaximumOffset,
            double angleToleranceRadians = DefaultAngleToleranceRadians)
        {
            if (double.IsNaN(maximumOffset) || double.IsInfinity(maximumOffset)
                || maximumOffset <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(maximumOffset));
            if (double.IsNaN(angleToleranceRadians)
                || double.IsInfinity(angleToleranceRadians)
                || angleToleranceRadians < 0.0
                || angleToleranceRadians > Math.PI / 2.0)
                throw new ArgumentOutOfRangeException(nameof(angleToleranceRadians));
            List<QuickLineSegment> candidates = new List<QuickLineSegment>();
            foreach (QuickLineSegment cable in cables ?? Array.Empty<QuickLineSegment>())
                if (cable != null) candidates.Add(cable);

            var hosts = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IReadOnlyList<QuickLinePoint> piece in redLinePieces
                ?? Array.Empty<IReadOnlyList<QuickLinePoint>>())
            {
                if (piece == null) continue;
                for (int index = 0; index + 1 < piece.Count; index++)
                {
                    QuickLinePoint start = piece[index];
                    QuickLinePoint end = piece[index + 1];
                    if (start == null || end == null) continue;
                    string host = FindNearestCable(start, end, candidates,
                        maximumOffset, angleToleranceRadians);
                    if (host != null && seen.Add(host)) hosts.Add(host);
                }
            }
            return hosts;
        }

        /// <summary>
        /// 3.8.0.1：同上，直接吃线段对象（每段拆成两点折线）。给「文字旁边的
        /// 红线已经认成线段」的调用方用。
        /// </summary>
        public static IReadOnlyList<string> ResolveHosts(
            IEnumerable<QuickLineSegment> redLineSegments,
            IEnumerable<QuickLineSegment> cables,
            double maximumOffset = DefaultMaximumOffset,
            double angleToleranceRadians = DefaultAngleToleranceRadians)
        {
            var pieces = new List<IReadOnlyList<QuickLinePoint>>();
            foreach (QuickLineSegment line in redLineSegments
                ?? Array.Empty<QuickLineSegment>())
            {
                if (line == null) continue;
                pieces.Add(new[] { line.Start, line.End });
            }
            return ResolveHosts(pieces, cables, maximumOffset,
                angleToleranceRadians);
        }

        private static string FindNearestCable(QuickLinePoint start, QuickLinePoint end,
            List<QuickLineSegment> cables, double maximumOffset, double angleTolerance)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 1e-18) return null;
            double length = Math.Sqrt(lengthSquared);
            double ux = dx / length;
            double uy = dy / length;
            string best = null;
            double bestScore = double.PositiveInfinity;
            foreach (QuickLineSegment cable in cables)
            {
                double cx = cable.End.X - cable.Start.X;
                double cy = cable.End.Y - cable.Start.Y;
                double cableLengthSquared = cx * cx + cy * cy;
                if (cableLengthSquared <= 1e-18) continue;
                double sine = Math.Abs(ux * cy - uy * cx);
                double cosine = Math.Abs(ux * cx + uy * cy);
                double angle = Math.Atan2(sine, cosine);
                if (angle > angleTolerance) continue;

                // 红线小段中点到电缆所在直线的垂距。
                double midX = (start.X + end.X) / 2.0 - cable.Start.X;
                double midY = (start.Y + end.Y) / 2.0 - cable.Start.Y;
                double perpendicular = Math.Abs(midX * cy - midY * cx)
                    / Math.Sqrt(cableLengthSquared);
                if (perpendicular > maximumOffset) continue;

                // 两段必须有一段投影重叠（红线缩短了也算，只要还搭在电缆上）。
                double alongStart = ((start.X - cable.Start.X) * cx
                    + (start.Y - cable.Start.Y) * cy) / cableLengthSquared;
                double alongEnd = ((end.X - cable.Start.X) * cx
                    + (end.Y - cable.Start.Y) * cy) / cableLengthSquared;
                double low = Math.Max(0.0, Math.Min(alongStart, alongEnd));
                double high = Math.Min(1.0, Math.Max(alongStart, alongEnd));
                if (high - low <= 1e-9) continue;

                // 垂距为主；重叠越多越好（相邻两根平行电缆离得近时取搭得更多的）。
                double score = perpendicular - (high - low) * Math.Sqrt(cableLengthSquared)
                    * 0.01;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = cable.Id;
                }
            }
            return best;
        }
    }
}
