using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// 3.8.0.1：旧图框的桥架/线管标注文字 → 它自己的那根红线。
    ///
    /// 现场情况：文字可能竖排或斜排（和红线不平行）、红线可能只画了一小段、
    /// 没有归属元数据。做法是按文字外框中心找附近没有毫米标的线段，先挑与文字
    /// 大致平行的、再按距离，接着顺着端点相连的红线扩成链；返回几条候选链，
    /// 由调用方用 <see cref="RouteAnnotationHostRecovery"/> 验证哪条旁边真有
    /// 带毫米标的电缆。纯几何，不碰 CAD。
    /// </summary>
    public static class RouteAnnotationLineRecovery
    {
        /// <summary>搜索半径＝max(字高 × 6, 600) + 文字外框半对角线。</summary>
        public const double SearchHeightFactor = 6.0;
        public const double SearchMinimum = 600.0;
        /// <summary>相邻红线端点相接的容差。</summary>
        public const double ChainTolerance = 50.0;
        /// <summary>文字与红线的最大夹角（只用于优先排序，不用于排除）。</summary>
        public const double AngleToleranceRadians = Math.PI / 9.0;
        public const int DefaultMaximumSeeds = 6;
        public const int DefaultMaximumChain = 32;

        /// <summary>
        /// 返回候选链（每条 = 一组相连的线段 id），先平行、后距离。besideRedLine
        /// = 文字旁边确实有候选红线（false 时调用方才退回「就近电缆」绑定）。
        /// excludedIds 是已经归属给别的标注的红线，不再参与。
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<QuickLineSegment>> FindRedLineChains(
            RouteAnnotationTextStamp stamp,
            IEnumerable<QuickLineSegment> lines,
            ISet<string> excludedIds,
            out bool besideRedLine,
            int maximumSeeds = DefaultMaximumSeeds,
            int maximumChain = DefaultMaximumChain)
        {
            besideRedLine = false;
            var result = new List<IReadOnlyList<QuickLineSegment>>();
            if (stamp == null || lines == null) return result;
            if (maximumSeeds < 1) maximumSeeds = 1;
            if (maximumChain < 1) maximumChain = 1;
            List<QuickLineSegment> candidates = lines
                .Where(line => line != null
                    && (excludedIds == null || !excludedIds.Contains(line.Id)))
                .ToList();
            if (candidates.Count == 0) return result;
            QuickLinePoint center = stamp.Center;
            double height = Math.Max(stamp.Height, 1.0);
            double radius = Math.Max(height * SearchHeightFactor, SearchMinimum)
                + stamp.ExtentRadiusMillimetres;
            // 平行的近线优先；远处的「平行」线不比近处的不平行线优先（竖排
            // 文字旁边很容易有一根不相干的竖线）。
            double parallelReach = Math.Max(height * 3.0, SearchMinimum * 1.5);
            List<QuickLineSegment> seeds = candidates
                .Select(line => new
                {
                    Line = line,
                    Distance = DistanceToSegment(center, line),
                    Parallel = IsParallelToText(stamp, line)
                })
                .Where(item => item.Distance <= radius)
                .OrderBy(item => item.Parallel && item.Distance <= parallelReach
                    ? item.Distance : item.Distance + radius)
                .ThenBy(item => item.Distance)
                .Select(item => item.Line)
                .Take(maximumSeeds)
                .ToList();
            if (seeds.Count == 0) return result;
            besideRedLine = true;
            foreach (QuickLineSegment seed in seeds)
            {
                if (excludedIds != null && excludedIds.Contains(seed.Id)) continue;
                result.Add(BuildChain(seed, candidates, excludedIds, maximumChain));
            }
            return result;
        }

        /// <summary>顺着端点相接的红线扩展成一条链（多段线拆开、多条红线共用一个标注）。</summary>
        public static IReadOnlyList<QuickLineSegment> BuildChain(
            QuickLineSegment seed, IEnumerable<QuickLineSegment> lines,
            ISet<string> excludedIds = null, int maximumChain = DefaultMaximumChain)
        {
            var chain = new List<QuickLineSegment> { seed };
            var indexed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                seed.Id
            };
            for (bool grew = true; grew && chain.Count < maximumChain;)
            {
                grew = false;
                foreach (QuickLineSegment line in lines ?? Enumerable.Empty<QuickLineSegment>())
                {
                    if (line == null || indexed.Contains(line.Id)) continue;
                    if (excludedIds != null && excludedIds.Contains(line.Id)) continue;
                    if (!chain.Any(piece => EndpointsTouch(piece, line, ChainTolerance)))
                        continue;
                    chain.Add(line);
                    indexed.Add(line.Id);
                    grew = true;
                    if (chain.Count >= maximumChain) break;
                }
            }
            return chain;
        }

        /// <summary>点到线段的最短距离。</summary>
        public static double DistanceToSegment(QuickLinePoint point, QuickLineSegment line)
        {
            double dx = line.End.X - line.Start.X;
            double dy = line.End.Y - line.Start.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 1e-18) return point.DistanceTo(line.Start);
            double parameter = ((point.X - line.Start.X) * dx
                + (point.Y - line.Start.Y) * dy) / lengthSquared;
            if (parameter < 0.0) parameter = 0.0;
            if (parameter > 1.0) parameter = 1.0;
            return point.DistanceTo(new QuickLinePoint(line.Start.X + dx * parameter,
                line.Start.Y + dy * parameter));
        }

        /// <summary>文字与线段是否大致平行（无方向，±20°）；读不到旋转角时算平行。</summary>
        public static bool IsParallelToText(RouteAnnotationTextStamp stamp,
            QuickLineSegment line)
        {
            if (!stamp.RotationRadians.HasValue
                || double.IsNaN(stamp.RotationRadians.Value)) return true;
            double lineAngle = Math.Atan2(line.End.Y - line.Start.Y,
                line.End.X - line.Start.X);
            return IsAngleWithinTolerance(lineAngle,
                stamp.RotationRadians.Value, AngleToleranceRadians);
        }

        private static bool EndpointsTouch(QuickLineSegment left, QuickLineSegment right,
            double tolerance)
            => left.Start.DistanceTo(right.Start) <= tolerance
                || left.Start.DistanceTo(right.End) <= tolerance
                || left.End.DistanceTo(right.Start) <= tolerance
                || left.End.DistanceTo(right.End) <= tolerance;

        private static bool IsAngleWithinTolerance(double first, double second,
            double toleranceRadians)
        {
            if (double.IsNaN(first) || double.IsInfinity(first)
                || double.IsNaN(second) || double.IsInfinity(second)
                || double.IsNaN(toleranceRadians)
                || double.IsInfinity(toleranceRadians)
                || toleranceRadians < 0.0 || toleranceRadians > Math.PI / 2.0)
                return false;
            double difference = Math.Abs(NormalizeAngle(first) - NormalizeAngle(second));
            difference = Math.Min(difference, Math.PI - difference);
            return difference <= toleranceRadians;
        }

        private static double NormalizeAngle(double angle)
        {
            double normalized = angle % (2.0 * Math.PI);
            if (normalized < 0.0) normalized += 2.0 * Math.PI;
            return normalized;
        }
    }
}
