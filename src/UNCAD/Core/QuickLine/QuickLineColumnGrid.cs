using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>3.3.1（D-054 Amendment，特性C）：地面柱网的一根网格线（常量 X 或常量 Y）。</summary>
    public sealed class QuickLineColumnGridLine
    {
        internal QuickLineColumnGridLine(bool constantX, double fixedValue,
            double minimum, double maximum)
        {
            ConstantX = constantX;
            FixedValue = fixedValue;
            Minimum = minimum;
            Maximum = maximum;
        }

        /// <summary>true=常量 X 线（沿 Y 方向延伸）；false=常量 Y 线（沿 X 方向）。</summary>
        public bool ConstantX { get; }
        public double FixedValue { get; }
        public double Minimum { get; }
        public double Maximum { get; }
    }

    /// <summary>3.3.1（特性C）：一根数字轴柱网线上的插值刻度（已取整）。</summary>
    public sealed class QuickLineColumnGridTick
    {
        internal QuickLineColumnGridTick(double x, int number)
        {
            X = x;
            Number = number;
        }

        public double X { get; }
        public int Number { get; }
    }

    /// <summary>3.3.1（特性C）：一次柱网规划的结果（纯数据，控件只画）。</summary>
    public sealed class QuickLineColumnGrid
    {
        internal QuickLineColumnGrid(
            IReadOnlyList<QuickLineColumnGridLine> lines,
            IReadOnlyList<QuickLineColumnGridTick> ticks,
            QuickLineSpatialPoint upstreamEndpoint,
            QuickLineSpatialPoint downstreamEndpoint,
            double spacingMillimetres)
        {
            Lines = lines;
            Ticks = ticks;
            UpstreamEndpoint = upstreamEndpoint;
            DownstreamEndpoint = downstreamEndpoint;
            SpacingMillimetres = spacingMillimetres;
        }

        /// <summary>地面网格线（覆盖场景包络范围；空=没有线段）。</summary>
        public IReadOnlyList<QuickLineColumnGridLine> Lines { get; }
        /// <summary>数字轴插值刻度；两端数字轴解析不出来时为空（只画网格不画刻度，不猜）。</summary>
        public IReadOnlyList<QuickLineColumnGridTick> Ticks { get; }
        /// <summary>上游端点（包络内 X 最小的端点，平手取 Y 再 Z 最小）——上游轴位标签锚点。</summary>
        public QuickLineSpatialPoint UpstreamEndpoint { get; }
        /// <summary>下游端点（X 最大，平手取 Y 再 Z 最小）。</summary>
        public QuickLineSpatialPoint DownstreamEndpoint { get; }
        public double SpacingMillimetres { get; }
    }

    /// <summary>
    /// 3.3.1（D-054 Amendment，特性C）：U1U 3D 预览的柱网/轴位刻度纯规划器。
    /// 用户现场反馈「3D预览内展示柱位 你没展示」，设计与用户确认的方案：
    /// ① 地面（Z=0）柱网——4800mm 柱距（与 QuickLineAxisGridPlanner 同口径），
    ///    两组正交网格线覆盖场景包络范围，锚定在上游端点（一根柱网线恰好穿过
    ///    路由上游端点）；网格可由控件开关（默认开）。
    /// ② 端点轴位——上游端点=包络里 X 最小的端点、下游=最大（平手取 Y 再 Z
    ///    最小，确定性），标签文字由模型 UpstreamAxisLabel/DownstreamAxisLabel
    ///    原文提供（解析失败显示原文不猜）。
    /// ③ 数字轴插值刻度——两端数字轴都解析出来时按路由 X 跨度线性插值，柱网
    ///    线位置取整标数（48、47、46…）；X 轴与数字轴的映射方向以端点标签为
    ///    准对齐（上游端点 X 恰标上游轴号）。超出两端轴号范围或与前一刻度同号
    ///    的跳过；任一端解析不出数字轴→不画刻度只画网格。
    /// </summary>
    public static class QuickLineColumnGridPlanner
    {
        public const double DefaultSpacingMillimetres =
            QuickLineAxisGridPlanner.DefaultSpacingMillimetres;
        private const int MaximumGridLinesPerDirection = 200;
        private const double MinimumSpanMillimetres = 0.5;

        /// <summary>
        /// 规划一个预览场景的柱网。segments 为空返回 null；两端轴位标签解析
        /// 与否只影响 Ticks（Lines 恒可画）。
        /// </summary>
        public static QuickLineColumnGrid Plan(
            IReadOnlyList<QuickLineOrbitPreviewSegment> segments,
            string upstreamAxisLabel, string downstreamAxisLabel,
            double spacingMillimetres = DefaultSpacingMillimetres)
        {
            ValidateSpacing(spacingMillimetres);
            if (segments == null || segments.Count == 0) return null;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            var endpoints = new List<QuickLineSpatialPoint>(segments.Count * 2);
            foreach (QuickLineOrbitPreviewSegment segment in segments)
            {
                if (segment == null) continue;
                foreach (QuickLineSpatialPoint point in new[]
                    { segment.Start, segment.End })
                {
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);
                    endpoints.Add(point);
                }
            }
            if (endpoints.Count == 0) return null;
            QuickLineSpatialPoint upstream = endpoints
                .OrderBy(point => point.X).ThenBy(point => point.Y)
                .ThenBy(point => point.Z).First();
            QuickLineSpatialPoint downstream = endpoints
                .OrderByDescending(point => point.X).ThenBy(point => point.Y)
                .ThenBy(point => point.Z).First();

            var lines = new List<QuickLineColumnGridLine>();
            foreach (double x in GridPositions(upstream.X, minX, maxX,
                spacingMillimetres))
                lines.Add(new QuickLineColumnGridLine(true, x, minY, maxY));
            foreach (double y in GridPositions(upstream.Y, minY, maxY,
                spacingMillimetres))
                lines.Add(new QuickLineColumnGridLine(false, y, minX, maxX));

            var ticks = new List<QuickLineColumnGridTick>();
            if (QuickLineAxisSpan.TryParse(upstreamAxisLabel,
                    out QuickLineAxisReference upstreamAxis)
                && QuickLineAxisSpan.TryParse(downstreamAxisLabel,
                    out QuickLineAxisReference downstreamAxis)
                && upstreamAxis.HasNumber && downstreamAxis.HasNumber)
            {
                ticks.AddRange(InterpolateTicks(upstream.X,
                    upstreamAxis.Number.Value, downstream.X,
                    downstreamAxis.Number.Value, upstream.X, minX, maxX,
                    spacingMillimetres));
            }
            return new QuickLineColumnGrid(lines, ticks, upstream, downstream,
                spacingMillimetres);
        }

        /// <summary>
        /// 柱网线位置：anchor + k×spacing，覆盖 [minimum, maximum]（首线在
        /// minimum 或其下、末线在 maximum 或其上）。双向各最多 200 根，超出的
        /// 病态跨度按包络中心截断（确定性）。
        /// </summary>
        public static IReadOnlyList<double> GridPositions(double anchor,
            double minimum, double maximum, double spacingMillimetres)
        {
            ValidateSpacing(spacingMillimetres);
            var result = new List<double>();
            if (double.IsNaN(anchor) || double.IsInfinity(anchor)
                || maximum < minimum) return result;
            long first = (long)Math.Floor((minimum - anchor) / spacingMillimetres
                + 1e-9);
            long last = (long)Math.Ceiling((maximum - anchor) / spacingMillimetres
                - 1e-9);
            if (last < first) last = first;
            if (last - first + 1 > MaximumGridLinesPerDirection)
            {
                // 超出预算：以包络中心为基准截取连续窗口。
                double middle = (minimum + maximum) / 2.0;
                long centre = (long)Math.Round((middle - anchor)
                    / spacingMillimetres);
                long half = MaximumGridLinesPerDirection / 2;
                long candidateFirst = Math.Max(first, centre - half);
                long candidateLast = candidateFirst
                    + MaximumGridLinesPerDirection - 1;
                first = candidateFirst;
                last = Math.Min(last, candidateLast);
            }
            for (long k = first; k <= last; k++)
                result.Add(anchor + k * spacingMillimetres);
            return result;
        }

        /// <summary>
        /// 数字轴插值刻度：柱网线位置 p 的轴号 =
        /// upstreamNumber + (p−upstreamX)/(downstreamX−upstreamX)×(Δnumber)，
        /// 四舍五入取整；超出两端轴号范围或与上一刻度同号的跳过。X 跨度退化
        /// （两端 X 几乎重合）时无刻度。
        /// </summary>
        public static IReadOnlyList<QuickLineColumnGridTick> InterpolateTicks(
            double upstreamX, int upstreamNumber,
            double downstreamX, int downstreamNumber,
            double anchorX, double envelopeMinX, double envelopeMaxX,
            double spacingMillimetres)
        {
            ValidateSpacing(spacingMillimetres);
            var result = new List<QuickLineColumnGridTick>();
            double span = downstreamX - upstreamX;
            if (Math.Abs(span) < MinimumSpanMillimetres) return result;
            int lowest = Math.Min(upstreamNumber, downstreamNumber);
            int highest = Math.Max(upstreamNumber, downstreamNumber);
            int? lastNumber = null;
            foreach (double x in GridPositions(anchorX, envelopeMinX,
                envelopeMaxX, spacingMillimetres))
            {
                double exact = upstreamNumber
                    + (x - upstreamX) / span * (downstreamNumber - upstreamNumber);
                int number = (int)Math.Round(exact,
                    MidpointRounding.AwayFromZero);
                if (number < lowest || number > highest) continue;
                if (lastNumber.HasValue && lastNumber.Value == number) continue;
                lastNumber = number;
                result.Add(new QuickLineColumnGridTick(x, number));
            }
            return result;
        }

        private static void ValidateSpacing(double spacingMillimetres)
        {
            if (double.IsNaN(spacingMillimetres)
                || double.IsInfinity(spacingMillimetres)
                || spacingMillimetres <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(spacingMillimetres));
        }
    }
}
