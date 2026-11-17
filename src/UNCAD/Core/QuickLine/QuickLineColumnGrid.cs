using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// 3.3.1/3.3.2（D-054 Amendment）：数字轴刻度（已取整）。Position 是该刻度在
    /// 数字轴（X 或 Y，见 <see cref="QuickLineColumnGrid.NumericAxisIsX"/>）上的
    /// 毫米坐标。
    /// </summary>
    public sealed class QuickLineColumnGridTick
    {
        internal QuickLineColumnGridTick(double position, int number)
        {
            Position = position;
            Number = number;
        }

        public double Position { get; }
        public int Number { get; }
    }

    /// <summary>
    /// 3.3.2（D-054 Amendment）：字母轴标签（U/V/W…）。Position 是该标签在字母轴
    /// （垂直于数字轴的那一向上）的毫米坐标。多字母值（ABC）原样回显。
    /// </summary>
    public sealed class QuickLineColumnGridLetterLabel
    {
        internal QuickLineColumnGridLetterLabel(double position, string letter)
        {
            Position = position;
            Letter = letter ?? string.Empty;
        }

        public double Position { get; }
        public string Letter { get; }
    }

    /// <summary>一次柱网规划的结果（纯数据，控件只画）。</summary>
    public sealed class QuickLineColumnGrid
    {
        internal QuickLineColumnGrid(
            IReadOnlyList<QuickLineColumnGridLine> lines,
            IReadOnlyList<QuickLineColumnGridTick> numericTicks,
            IReadOnlyList<QuickLineColumnGridLetterLabel> letterLabels,
            bool numericAxisIsX,
            QuickLineSpatialPoint upstreamEndpoint,
            QuickLineSpatialPoint downstreamEndpoint,
            double minX, double minY, double maxX, double maxY,
            double spacingMillimetres)
        {
            Lines = lines;
            NumericTicks = numericTicks;
            LetterLabels = letterLabels;
            NumericAxisIsX = numericAxisIsX;
            UpstreamEndpoint = upstreamEndpoint;
            DownstreamEndpoint = downstreamEndpoint;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            SpacingMillimetres = spacingMillimetres;
        }

        /// <summary>地面网格线（覆盖场景包络范围；空=没有线段）。</summary>
        public IReadOnlyList<QuickLineColumnGridLine> Lines { get; }
        /// <summary>
        /// 数字轴插值刻度；两端数字轴解析不出来时为空（只画网格不画刻度，不猜）。
        /// 刻度沿 <see cref="NumericAxisIsX"/> 指定的那个向分布。
        /// </summary>
        public IReadOnlyList<QuickLineColumnGridTick> NumericTicks { get; }
        /// <summary>
        /// 3.3.2：字母轴标签；两端字母轴解析不出来时为空（不猜）。沿垂直于数字
        /// 轴的那一向分布。
        /// </summary>
        public IReadOnlyList<QuickLineColumnGridLetterLabel> LetterLabels { get; }
        /// <summary>
        /// 3.3.2：数字轴沿场景 X（true）还是 Y（false）。由上下游端点的平面位移
        /// 决定（位移大的那向=数字轴，平手取 X）——场景 X 是重建抽象轴，与现实
        /// 柱网方向无固定对应，不能硬编码「数字轴=X」（用户 3.3.1 现场反馈
        /// 「轴位不应该这样显示」的根因）。
        /// </summary>
        public bool NumericAxisIsX { get; }
        /// <summary>上游端点（包络内 X 最小的端点，平手取 Y 再 Z 最小）——上游轴位标签锚点。</summary>
        public QuickLineSpatialPoint UpstreamEndpoint { get; }
        /// <summary>下游端点（X 最大，平手取 Y 再 Z 最大——3.3.2 修订，见 Plan 注释②）。</summary>
        public QuickLineSpatialPoint DownstreamEndpoint { get; }
        /// <summary>3.3.2：场景包络（刻度/标签摆位用，控件不再自行推导）。</summary>
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double SpacingMillimetres { get; }
    }

    /// <summary>
    /// U1U 3D 预览的柱网/轴位标签纯规划器（3.3.1 特性C 引入，3.3.2 D-054
    /// Amendment 扩为两向）。设计与用户确认的方案：
    /// ① 地面（Z=0）柱网——4800mm 柱距（与 QuickLineAxisGridPlanner 同口径），
    ///    数字轴×字母轴**两向正交网格线**覆盖场景包络范围，锚定在上游端点
    ///    （一根柱网线恰好穿过路由上游端点）；网格可由控件开关（默认开）。
    /// ② 端点轴位——上游端点=包络里 X 最小的端点、下游=最大（平手取 Y 再 Z
    ///    最小，确定性），标签文字由模型 UpstreamAxisLabel/DownstreamAxisLabel
    ///    原文提供（解析失败显示原文不猜）。
    /// ③ 数字轴刻度——数字轴=上下游端点平面位移较大的那一向（位移平手取 X；
    ///    场景 X 是抽象轴，与现实柱网方向无对应），两端数字轴都解析出来时按
    ///    端点跨度线性插值，柱网线位置取整标数（48、47、46…）；映射方向以
    ///    端点标签为准对齐（上游端点恰标上游轴号）。超出两端轴号范围或与前
    ///    一刻度同号的跳过；任一端解析不出数字轴→不标该向刻度（不猜）。
    /// ④ 字母轴标签（3.3.2 新增，修正单向插值）——字母轴=垂直于数字轴的那一
    ///    向。两端字母都解析出来时：两端字母相同（路由贴着同一排柱走）→以
    ///    上游端点字母锚定，按 4800 柱距向两侧递推 U/V/W…（多字母值按 26 进制
    ///    索引推）；两端字母不同→按两端字母位置线性插值并夹在两端之间（与
    ///    数字轴同口径，不做端点外推）；两端字母位置重合（场景上无法定序）
    ///    →不标（不猜）。任一端解析不出字母轴→不标该向。
    /// </summary>
    public static class QuickLineColumnGridPlanner
    {
        public const double DefaultSpacingMillimetres =
            QuickLineAxisGridPlanner.DefaultSpacingMillimetres;
        private const int MaximumGridLinesPerDirection = 200;
        private const double MinimumSpanMillimetres = 0.5;

        /// <summary>
        /// 规划一个预览场景的柱网。segments 为空返回 null；两端轴位标签解析
        /// 与否只影响 NumericTicks/LetterLabels（Lines 恒可画）。
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
            // 3.3.2 修订：下游端点平手改取 max Y（再 max Z）——3.3.1 取 min Y
            // 会让纯 Y 向路由的上下游端点收敛到同一个节点（两端轴位钉叠在一
            // 点、位移恒为零），数字轴向判定也随之失效；取相反端才确定。
            QuickLineSpatialPoint downstream = endpoints
                .OrderByDescending(point => point.X)
                .ThenByDescending(point => point.Y)
                .ThenByDescending(point => point.Z).First();

            var lines = new List<QuickLineColumnGridLine>();
            foreach (double x in GridPositions(upstream.X, minX, maxX,
                spacingMillimetres))
                lines.Add(new QuickLineColumnGridLine(true, x, minY, maxY));
            foreach (double y in GridPositions(upstream.Y, minY, maxY,
                spacingMillimetres))
                lines.Add(new QuickLineColumnGridLine(false, y, minX, maxX));

            // 3.3.2：数字轴向=端点平面位移较大的那向（平手取 X，确定性）。
            bool numericAxisIsX = Math.Abs(downstream.X - upstream.X)
                >= Math.Abs(downstream.Y - upstream.Y);

            QuickLineAxisSpan.TryParse(upstreamAxisLabel,
                out QuickLineAxisReference upstreamAxis);
            QuickLineAxisSpan.TryParse(downstreamAxisLabel,
                out QuickLineAxisReference downstreamAxis);

            IReadOnlyList<QuickLineColumnGridTick> numericTicks =
                PlanNumericTicks(upstream, downstream, upstreamAxis,
                    downstreamAxis, numericAxisIsX, minX, minY, maxX, maxY,
                    spacingMillimetres);
            IReadOnlyList<QuickLineColumnGridLetterLabel> letterLabels =
                PlanLetterLabels(upstream, downstream, upstreamAxis,
                    downstreamAxis, numericAxisIsX,
                    numericAxisIsX ? minY : minX,
                    numericAxisIsX ? maxY : maxX,
                    spacingMillimetres);
            return new QuickLineColumnGrid(lines, numericTicks, letterLabels,
                numericAxisIsX, upstream, downstream,
                minX, minY, maxX, maxY, spacingMillimetres);
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
        /// upstreamNumber + (p−upstreamAt)/(downstreamAt−upstreamAt)×(Δnumber)，
        /// 四舍五入取整；超出两端轴号范围或与上一刻度同号的跳过。数字轴向跨度
        /// 退化（两端在该向上几乎重合）时无刻度。参数是纯数字（axis 坐标），
        /// 不预设 X/Y。
        /// </summary>
        public static IReadOnlyList<QuickLineColumnGridTick> InterpolateTicks(
            double upstreamAt, int upstreamNumber,
            double downstreamAt, int downstreamNumber,
            double anchor, double envelopeMinimum, double envelopeMaximum,
            double spacingMillimetres)
        {
            ValidateSpacing(spacingMillimetres);
            var result = new List<QuickLineColumnGridTick>();
            double span = downstreamAt - upstreamAt;
            if (Math.Abs(span) < MinimumSpanMillimetres) return result;
            int lowest = Math.Min(upstreamNumber, downstreamNumber);
            int highest = Math.Max(upstreamNumber, downstreamNumber);
            int? lastNumber = null;
            foreach (double position in GridPositions(anchor, envelopeMinimum,
                envelopeMaximum, spacingMillimetres))
            {
                double exact = upstreamNumber
                    + (position - upstreamAt) / span
                        * (downstreamNumber - upstreamNumber);
                int number = (int)Math.Round(exact,
                    MidpointRounding.AwayFromZero);
                if (number < lowest || number > highest) continue;
                if (lastNumber.HasValue && lastNumber.Value == number) continue;
                lastNumber = number;
                result.Add(new QuickLineColumnGridTick(position, number));
            }
            return result;
        }

        /// <summary>
        /// 3.3.2：字母轴标签规划（见类注释④）。纯数字坐标（letterAxis 坐标），
        /// 不预设 X/Y。
        /// </summary>
        public static IReadOnlyList<QuickLineColumnGridLetterLabel>
            InterpolateLetterLabels(
                double upstreamAt, long upstreamIndex,
                double downstreamAt, long downstreamIndex,
                double anchor, double envelopeMinimum, double envelopeMaximum,
                double spacingMillimetres)
        {
            ValidateSpacing(spacingMillimetres);
            var result = new List<QuickLineColumnGridLetterLabel>();
            if (upstreamIndex < 1 || downstreamIndex < 1) return result;
            double span = downstreamAt - upstreamAt;
            string lastLetter = null;
            if (upstreamIndex == downstreamIndex)
            {
                // 两端字母相同（路由贴同一排柱走）：以上游字母锚定，按 4800
                // 柱距沿坐标正向递推邻居 U/V/W…（覆盖包络；递增方位无台账
                // 信息可定，约定 +坐标=A→Z 并文档化）。
                foreach (double position in GridPositions(anchor,
                    envelopeMinimum, envelopeMaximum, spacingMillimetres))
                {
                    // 网格位置=anchor+k×spacing，字母索引=锚点索引+k（整数，无舍入）。
                    double offsetBays = (position - anchor) / spacingMillimetres;
                    long index = upstreamIndex + (long)Math.Round(offsetBays);
                    string letter = LettersFromIndex(index);
                    if (letter == null || letter == lastLetter) continue;
                    lastLetter = letter;
                    result.Add(new QuickLineColumnGridLetterLabel(
                        position, letter));
                }
                return result;
            }
            if (Math.Abs(span) < MinimumSpanMillimetres)
            {
                // 两端字母不同但位置重合：场景上无法定序，不猜。
                return result;
            }
            // 两端字母不同：按跨度线性插值，夹在两端索引之间（与数字轴同口径）。
            long lowest = Math.Min(upstreamIndex, downstreamIndex);
            long highest = Math.Max(upstreamIndex, downstreamIndex);
            foreach (double position in GridPositions(anchor,
                envelopeMinimum, envelopeMaximum, spacingMillimetres))
            {
                double exact = upstreamIndex
                    + (position - upstreamAt) / span
                        * (downstreamIndex - upstreamIndex);
                long index = (long)Math.Round(exact, MidpointRounding.AwayFromZero);
                if (index < lowest || index > highest) continue;
                string letter = LettersFromIndex(index);
                if (letter == null || letter == lastLetter) continue;
                lastLetter = letter;
                result.Add(new QuickLineColumnGridLetterLabel(position, letter));
            }
            return result;
        }

        /// <summary>
        /// 字母索引→字母串（26 进制双射：1=A … 26=Z、27=AA）。索引 &lt;1 或溢出
        /// 返回 null（调用方跳过，不猜）。
        /// </summary>
        public static string LettersFromIndex(long index)
        {
            if (index < 1) return null;
            var chars = new List<char>();
            long remaining = index;
            while (remaining > 0)
            {
                long digit = (remaining - 1) % 26;
                chars.Add((char)('A' + digit));
                remaining = (remaining - 1) / 26;
            }
            chars.Reverse();
            return new string(chars.ToArray());
        }

        private static IReadOnlyList<QuickLineColumnGridTick> PlanNumericTicks(
            QuickLineSpatialPoint upstream, QuickLineSpatialPoint downstream,
            QuickLineAxisReference upstreamAxis,
            QuickLineAxisReference downstreamAxis,
            bool numericAxisIsX, double minX, double minY,
            double maxX, double maxY, double spacingMillimetres)
        {
            if (upstreamAxis?.HasNumber != true
                || downstreamAxis?.HasNumber != true) return Array.Empty<QuickLineColumnGridTick>();
            double upstreamAt = numericAxisIsX ? upstream.X : upstream.Y;
            double downstreamAt = numericAxisIsX ? downstream.X : downstream.Y;
            double envelopeMinimum = numericAxisIsX ? minX : minY;
            double envelopeMaximum = numericAxisIsX ? maxX : maxY;
            return InterpolateTicks(upstreamAt, upstreamAxis.Number.Value,
                downstreamAt, downstreamAxis.Number.Value,
                upstreamAt, envelopeMinimum, envelopeMaximum,
                spacingMillimetres);
        }

        private static IReadOnlyList<QuickLineColumnGridLetterLabel>
            PlanLetterLabels(QuickLineSpatialPoint upstream,
                QuickLineSpatialPoint downstream,
                QuickLineAxisReference upstreamAxis,
                QuickLineAxisReference downstreamAxis,
                bool numericAxisIsX, double envelopeMinimum,
                double envelopeMaximum, double spacingMillimetres)
        {
            if (upstreamAxis?.HasLetter != true
                || downstreamAxis?.HasLetter != true)
                return Array.Empty<QuickLineColumnGridLetterLabel>();
            // 字母轴=垂直于数字轴的那一向。
            double upstreamAt = numericAxisIsX ? upstream.Y : upstream.X;
            double downstreamAt = numericAxisIsX ? downstream.Y : downstream.X;
            return InterpolateLetterLabels(upstreamAt,
                upstreamAxis.LetterIndex,
                downstreamAt, downstreamAxis.LetterIndex,
                upstreamAt, envelopeMinimum, envelopeMaximum,
                spacingMillimetres);
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
