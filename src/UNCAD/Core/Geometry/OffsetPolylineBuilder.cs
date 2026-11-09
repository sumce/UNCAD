using System;
using System.Collections.Generic;
using UNCAD.Core.QuickLine;

namespace UNCAD.Core.Geometry
{
    /// <summary>
    /// 解析式折线偏移构建器（D-050，2026-11-09）。
    ///
    /// 背景（用户报告"桥架红线和基线交叉"）：旧路径对多段线源直接调
    /// AutoCAD Curve.GetOffsetCurves 并把返回的全部碎片上图，不做"是否仍在
    /// 同一侧"校验——带拐角路径在偏移距离超过拐角局部可达距离（短段+急弯）时，
    /// 偏移结果自交翻侧，红线折回基线另一侧。
    ///
    /// 本构建器完全解析式：逐段计算法向偏移端点，拐角用两段偏移线的交点做
    /// miter 斜接；斜接腿超过 MiterLimitFactor×偏移（急弯外角）或交点落入
    /// 错误侧（内角夹断）时退化为 bevel 倒角（倒角端点恒在各自段的正确侧）。
    /// 最后对全部输出顶点与每段中点做"仍在正确侧"校验，任一失败返回 false，
    /// 由调用方退回逐段独立偏移（逐段平行线数学上不可能交叉）。
    /// 纯 Core，无 AutoCAD 依赖。
    /// </summary>
    public static class OffsetPolylineBuilder
    {
        /// <summary>miter 斜接腿上限（×偏移距离），超过退化为 bevel。</summary>
        public const double MiterLimitFactor = 4.0;

        /// <summary>
        /// 构造顶点链的单侧偏移折线。
        /// leftOfTravel=true 表示偏移在链行进方向左手侧（法向 = 方向逆时针转 90°）。
        /// 返回 false = 无法保证不翻侧（调用方退回逐段偏移）。
        /// </summary>
        public static bool TryBuild(IReadOnlyList<QuickLinePoint> vertices,
            double offset, bool leftOfTravel, out List<QuickLinePoint> result)
        {
            result = null;
            if (vertices == null || vertices.Count < 2 || offset <= 0) return false;
            List<QuickLinePoint> chain = Dedupe(vertices);
            if (chain.Count < 2) return false;
            double sign = leftOfTravel ? 1.0 : -1.0;

            // 每段的偏移端点：A[i]=P[i]+s*n*off，B[i]=P[i+1]+s*n*off。
            int segmentCount = chain.Count - 1;
            var a = new QuickLinePoint[segmentCount];
            var b = new QuickLinePoint[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                double dx = chain[i + 1].X - chain[i].X;
                double dy = chain[i + 1].Y - chain[i].Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-9) return false;
                double nx = -dy / length * sign * offset;
                double ny = dx / length * sign * offset;
                a[i] = new QuickLinePoint(chain[i].X + nx, chain[i].Y + ny);
                b[i] = new QuickLinePoint(chain[i + 1].X + nx, chain[i + 1].Y + ny);
            }

            var output = new List<QuickLinePoint>(chain.Count + 4) { a[0] };
            for (int joint = 0; joint < segmentCount - 1; joint++)
            {
                if (!TryMiter(chain, a, b, joint, offset, sign, out QuickLinePoint miter))
                {
                    // bevel：两个端点各自保序（恒在正确侧），拐角切平。
                    output.Add(b[joint]);
                    output.Add(a[joint + 1]);
                    continue;
                }
                output.Add(miter);
            }
            output.Add(b[segmentCount - 1]);

            if (!ValidateSide(chain, output, offset, sign)) return false;
            result = output;
            return true;
        }

        /// <summary>两段偏移线的交点是否可用作 miter：存在、腿长不超限、且在正确侧。</summary>
        private static bool TryMiter(List<QuickLinePoint> chain, QuickLinePoint[] a,
            QuickLinePoint[] b, int joint, double offset, double sign,
            out QuickLinePoint miter)
        {
            miter = default(QuickLinePoint);
            // 平行/共线（同向折线）：直接共享端点。
            double d1x = b[joint].X - a[joint].X, d1y = b[joint].Y - a[joint].Y;
            double d2x = b[joint + 1].X - a[joint + 1].X,
                d2y = b[joint + 1].Y - a[joint + 1].Y;
            double cross = d1x * d2y - d1y * d2x;
            if (Math.Abs(cross) < 1e-12)
            {
                if (b[joint].DistanceTo(a[joint + 1]) < 1e-7)
                {
                    miter = b[joint];
                    return true;
                }
                return false; // 平行但不共线（不可能发生在偏移同侧链上）→ bevel 兜底。
            }
            double t = ((a[joint + 1].X - a[joint].X) * d2y
                        - (a[joint + 1].Y - a[joint].Y) * d2x) / cross;
            miter = new QuickLinePoint(a[joint].X + d1x * t, a[joint].Y + d1y * t);

            // 斜接腿 = 交点到源拐角顶点的距离；急弯外角会射出很远。
            QuickLinePoint vertex = chain[joint + 1];
            if (miter.DistanceTo(vertex) > MiterLimitFactor * offset) return false;
            // 内角夹断保护：交点必须仍在偏移侧且不贴回基线。正常内/外角 miter
            // 到基线的距离恰为偏移值；显著变小 = 短段+急弯的病态夹断，交给
            // bevel 再由全局校验兜底。
            double distance = DistanceToChain(chain, miter, out int nearest,
                out bool interior);
            if (interior && distance < offset * 0.85) return false;
            return true;
        }

        /// <summary>输出折线全体顶点 + 每段中点必须仍在源链的偏移侧。</summary>
        private static bool ValidateSide(List<QuickLinePoint> chain,
            List<QuickLinePoint> output, double offset, double sign)
        {
            for (int i = 0; i < output.Count; i++)
                if (!IsOnOffsetSide(chain, output[i], offset, sign)) return false;
            for (int i = 0; i < output.Count - 1; i++)
            {
                var middle = new QuickLinePoint(
                    (output[i].X + output[i + 1].X) / 2,
                    (output[i].Y + output[i + 1].Y) / 2);
                if (!IsOnOffsetSide(chain, middle, offset, sign)) return false;
            }
            return true;
        }

        private static bool IsOnOffsetSide(List<QuickLinePoint> chain,
            QuickLinePoint point, double offset, double sign)
        {
            double distance = DistanceToChain(chain, point, out int nearest,
                out bool interior);
            if (!interior) return true; // 投影落在链端之外：无侧可言，交由相邻点判定。
            // 正常偏移点到基线的距离恒 ≥ 偏移值（外角 miter 更远）；显著变小
            // （bevel 翅边贴近基线）即病态——整体判 false，调用方退回逐段偏移。
            if (distance < offset * 0.85) return false;
            QuickLinePoint from = chain[nearest];
            QuickLinePoint to = chain[nearest + 1];
            double cross = (to.X - from.X) * (point.Y - from.Y)
                - (to.Y - from.Y) * (point.X - from.X);
            return Math.Sign(cross) == Math.Sign(sign) || Math.Abs(cross) < 1e-9;
        }

        /// <summary>点到折线链的最小距离；nearest=最近段号，interior=垂足是否落在段内。</summary>
        private static double DistanceToChain(List<QuickLinePoint> chain,
            QuickLinePoint point, out int nearest, out bool interior)
        {
            nearest = -1;
            interior = false;
            double best = double.PositiveInfinity;
            for (int i = 0; i < chain.Count - 1; i++)
            {
                double t = ProjectOnSegment(chain[i], chain[i + 1], point);
                double clamped = Math.Max(0.0, Math.Min(1.0, t));
                double dx = chain[i + 1].X - chain[i].X;
                double dy = chain[i + 1].Y - chain[i].Y;
                double px = chain[i].X + dx * clamped;
                double py = chain[i].Y + dy * clamped;
                double distance = Math.Sqrt((point.X - px) * (point.X - px)
                    + (point.Y - py) * (point.Y - py));
                if (distance < best - 1e-12)
                {
                    best = distance;
                    nearest = i;
                    interior = clamped > 1e-9 && clamped < 1.0 - 1e-9;
                }
            }
            return best;
        }

        private static double ProjectOnSegment(QuickLinePoint from, QuickLinePoint to,
            QuickLinePoint point)
        {
            double dx = to.X - from.X, dy = to.Y - from.Y;
            double denominator = dx * dx + dy * dy;
            if (denominator < 1e-18) return 0;
            return ((point.X - from.X) * dx + (point.Y - from.Y) * dy) / denominator;
        }

        private static List<QuickLinePoint> Dedupe(IReadOnlyList<QuickLinePoint> points)
        {
            var result = new List<QuickLinePoint>(points.Count);
            foreach (QuickLinePoint point in points)
            {
                if (result.Count > 0
                    && result[result.Count - 1].DistanceTo(point) < 1e-7)
                    continue;
                result.Add(point);
            }
            return result;
        }
    }
}
