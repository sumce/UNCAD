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
            bool numbersIncreaseWithAxis,
            bool lettersIncreaseWithAxis,
            QuickLineSpatialPoint? upstreamEndpoint,
            QuickLineSpatialPoint? downstreamEndpoint,
            QuickLineSpatialPoint gridAnchor,
            double minX, double minY, double maxX, double maxY,
            double spacingMillimetres, bool directionResolved,
            IList<string> diagnostics = null)
        {
            Lines = lines;
            NumericTicks = numericTicks;
            LetterLabels = letterLabels;
            NumericAxisIsX = numericAxisIsX;
            NumbersIncreaseWithAxis = numbersIncreaseWithAxis;
            LettersIncreaseWithAxis = lettersIncreaseWithAxis;
            UpstreamEndpoint = upstreamEndpoint;
            DownstreamEndpoint = downstreamEndpoint;
            GridAnchor = gridAnchor;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            SpacingMillimetres = spacingMillimetres;
            DirectionResolved = directionResolved;
            Diagnostics = diagnostics == null
                ? Array.Empty<string>() : diagnostics.ToArray();
        }

        /// <summary>地面网格线（覆盖场景包络范围；空=没有线段）。</summary>
        public IReadOnlyList<QuickLineColumnGridLine> Lines { get; }
        /// <summary>
        /// 数字轴刻度：每根柱网线一个轴号（相邻差 1，跨距恒为
        /// <see cref="SpacingMillimetres"/>）；两端轴位读不出来时为空（只画网格
        /// 不画刻度，不猜）。刻度沿 <see cref="NumericAxisIsX"/> 指定的那个向分布。
        /// </summary>
        public IReadOnlyList<QuickLineColumnGridTick> NumericTicks { get; }
        /// <summary>
        /// 3.3.2：字母轴标签；两端字母轴解析不出来时为空（不猜）。沿垂直于数字
        /// 轴的那一向分布。
        /// </summary>
        public IReadOnlyList<QuickLineColumnGridLetterLabel> LetterLabels { get; }
        /// <summary>
        /// 数字轴沿场景 X（true）还是 Y（false）。
        /// </summary>
        public bool NumericAxisIsX { get; }
        /// <summary>
        /// 3.8.0.4：沿 +坐标方向轴号递增（true）还是递减。数字轴由两端轴号与
        /// 图面位移定；字母轴在两端字母相同时按约定定。
        /// </summary>
        public bool NumbersIncreaseWithAxis { get; }
        /// <summary>3.8.0.4：字母沿 +坐标方向递增（true）还是递减。</summary>
        public bool LettersIncreaseWithAxis { get; }
        /// <summary>由路线端点与轴位确认的上游端点；无法确认时为空。</summary>
        public QuickLineSpatialPoint? UpstreamEndpoint { get; }
        /// <summary>由路线端点与轴位确认的下游端点；无法确认时为空。</summary>
        public QuickLineSpatialPoint? DownstreamEndpoint { get; }
        /// <summary>柱网的稳定坐标锚点；方向未知时仅用于铺网，不代表上游。</summary>
        public QuickLineSpatialPoint GridAnchor { get; }
        /// <summary>3.3.2：场景包络（刻度/标签摆位用，控件不再自行推导）。</summary>
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double SpacingMillimetres { get; }
        public bool DirectionResolved { get; }
        public IReadOnlyList<string> Diagnostics { get; }
    }

    /// <summary>
    /// U1U 3D 预览的柱网/轴位标签纯规划器（3.3.1 特性C 引入，3.3.2 D-054
    /// Amendment 扩为两向）。
    ///
    /// 柱网（Z=0）——4800mm 跨距（<see cref="DefaultSpacingMillimetres"/>），两向
    /// 网格线覆盖场景包络；方向确认时锚在上游端点，否则只以首段起点作为网格
    /// 原点，不把它解释成上游。
    ///
    /// 轴位标签（3.8.0.4 重写；用户口径「一个轴位是 4.8 m；设备很大概率不在
    /// 轴位中心，只要两端能被正确轴位保住就行；算出来的网格轴位和设备/上游
    /// 轴位对不上要提示」）：
    /// ① **跨距恒定**：数字/字母每根柱网线一个轴号、相邻差 1——不再按两端
    ///    跨度线性插值（那会把 11 跨压进几十米的图面，跨距就不是 4.8 m 了）。
    /// ② **两端各落在自己的格子里**：把两端轴号与图面位移的偏差对半分摊
    ///    （每端最多偏半跨）——设备不在轴位中心也能被正确的两根轴夹住。
    /// ③ **轴向约定**：默认「字母沿 +X、数字沿 −Y」（等测图右上为北）。默认
    ///    约定放不下（任一端偏出半跨）但旋转约定「数字沿 +X、字母沿 −Y」放得
    ///    下时用旋转约定；两个都放不下按偏差小的选（平手取位移大的那向，
    ///    再平手取 X），网格锚在上游轴位上，并报「轴位与图面距离对不上」。
    /// ④ 数字轴的递增方向由两端轴号与图面位移定（不硬编码）；两端字母相同
    ///    时字母递增方向按约定（见 ③）。
    /// ⑤ 任一端轴位解析不出来 → 该向不标（不猜）。
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
            => Plan(segments, upstreamAxisLabel, downstreamAxisLabel,
                null, null, false, spacingMillimetres);

        /// <summary>
        /// Plans the visible grid and places axis marks only when the supplied
        /// route endpoints have already been direction-verified.
        /// </summary>
        public static QuickLineColumnGrid Plan(
            IReadOnlyList<QuickLineOrbitPreviewSegment> segments,
            string upstreamAxisLabel, string downstreamAxisLabel,
            QuickLineSpatialPoint? upstreamEndpoint,
            QuickLineSpatialPoint? downstreamEndpoint,
            bool directionResolved,
            double spacingMillimetres = DefaultSpacingMillimetres,
            IEnumerable<string> diagnostics = null)
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
            QuickLineSpatialPoint fallbackAnchor = segments
                .Where(segment => segment != null)
                .Select(segment => segment.Start).FirstOrDefault();
            bool resolved = directionResolved
                && upstreamEndpoint.HasValue && downstreamEndpoint.HasValue
                && endpoints.Contains(upstreamEndpoint.Value)
                && endpoints.Contains(downstreamEndpoint.Value)
                && !upstreamEndpoint.Value.Equals(downstreamEndpoint.Value);
            var planDiagnostics = (diagnostics ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            if (directionResolved && !resolved)
                planDiagnostics.Add("上下游端点无效或重合；方向相关轴位标记已隐藏。");

            QuickLineSpatialPoint gridAnchor;
            bool numericAxisIsX;
            bool numbersIncrease;
            bool lettersIncrease;
            var numericTicks = new List<QuickLineColumnGridTick>();
            var letterLabels = new List<QuickLineColumnGridLetterLabel>();
            AxisFit fit = null;
            if (resolved)
            {
                QuickLineAxisSpan.TryParse(upstreamAxisLabel,
                    out QuickLineAxisReference upstreamAxis);
                QuickLineAxisSpan.TryParse(downstreamAxisLabel,
                    out QuickLineAxisReference downstreamAxis);
                fit = ChooseAxisFit(upstreamEndpoint.Value,
                    downstreamEndpoint.Value, upstreamAxis, downstreamAxis,
                    spacingMillimetres);
            }
            if (fit != null)
            {
                numericAxisIsX = fit.NumbersAlongX;
                numbersIncrease = fit.NumberIncreasesWithAxis;
                lettersIncrease = fit.LetterIncreasesWithAxis;
                gridAnchor = new QuickLineSpatialPoint(
                    fit.NumbersAlongX ? fit.NumberAnchor : fit.LetterAnchor,
                    fit.NumbersAlongX ? fit.LetterAnchor : fit.NumberAnchor, 0.0);
                if (fit.NumberConstrained)
                    AddNumberTicks(numericTicks, fit, numericAxisIsX,
                        minX, minY, maxX, maxY, spacingMillimetres);
                if (fit.LetterConstrained)
                    AddLetterLabels(letterLabels, fit, numericAxisIsX,
                        minX, minY, maxX, maxY, spacingMillimetres);
                if (!fit.Fits)
                    planDiagnostics.Add(fit.DescribeMismatch(spacingMillimetres));
            }
            else
            {
                // 没有可用的轴位标签：只铺网格，不标轴号（不猜）。
                numericAxisIsX = !resolved
                    || Math.Abs(downstreamEndpoint.Value.X
                        - upstreamEndpoint.Value.X)
                        >= Math.Abs(downstreamEndpoint.Value.Y
                        - upstreamEndpoint.Value.Y);
                numbersIncrease = numericAxisIsX;
                lettersIncrease = !numericAxisIsX;
                gridAnchor = resolved ? upstreamEndpoint.Value : fallbackAnchor;
            }

            var lines = new List<QuickLineColumnGridLine>();
            foreach (double x in GridPositions(gridAnchor.X, minX, maxX,
                spacingMillimetres))
                lines.Add(new QuickLineColumnGridLine(true, x, minY, maxY));
            foreach (double y in GridPositions(gridAnchor.Y, minY, maxY,
                spacingMillimetres))
                lines.Add(new QuickLineColumnGridLine(false, y, minX, maxX));
            return new QuickLineColumnGrid(lines, numericTicks, letterLabels,
                numericAxisIsX, numbersIncrease, lettersIncrease,
                resolved ? upstreamEndpoint : null,
                resolved ? downstreamEndpoint : null, gridAnchor,
                minX, minY, maxX, maxY, spacingMillimetres, resolved,
                planDiagnostics);
        }

        /// <summary>
        /// 3.8.0.4：轴位与图面距离对不上时的一句话（给问题清单/命令行/批量
        /// 备注用）；放得下、轴位读不出来或方向未确认时返回 null。
        /// （它与网格自己的诊断同源，措辞不会两处不一致。）
        /// </summary>
        internal static string DescribeAxisMismatch(
            QuickLineSpatialPoint upstream, QuickLineSpatialPoint downstream,
            string upstreamAxisLabel, string downstreamAxisLabel,
            double spacingMillimetres = DefaultSpacingMillimetres)
        {
            ValidateSpacing(spacingMillimetres);
            QuickLineAxisSpan.TryParse(upstreamAxisLabel,
                out QuickLineAxisReference upstreamAxis);
            QuickLineAxisSpan.TryParse(downstreamAxisLabel,
                out QuickLineAxisReference downstreamAxis);
            AxisFit fit = ChooseAxisFit(upstream, downstream, upstreamAxis,
                downstreamAxis, spacingMillimetres);
            return fit != null && !fit.Fits
                ? fit.DescribeMismatch(spacingMillimetres) : null;
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

        // ---- 轴位拟合 ------------------------------------------------------------

        /// <summary>一端轴位与图面位移的拟合结果（纯数据，含报警文案）。</summary>
        private sealed class AxisFit
        {
            internal bool NumbersAlongX;
            internal bool NumberConstrained;
            internal bool LetterConstrained;
            internal bool NumberIncreasesWithAxis;
            internal bool LetterIncreasesWithAxis;
            /// <summary>上游轴号（数字）线所在的坐标。</summary>
            internal double NumberAnchor;
            /// <summary>上游字母线所在的坐标。</summary>
            internal double LetterAnchor;
            /// <summary>数字轴：图面跨度 − 应有跨度（mm，正=图面更长）。</summary>
            internal double NumberError;
            /// <summary>字母轴：同上。</summary>
            internal double LetterError;
            internal double NumberActualSpan;
            internal double NumberExpectedSpan;
            internal double LetterActualSpan;
            internal double LetterExpectedSpan;
            internal double Spacing;
            internal QuickLineAxisReference UpstreamAxis;
            internal QuickLineAxisReference DownstreamAxis;

            /// <summary>两个受约束轴都放得下：每端偏离自己的轴不超过半跨。</summary>
            internal bool Fits => Score < Spacing;

            /// <summary>受约束轴里最大的偏差（mm）；没有受约束轴时为正无穷。</summary>
            internal double Score
            {
                get
                {
                    double score = 0.0;
                    bool any = false;
                    if (NumberConstrained)
                    {
                        score = Math.Max(score, Math.Abs(NumberError));
                        any = true;
                    }
                    if (LetterConstrained)
                    {
                        score = Math.Max(score, Math.Abs(LetterError));
                        any = true;
                    }
                    return any ? score : double.PositiveInfinity;
                }
            }

            internal string DescribeMismatch(double spacing)
            {
                var parts = new List<string>();
                if (NumberConstrained && Math.Abs(NumberError) >= spacing)
                    parts.Add(DescribeAxis("数字轴", NumbersAlongX ? "X" : "Y",
                        UpstreamAxis.Number?.ToString(CultureInfo.InvariantCulture),
                        DownstreamAxis.Number?.ToString(CultureInfo.InvariantCulture),
                        NumberActualSpan, NumberExpectedSpan, spacing));
                if (LetterConstrained && Math.Abs(LetterError) >= spacing)
                    parts.Add(DescribeAxis("字母轴", NumbersAlongX ? "Y" : "X",
                        UpstreamAxis.Letter, DownstreamAxis.Letter,
                        LetterActualSpan, LetterExpectedSpan, spacing));
                return "轴位与图面距离对不上：上游 " + UpstreamAxis
                    + " ↔ 下游 " + DownstreamAxis + "——"
                    + string.Join("；", parts)
                    + "。网格已按上游轴位、每跨 " + Metrics(spacing)
                    + " m 铺开（数字轴沿 " + (NumbersAlongX ? "X" : "Y")
                    + "、字母轴沿 " + (NumbersAlongX ? "Y" : "X")
                    + "），设备/上游那两端对不上各自轴号，请核对台账轴位或路线长度";
            }

            private static string DescribeAxis(string name, string axis,
                string upstream, string downstream, double actual, double expected,
                double spacing)
            {
                bool same = string.Equals(upstream, downstream,
                    StringComparison.OrdinalIgnoreCase);
                string label = same
                    ? "两端同为 " + upstream + "（应落在同一根轴上）"
                    : upstream + "→" + downstream + " 应相差 "
                        + Bays(expected, spacing) + " 跨 × " + Metrics(spacing)
                        + " m = " + Metrics(expected) + " m";
                return name + " " + label + "，图面沿 " + axis + " 只有 "
                    + Metrics(actual) + " m（"
                    + (actual >= expected ? "多 " : "差 ")
                    + Metrics(Math.Abs(actual - expected)) + " m ≈ "
                    + Bays(Math.Abs(actual - expected), spacing) + " 跨）";
            }

            private static string Bays(double millimetres, double spacing)
                => (millimetres / spacing).ToString("0.#",
                    CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 选择轴向约定：默认（字母沿 +X、数字沿 −Y）放得下就用默认；否则试
        /// 旋转约定（数字沿 +X、字母沿 −Y）；都放不下按偏差小的选，平手取
        /// 数字轴位移较大的那向，再平手取 X。
        /// </summary>
        private static AxisFit ChooseAxisFit(QuickLineSpatialPoint upstream,
            QuickLineSpatialPoint downstream,
            QuickLineAxisReference upstreamAxis,
            QuickLineAxisReference downstreamAxis, double spacing)
        {
            if (upstreamAxis == null || downstreamAxis == null) return null;
            bool numberConstrained = upstreamAxis.HasNumber
                && downstreamAxis.HasNumber;
            bool letterConstrained = upstreamAxis.HasLetter
                && downstreamAxis.HasLetter;
            if (!numberConstrained && !letterConstrained) return null;
            AxisFit alongY = Evaluate(upstream, downstream, upstreamAxis,
                downstreamAxis, spacing, false, numberConstrained,
                letterConstrained);
            AxisFit alongX = Evaluate(upstream, downstream, upstreamAxis,
                downstreamAxis, spacing, true, numberConstrained,
                letterConstrained);
            if (alongY.Fits) return alongY;
            if (alongX.Fits) return alongX;
            if (alongX.Score < alongY.Score - 1e-6) return alongX;
            if (alongY.Score < alongX.Score - 1e-6) return alongY;
            // 平手：数字轴取位移较大的一向，再平手取 X（确定性）。
            return Math.Abs(downstream.X - upstream.X)
                >= Math.Abs(downstream.Y - upstream.Y) ? alongX : alongY;
        }

        private static AxisFit Evaluate(QuickLineSpatialPoint upstream,
            QuickLineSpatialPoint downstream,
            QuickLineAxisReference upstreamAxis,
            QuickLineAxisReference downstreamAxis, double spacing,
            bool numbersAlongX, bool numberConstrained, bool letterConstrained)
        {
            var fit = new AxisFit
            {
                NumbersAlongX = numbersAlongX,
                NumberConstrained = numberConstrained,
                LetterConstrained = letterConstrained,
                Spacing = spacing,
                UpstreamAxis = upstreamAxis,
                DownstreamAxis = downstreamAxis
            };
            double numberUp = numbersAlongX ? upstream.X : upstream.Y;
            double numberDown = numbersAlongX ? downstream.X : downstream.Y;
            double letterUp = numbersAlongX ? upstream.Y : upstream.X;
            double letterDown = numbersAlongX ? downstream.Y : downstream.X;
            if (numberConstrained)
            {
                int deltaNumber = downstreamAxis.Number.Value
                    - upstreamAxis.Number.Value;
                double delta = numberDown - numberUp;
                int sign = IncreaseSign(delta, deltaNumber,
                    numbersAlongX ? 1 : -1);
                fit.NumberIncreasesWithAxis = sign > 0;
                fit.NumberActualSpan = Math.Abs(delta);
                fit.NumberExpectedSpan = Math.Abs(deltaNumber) * spacing;
                fit.NumberError = fit.NumberActualSpan - fit.NumberExpectedSpan;
                double offset = delta - deltaNumber * spacing * sign;
                // 这一轴放得下：偏差对半分摊，两端各偏不超过半跨（设备不在
                // 轴位中心也落在自己的格子里）。放不下就锚在上游轴位，让上游
                // 至少对上自己的轴号（报警里点名另一端对不上）。
                fit.NumberAnchor = Math.Abs(offset) < spacing
                    ? numberUp + offset / 2.0 : numberUp;
            }
            else
            {
                fit.NumberIncreasesWithAxis = numbersAlongX;
                fit.NumberAnchor = numberUp;
            }
            if (letterConstrained)
            {
                long deltaLetter = downstreamAxis.LetterIndex
                    - upstreamAxis.LetterIndex;
                double delta = letterDown - letterUp;
                int sign = IncreaseSign(delta, deltaLetter,
                    numbersAlongX ? -1 : 1);
                fit.LetterIncreasesWithAxis = sign > 0;
                fit.LetterActualSpan = Math.Abs(delta);
                fit.LetterExpectedSpan = Math.Abs(deltaLetter) * spacing;
                fit.LetterError = fit.LetterActualSpan - fit.LetterExpectedSpan;
                double offset = delta - deltaLetter * spacing * sign;
                fit.LetterAnchor = Math.Abs(offset) < spacing
                    ? letterUp + offset / 2.0 : letterUp;
            }
            else
            {
                fit.LetterIncreasesWithAxis = !numbersAlongX;
                fit.LetterAnchor = letterUp;
            }
            return fit;
        }

        /// <summary>
        /// 轴号沿 +坐标的递增符号：由两端轴号差与图面位移定（同号 → +1，
        /// 异号 → −1）；位移或轴号差为 0 时用约定值（默认字母沿 +X、数字沿 −Y）。
        /// </summary>
        private static int IncreaseSign(double delta, long indexDelta,
            int fallback)
        {
            if (Math.Abs(delta) < MinimumSpanMillimetres || indexDelta == 0)
                return fallback;
            return Math.Sign(delta) == Math.Sign(indexDelta) ? 1 : -1;
        }

        private static void AddNumberTicks(List<QuickLineColumnGridTick> target,
            AxisFit fit, bool numericAxisIsX, double minX, double minY,
            double maxX, double maxY, double spacing)
        {
            double minimum = numericAxisIsX ? minX : minY;
            double maximum = numericAxisIsX ? maxX : maxY;
            int upstream = fit.UpstreamAxis.Number.Value;
            int? previous = null;
            foreach (double position in GridPositions(fit.NumberAnchor, minimum,
                maximum, spacing))
            {
                double exact = upstream + (position - fit.NumberAnchor) / spacing
                    * (fit.NumberIncreasesWithAxis ? 1.0 : -1.0);
                int number = (int)Math.Round(exact, MidpointRounding.AwayFromZero);
                if (number < 0) continue;
                if (previous.HasValue && previous.Value == number) continue;
                previous = number;
                target.Add(new QuickLineColumnGridTick(position, number));
            }
        }

        private static void AddLetterLabels(
            List<QuickLineColumnGridLetterLabel> target, AxisFit fit,
            bool numericAxisIsX, double minX, double minY, double maxX,
            double maxY, double spacing)
        {
            double minimum = numericAxisIsX ? minY : minX;
            double maximum = numericAxisIsX ? maxY : maxX;
            long upstream = fit.UpstreamAxis.LetterIndex;
            string previous = null;
            foreach (double position in GridPositions(fit.LetterAnchor, minimum,
                maximum, spacing))
            {
                double exact = upstream + (position - fit.LetterAnchor) / spacing
                    * (fit.LetterIncreasesWithAxis ? 1.0 : -1.0);
                string letter = LettersFromIndex(
                    (long)Math.Round(exact, MidpointRounding.AwayFromZero));
                if (letter == null || letter == previous) continue;
                previous = letter;
                target.Add(new QuickLineColumnGridLetterLabel(position, letter));
            }
        }

        private static string Metrics(double millimetres)
            => (millimetres / 1000.0).ToString("0.##",
                CultureInfo.InvariantCulture);

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
