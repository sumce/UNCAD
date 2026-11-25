using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UNCAD.Core.QuickLine;
using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.3.1（D-054 Amendment）预览增强、3.3.2（D-054 Amendment）两向柱网修正的
    /// 纯逻辑锁死：柱网两向规划（数字轴刻度+字母轴标签+方向判定）、桥架宽度
    /// 解析（特性D）、FromScene 对毫米标原文/通道归属/轴位原文的带入，以及
    /// 阶段类接线契约。
    ///
    /// 退役说明（3.3.2）：QuickLineOrbitLabelPolicy 标签显隐阈值测试随 Canvas
    /// 内核退役——透视 3D 下米数标是固定世界尺寸的 Billboard（面向相机恒可
    /// 读），不存在「投影过短」这个 2D 概念，无阈值可判。
    /// </summary>
    public sealed class QuickLineOrbitPreviewEnhancementTests
    {
        private static QuickLineColumnGrid PlanForKnownTestEndpoints(
            IReadOnlyList<QuickLineOrbitPreviewSegment> segments,
            string upstreamAxis, string downstreamAxis,
            double spacingMillimetres = QuickLineColumnGridPlanner
                .DefaultSpacingMillimetres)
        {
            QuickLineSpatialPoint[] points = (segments
                ?? Array.Empty<QuickLineOrbitPreviewSegment>())
                .Where(segment => segment != null)
                .SelectMany(segment => new[] { segment.Start, segment.End })
                .Distinct().ToArray();
            if (points.Length == 0)
                return QuickLineColumnGridPlanner.Plan(segments,
                    upstreamAxis, downstreamAxis, spacingMillimetres);

            QuickLineSpatialPoint upstream = points.OrderBy(point => point.X)
                .ThenBy(point => point.Y).ThenBy(point => point.Z).First();
            QuickLineSpatialPoint downstream = points
                .OrderByDescending(point => point.X)
                .ThenByDescending(point => point.Y)
                .ThenByDescending(point => point.Z).First();
            return QuickLineColumnGridPlanner.Plan(segments, upstreamAxis,
                downstreamAxis, upstream, downstream, true, spacingMillimetres);
        }

        // ---- 特性C（3.3.2 两向）：柱网 + 数字轴刻度 + 字母轴标签 ----------------

        [Fact]
        public void ColumnGrid_PlansTwoWayGridNumericTicksAndLetterLabels()
        {
            // 52800mm = 11 跨 × 4800：柱网 12 根常量 X 线 + 1 根常量 Y 线，
            // 数字刻度 48→37 每根柱网线一号（锚定上游端点：0 处恰是 48）；
            // 3.3.2：两端字母同为 V → 字母轴以 V 锚定（包络内只有一条常量 Y
            // 网格线，故只标 V）。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(52800, 0, 0),
                    QuickLineSpatialAxis.X, 52800, true)
            };
            QuickLineColumnGrid grid = PlanForKnownTestEndpoints(
                segments, "48/V", "37/V");

            Assert.NotNull(grid);
            Assert.Equal(12, grid.Lines.Count(line => line.ConstantX));
            Assert.Equal(1, grid.Lines.Count(line => !line.ConstantX));
            Assert.Equal(0.0, grid.UpstreamEndpoint.Value.X, 6);
            Assert.Equal(52800.0, grid.DownstreamEndpoint.Value.X, 6);
            Assert.Equal(4800.0, grid.SpacingMillimetres, 6);

            // 数字轴向=位移大的那向（X，|ΔX|=52800 > |ΔY|=0）。
            Assert.True(grid.NumericAxisIsX,
                "数字轴向由端点平面位移决定（场景 X 是抽象轴，不硬编码）");
            Assert.Equal(12, grid.NumericTicks.Count);
            Assert.Equal(48, grid.NumericTicks[0].Number);
            Assert.Equal(0.0, grid.NumericTicks[0].Position, 6);
            Assert.Equal(37, grid.NumericTicks[grid.NumericTicks.Count - 1].Number);
            Assert.Equal(52800.0,
                grid.NumericTicks[grid.NumericTicks.Count - 1].Position, 6);
            // 方向以端点标签为准：48 在上游端（X 小）一侧，单调递减。
            Assert.Equal(Enumerable.Range(37, 12).Reverse().ToList(),
                grid.NumericTicks.Select(tick => tick.Number).ToList());

            // 字母轴（3.3.2 新增）：两端 V/V → 锚定 V，Y 向包络只有 0 一条线。
            Assert.Single(grid.LetterLabels);
            Assert.Equal("V", grid.LetterLabels[0].Letter);
            Assert.Equal(0.0, grid.LetterLabels[0].Position, 6);

            // 反过来 37 在上游：刻度方向翻转（不按 X 正方向硬编码）。
            QuickLineColumnGrid flipped = PlanForKnownTestEndpoints(
                segments, "37/V", "48/V");
            Assert.Equal(37, flipped.NumericTicks[0].Number);
            Assert.Equal(48, flipped.NumericTicks[flipped.NumericTicks.Count - 1].Number);
        }

        [Fact]
        public void ColumnGrid_SameLetterRoute_LabelsLettersAtPitchFromTheUpstream()
        {
            // 3.8.0.4（用户口径「一个轴位是 4.8 m；两端能被正确轴位保住」）：
            // 柱网线每根一个轴号、相邻差 1，跨距恒为 4800——不再按两端跨度
            // 线性插值。这个场景（X 2 跨 + Y 2 跨，台账却差 11 个号）本来就
            // 对不上：网格锚在上游、按 4.8 m 铺，问题清单点名。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(9600, 0, 0),
                    QuickLineSpatialAxis.X, 9600, true),
                new QuickLineOrbitPreviewSegment("s2",
                    new QuickLineSpatialPoint(9600, 0, 0),
                    new QuickLineSpatialPoint(9600, 9600, 0),
                    QuickLineSpatialAxis.Y, 9600, true)
            };
            QuickLineColumnGrid grid = PlanForKnownTestEndpoints(
                segments, "48/V", "37/V");
            Assert.True(grid.NumericAxisIsX);
            // 数字沿 X：48、47、46（每根柱网线一号）。
            Assert.Equal(new[] { 48, 47, 46 },
                grid.NumericTicks.Select(tick => tick.Number).ToArray());
            // 数字轴在 X → 字母轴在 Y（旋转约定：字母沿 −Y 递增）：V、U、T。
            Assert.Equal(new[] { "V", "U", "T" },
                grid.LetterLabels.Select(label => label.Letter).ToArray());
            Assert.Equal(0.0, grid.LetterLabels
                .Single(label => label.Letter == "V").Position, 6);
            Assert.Contains(grid.Diagnostics, text => text.IndexOf(
                "轴位与图面距离对不上", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void ColumnGrid_DifferentLetters_LabelAtPitchAndAnchorAtTheUpstream()
        {
            // 3.8.0.4：字母轴同样每根柱网线一个字母、跨距 4.8 m——不再按两端
            // 字母跨度插值（那会把一个字母压进几十毫米、跨距不再是 4.8 m）。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(0, 9600, 0),
                    QuickLineSpatialAxis.Y, 9600, true)
            };
            // 位移全在 Y → 数字轴=Y、字母轴=X；两端字母 V→U 但两端 X 重合
            //（字母轴实际跨度 0，放不下一个字母的 4.8 m）：字母锚在上游 V，
            // 数字轴对不上（11 个号对 2 跨）——两轴都在问题清单里点名。
            QuickLineColumnGrid degenerate = PlanForKnownTestEndpoints(
                segments, "48/V", "37/U");
            Assert.False(degenerate.NumericAxisIsX);
            Assert.Equal("V", degenerate.LetterLabels.Single().Letter);
            Assert.Equal(0.0, degenerate.LetterLabels.Single().Position, 6);
            Assert.NotEmpty(degenerate.NumericTicks);
            Assert.Contains(degenerate.Diagnostics, text => text.IndexOf(
                "轴位与图面距离对不上", StringComparison.Ordinal) >= 0);

            // 字母位置有真实跨度：路由在 X 向 0→9600（位移平手取 X），字母轴=Y，
            // 两端 Y 错开 9600（V@0、U@4800、T@9600）。
            var diagonal = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(9600, 9600, 0),
                    QuickLineSpatialAxis.X, 13606, true)
            };
            QuickLineColumnGrid grid = PlanForKnownTestEndpoints(
                diagonal, "48/V", "37/U");
            Assert.True(grid.NumericAxisIsX);
            Assert.Equal(new[] { "V", "U", "T" },
                grid.LetterLabels.Select(label => label.Letter).ToArray());
        }

        [Fact]
        public void ColumnGrid_UnparseableOrHalfLabels_GridWithoutThatAxis()
        {
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(9600, 0, 0),
                    QuickLineSpatialAxis.X, 9600, true)
            };
            // 任一端解析失败（「轴A」）：数字刻度与字母标签都不画（不猜）。
            QuickLineColumnGrid bad = PlanForKnownTestEndpoints(
                segments, "轴A", "37/V");
            Assert.Empty(bad.NumericTicks);
            Assert.Empty(bad.LetterLabels);
            Assert.NotEmpty(bad.Lines);
            // 两端都能解析但都没有数字轴（纯字母 V→P）：数字刻度不画；字母轴
            // 沿 X（位移在 X）、锚在上游 V，按 4.8 m 每根线一个字母（V/U/T）；
            // 字母差 6 个号对不上 2 跨，问题清单点名。
            QuickLineColumnGrid letters = PlanForKnownTestEndpoints(
                segments, "V", "P");
            Assert.Empty(letters.NumericTicks);
            Assert.Equal(new[] { "V", "U", "T" },
                letters.LetterLabels.Select(label => label.Letter).ToArray());
            Assert.Contains(letters.Diagnostics, text => text.IndexOf(
                "轴位与图面距离对不上", StringComparison.Ordinal) >= 0);
            // 半边值（一端数字一端字母）：两向都不算。
            QuickLineColumnGrid mixed = PlanForKnownTestEndpoints(
                segments, "48", "V");
            Assert.Empty(mixed.NumericTicks);
            Assert.Empty(mixed.LetterLabels);
        }

        [Fact]
        public void ColumnGrid_NumericAxisFollowsLargerDisplacementNotSceneX()
        {
            // 位移全在 Y：数字轴=Y（|ΔY|>|ΔX|），刻度沿 Y 插值——修正 3.3.1
            // 沿 X 单向插值「轴位不应该这样显示」的根因。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(100, 0, 0),
                    new QuickLineSpatialPoint(100, 9600, 0),
                    QuickLineSpatialAxis.Y, 9600, true)
            };
            QuickLineColumnGrid grid = PlanForKnownTestEndpoints(
                segments, "48/V", "46/V");
            Assert.False(grid.NumericAxisIsX);
            Assert.Equal(new[] { 48, 47, 46 },
                grid.NumericTicks.Select(tick => tick.Number).ToArray());
            Assert.Equal(new[] { 0.0, 4800.0, 9600.0 },
                grid.NumericTicks.Select(tick => tick.Position).ToArray());
            // 字母轴=X 向（垂直于数字轴）：两端 X 重合、字母同为 V → 锚定 V。
            Assert.Equal("V", grid.LetterLabels.Single().Letter);
            Assert.Equal(100.0, grid.LetterLabels.Single().Position, 6);

            // 纯竖直路由（平面位移为零）：数字轴无位移可对齐，锚在上游（48 那根
            // 线恰好过上游点）；台账 11 个号的差距对不上，问题清单点名；网格照画。
            var vertical = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(100, 200, 0),
                    new QuickLineSpatialPoint(100, 200, 4000),
                    QuickLineSpatialAxis.Z, 4000, true)
            };
            QuickLineColumnGrid zOnly = PlanForKnownTestEndpoints(
                vertical, "48/V", "37/V");
            Assert.Single(zOnly.NumericTicks);
            Assert.Equal(48, zOnly.NumericTicks[0].Number);
            Assert.Equal(100.0, zOnly.NumericTicks[0].Position, 6);
            Assert.Equal("V", zOnly.LetterLabels.Single().Letter);
            Assert.NotEmpty(zOnly.Lines);
            Assert.Equal(100.0, zOnly.UpstreamEndpoint.Value.X, 6);
            Assert.Equal(100.0, zOnly.DownstreamEndpoint.Value.X, 6);
            Assert.Contains(zOnly.Diagnostics, text => text.IndexOf(
                "轴位与图面距离对不上", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void ColumnGrid_TieBreakMaxYAndTieDisplacementChoosesX()
        {
            // 3.3.2 修订锁死：纯 Y 向路由的上下游端点必须落在相反两端
            //（3.3.1 两端都取 min-Y 时叠在同一节点，位移恒为零）；位移平手
            //（|ΔX|==|ΔY|）时数字轴取 X（确定性）。
            var diagonal = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(9600, 9600, 0),
                    QuickLineSpatialAxis.X, 13576, true)
            };
            QuickLineColumnGrid grid = PlanForKnownTestEndpoints(
                diagonal, "48/V", "37/V");
            Assert.True(grid.NumericAxisIsX, "位移平手取 X（不猜另一个）");
            Assert.Equal(0.0, grid.UpstreamEndpoint.Value.Y, 6);
            // 下游端点平手取 max Y——两端钉分开。
            Assert.Equal(9600.0, grid.DownstreamEndpoint.Value.Y, 6);

            var pureY = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(100, 0, 0),
                    new QuickLineSpatialPoint(100, 9600, 0),
                    QuickLineSpatialAxis.Y, 9600, true)
            };
            QuickLineColumnGrid yGrid = PlanForKnownTestEndpoints(
                pureY, "48/V", "46/V");
            Assert.False(yGrid.NumericAxisIsX);
            Assert.Equal(0.0, yGrid.UpstreamEndpoint.Value.Y, 6);
            Assert.Equal(9600.0, yGrid.DownstreamEndpoint.Value.Y, 6);
        }

        [Fact]
        public void ColumnGrid_AnchorExtensionSkipsLettersBelowA()
        {
            // 3.8.0.4：偏差对半分摊后锚点会落到包络里；包络外那一根（索引 <1，
            // 如 A 的左邻）跳过，不猜负索引字母。上游 0 / 下游 7200、A→B
            //（应差 4800，实际多 2400 → 每端偏 1200）：A 线在 1200，
            // 包络外那根（−3600）没有字母。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(7200, 0, 0),
                    QuickLineSpatialAxis.X, 7200, true)
            };
            QuickLineColumnGrid grid = PlanForKnownTestEndpoints(
                segments, "48/A", "48/B");
            Assert.Equal(new[] { "A", "B", "C" },
                grid.LetterLabels.Select(label => label.Letter).ToArray());
            Assert.Equal(1200.0, grid.LetterLabels[0].Position, 6);
            Assert.Equal(new[] { 1200.0, 6000.0, 10800.0 },
                grid.LetterLabels.Select(label => label.Position).ToArray());
            Assert.Empty(grid.Diagnostics);
        }

        [Fact]
        public void ColumnGrid_PlanRejectsInvalidSpacing()
        {
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(4800, 0, 0),
                    QuickLineSpatialAxis.X, 4800, true)
            };
            Assert.Throws<ArgumentOutOfRangeException>(
                () => QuickLineColumnGridPlanner.Plan(segments, "48/V", "37/V", 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => QuickLineColumnGridPlanner.Plan(
                    segments, "48/V", "37/V", double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => QuickLineColumnGridPlanner.Plan(
                    segments, "48/V", "37/V", double.PositiveInfinity));
            // segments 本身为 null 不抛（返回 null=无场景）。
            Assert.Null(QuickLineColumnGridPlanner.Plan(
                null, "48/V", "37/V"));
        }

        [Fact]
        public void ColumnGrid_NumbersStayOneBayApartAndMismatchIsReported()
        {
            // 3.8.0.4：跨距恒为 4.8 m——柱网线每根一个轴号、相邻差 1。图面比
            // 台账短（9.5 m 对 4 跨 19.2 m）时不再把 4 个号压进 9.5 m、也不再
            // 跳号；网格锚在上游、按 4.8 m 铺，问题清单点名。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(9500, 0, 0),
                    QuickLineSpatialAxis.X, 9500, true)
            };
            QuickLineColumnGrid grid = PlanForKnownTestEndpoints(
                segments, "48/V", "44/V");
            Assert.True(grid.NumericAxisIsX);
            Assert.Equal(new[] { 48, 47, 46 },
                grid.NumericTicks.Select(tick => tick.Number).ToArray());
            Assert.Equal(new[] { 0.0, 4800.0, 9600.0 },
                grid.NumericTicks.Select(tick => tick.Position).ToArray());
            Assert.Contains(grid.Diagnostics, text =>
                text.IndexOf("轴位与图面距离对不上", StringComparison.Ordinal) >= 0
                && text.IndexOf("48→44", StringComparison.Ordinal) >= 0
                && text.IndexOf("19.2 m", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void ColumnGrid_DeviceOffAxisCentre_BothEndsStayInsideTheirBays()
        {
            // 用户口径（3.8.0.4）：设备很大概率不在轴位中心——只要两端能被正确
            // 轴位保住就行。上游 48/V、设备 37/V，图面南北 54000（11 跨 + 1200）、
            // 东西 300：偏差对半分摊后两端各偏 600 / 150，各自落在自己的格子里；
            // 跨距仍是 4800，不报警。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(300, 54000, 0),
                    QuickLineSpatialAxis.Y, 54001, true)
            };
            QuickLineColumnGrid grid = PlanForKnownTestEndpoints(
                segments, "48/V", "37/V");
            Assert.False(grid.NumericAxisIsX);
            Assert.Empty(grid.Diagnostics);
            QuickLineColumnGridTick up = grid.NumericTicks
                .Single(tick => tick.Number == 48);
            QuickLineColumnGridTick down = grid.NumericTicks
                .Single(tick => tick.Number == 37);
            Assert.Equal(11 * 4800.0, down.Position - up.Position, 6);
            // 两端离各自的轴都不超过半跨（2400）——被正确轴位保住。
            Assert.InRange(Math.Abs(grid.UpstreamEndpoint.Value.Y - up.Position),
                0.0, 2400.0);
            Assert.InRange(Math.Abs(grid.DownstreamEndpoint.Value.Y - down.Position),
                0.0, 2400.0);
            Assert.Equal("V", grid.LetterLabels
                .Single(label => label.Letter == "V").Letter);
        }

        [Fact]
        public void ColumnGrid_RotatedConventionWhenTheDefaultCannotHoldBothEnds()
        {
            // 默认约定（字母沿 X、数字沿 −Y）放不下、旋转约定（数字沿 X）放得
            // 下时用旋转约定：路由沿 X 11 跨、台账 48/V→37/V。两端各在自己的
            // 格子里、跨距 4800、不报警。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(52800, 0, 0),
                    QuickLineSpatialAxis.X, 52800, true)
            };
            QuickLineColumnGrid grid = PlanForKnownTestEndpoints(
                segments, "48/V", "37/V");
            Assert.True(grid.NumericAxisIsX);
            Assert.Empty(grid.Diagnostics);
            Assert.Equal(48, grid.NumericTicks[0].Number);
            Assert.Equal(0.0, grid.NumericTicks[0].Position, 6);
            Assert.Equal(37, grid.NumericTicks[grid.NumericTicks.Count - 1].Number);
            Assert.Equal(52800.0,
                grid.NumericTicks[grid.NumericTicks.Count - 1].Position, 6);
            // 数字沿 +X 递减 → 字母轴=Y、沿 −Y 递增：V 在上游 Y=0。
            Assert.True(grid.NumbersIncreaseWithAxis == false);
            Assert.Equal("V", grid.LetterLabels.Single().Letter);
        }

        [Fact]
        public void AxisMismatch_IsNamedWithExpectedAndActualDistances()
        {
            // 3.8.0.4：轴位与图面对不上时要能一句话说清（问题清单/命令行/批量
            // 备注同源）：谁到谁、差几跨、应多长、实际多长、往哪个方向。
            var upstream = new QuickLineSpatialPoint(0, 0, 0);
            var downstream = new QuickLineSpatialPoint(0, 40000, 0);
            string message = QuickLineColumnGridPlanner.DescribeAxisMismatch(
                upstream, downstream, "48/V", "37/V");
            Assert.NotNull(message);
            Assert.Contains("上游 48/V ↔ 下游 37/V", message, StringComparison.Ordinal);
            Assert.Contains("11 跨", message, StringComparison.Ordinal);
            Assert.Contains("52.8 m", message, StringComparison.Ordinal);
            Assert.Contains("40 m", message, StringComparison.Ordinal);
            Assert.Contains("12.8 m", message, StringComparison.Ordinal);
            Assert.Contains("每跨 4.8 m", message, StringComparison.Ordinal);
            // 放得下时不报；轴位读不出来、方向未确认也不报。
            Assert.Null(QuickLineColumnGridPlanner.DescribeAxisMismatch(
                upstream, new QuickLineSpatialPoint(0, 54000, 0), "48/V", "37/V"));
            Assert.Null(QuickLineColumnGridPlanner.DescribeAxisMismatch(
                upstream, downstream, "轴A", "37/V"));
            Assert.Null(QuickLineColumnGridPlanner.DescribeAxisMismatch(
                upstream, downstream, null, null));
        }

        [Fact]
        public void ColumnGrid_LetterIndexRoundTripsAndRejectsInvalid()
        {
            // 字母索引双射：1=A、26=Z、27=AA（多字母台账值可推邻居）。
            Assert.Equal("A", QuickLineColumnGridPlanner.LettersFromIndex(1));
            Assert.Equal("Z", QuickLineColumnGridPlanner.LettersFromIndex(26));
            Assert.Equal("AA", QuickLineColumnGridPlanner.LettersFromIndex(27));
            Assert.Null(QuickLineColumnGridPlanner.LettersFromIndex(0));
            Assert.Null(QuickLineColumnGridPlanner.LettersFromIndex(-3));

            // 多字母值（ABB=730 … ABE=733）：柱网线每根一个字母，按 4.8 m 递推。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(14400, 0, 0),
                    QuickLineSpatialAxis.X, 14400, true)
            };
            QuickLineColumnGrid grid = PlanForKnownTestEndpoints(
                segments, "48/ABB", "48/ABE");
            Assert.Equal(new[] { "ABB", "ABC", "ABD", "ABE" },
                grid.LetterLabels.Select(label => label.Letter).ToArray());
        }

        [Fact]
        public void ColumnGrid_GridPositions_CoversEnvelopeAndCapsCount()
        {
            // 覆盖包络：首线在 minimum 或其下、末线在 maximum 或其上
            //（5000 的包络末线越到 9600——柱网天然外扩一格）。
            IReadOnlyList<double> positions = QuickLineColumnGridPlanner
                .GridPositions(0.0, 100.0, 5000.0, 4800.0);
            Assert.Equal(new[] { 0.0, 4800.0, 9600.0 }, positions);
            // 锚点不必是 0：从 700 起锚，覆盖 [100, 5000]（−4100、700、5500）。
            IReadOnlyList<double> anchored = QuickLineColumnGridPlanner
                .GridPositions(700.0, 100.0, 5000.0, 4800.0);
            Assert.Equal(-4100.0, anchored[0], 6);
            Assert.Equal(700.0, anchored[1], 6);
            Assert.Equal(5500.0, anchored[anchored.Count - 1], 6);
            // 病态跨度截断到 200 根（确定性，不炸渲染）。
            IReadOnlyList<double> capped = QuickLineColumnGridPlanner
                .GridPositions(0.0, 0.0, 4800.0 * 300.0, 4800.0);
            Assert.Equal(200, capped.Count);
            // 非法柱距拒绝。
            Assert.Throws<ArgumentOutOfRangeException>(
                () => QuickLineColumnGridPlanner.GridPositions(0, 0, 100, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => QuickLineColumnGridPlanner.GridPositions(
                    0, 0, 100, double.NaN));
            // 空场景/空段返回 null。
            Assert.Null(QuickLineColumnGridPlanner.Plan(
                new List<QuickLineOrbitPreviewSegment>(), "48/V", "37/V"));
        }

        // ---- 特性D：桥架宽度解析 ----------------------------------------------

        [Fact]
        public void TrayWidth_ParsesMillimetreAndLegacyForms()
        {
            Assert.True(BridgeLabelFormatter.TryParseTrayWidthMillimetres(
                "桥架200*100 2500mm", out double width));
            Assert.Equal(200.0, width, 6);
            Assert.True(BridgeLabelFormatter.TryParseTrayWidthMillimetres(
                "桥架400*100 12000mm", out double wide));
            Assert.Equal(400.0, wide, 6);
            Assert.True(BridgeLabelFormatter.TryParseTrayWidthMillimetres(
                " 桥架100*50 10格", out double legacy));
            Assert.Equal(100.0, legacy, 6);
            // 不是桥架规格文本（线管/垃圾/空）→ false，调用方用 200 缺省不猜。
            Assert.False(BridgeLabelFormatter.TryParseTrayWidthMillimetres(
                "⌀20线管 2000mm", out _));
            Assert.False(BridgeLabelFormatter.TryParseTrayWidthMillimetres(
                "随便", out _));
            Assert.False(BridgeLabelFormatter.TryParseTrayWidthMillimetres(
                "", out _));
            Assert.False(BridgeLabelFormatter.TryParseTrayWidthMillimetres(
                null, out _));
        }

        // ---- 模型带入（特性B/C/D）---------------------------------------------

        [Fact]
        public void FromScene_CarriesLabelsChannelsAndAxisLabels()
        {
            QuickLineIsometricScene scene = BuildTwoSegmentScene();
            var labelTexts = new Dictionary<string, string>
            {
                ["h"] = "1000mm"
            };
            var channels = new Dictionary<string, QuickLineOrbitPreviewChannel>
            {
                ["h"] = new QuickLineOrbitPreviewChannel(400.0, false, false),
                ["v"] = new QuickLineOrbitPreviewChannel(null, false, true)
            };
            QuickLineOrbitPreviewModel model =
                QuickLineOrbitPreviewModel.FromScene(scene, "摘要",
                    "48/V", "37/V", labelTexts, channels);

            Assert.NotNull(model);
            Assert.Equal("48/V", model.UpstreamAxisLabel);
            Assert.Equal("37/V", model.DownstreamAxisLabel);
            QuickLineOrbitPreviewSegment horizontal = model.Segments.Single(
                item => item.Axis == QuickLineSpatialAxis.X);
            QuickLineOrbitPreviewSegment vertical = model.Segments.Single(
                item => item.Axis == QuickLineSpatialAxis.Z);
            Assert.Equal("1000mm", horizontal.LabelText);
            Assert.NotNull(horizontal.Channel);
            Assert.Equal(400.0, horizontal.Channel.TrayWidthMillimetres.Value, 6);
            Assert.False(horizontal.Channel.TrayWidthAssumedDefault);
            Assert.Equal(string.Empty, vertical.LabelText);
            Assert.NotNull(vertical.Channel);
            Assert.Null(vertical.Channel.TrayWidthMillimetres);
            Assert.True(vertical.Channel.HasConduit);
        }

        [Fact]
        public void FromScene_WithoutOptionalData_KeepsLegacyShape()
        {
            QuickLineIsometricScene scene = BuildTwoSegmentScene();
            QuickLineOrbitPreviewModel model =
                QuickLineOrbitPreviewModel.FromScene(scene, "摘要");
            Assert.Equal(2, model.Segments.Count);
            Assert.Equal(string.Empty, model.UpstreamAxisLabel);
            Assert.Equal(string.Empty, model.DownstreamAxisLabel);
            foreach (QuickLineOrbitPreviewSegment segment in model.Segments)
            {
                Assert.Equal(string.Empty, segment.LabelText);
                Assert.Null(segment.Channel);
            }
        }

        // ---- 阶段类接线契约（BuildPreview 带入，源码契约与既有风格一致） -------

        [Fact]
        public void Stage_WiresPreviewEnhancementsIntoBuildPreview()
        {
            string stage = Source("src", "UNCAD", "Features", "Fill",
                "FillRouteInspectionStage.cs");
            // 特性B：毫米标原文逐段带入。
            Assert.Contains("labelTexts", stage);
            // 特性C：上下游轴位原文带入 FromScene。
            Assert.Contains("upstreamAxis, downstreamAxis", stage);
            // 特性D：通道归属构建 + 宽度解析 + 200 缺省。
            Assert.Contains("BuildChannels", stage);
            Assert.Contains("TryParseTrayWidthMillimetres", stage);
            Assert.Contains("DefaultTrayWidthMillimetres = 200.0", stage);
            Assert.Contains("QuickLineOrbitPreviewChannel", stage);
            // 归属只认 KIND=U1Q/U1C（与图层矫正同口径，其它不猜）。
            Assert.Contains("\"U1Q\"", stage);
            Assert.Contains("\"U1C\"", stage);
        }

        private static QuickLineIsometricScene BuildTwoSegmentScene()
        {
            var graph = QuickLineGraph.Build(new List<QuickLineSegment>
            {
                new QuickLineSegment("h", new QuickLinePoint(0, 0),
                    new QuickLinePoint(1000, 0)),
                new QuickLineSegment("v", new QuickLinePoint(1000, 0),
                    new QuickLinePoint(1000, -4000))
            }, 1.0);
            var distances = new Dictionary<string, double>
            {
                ["h"] = 1000.0,
                ["v"] = 4000.0
            };
            return QuickLineIsometricSceneBuilder.BuildAll(graph, distances,
                distances, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static string Source(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return File.ReadAllText(Path.Combine(root, Path.Combine(parts)));
        }
    }
}
