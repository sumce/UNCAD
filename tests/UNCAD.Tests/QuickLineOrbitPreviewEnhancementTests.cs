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
            QuickLineColumnGrid grid = QuickLineColumnGridPlanner.Plan(
                segments, "48/V", "37/V");

            Assert.NotNull(grid);
            Assert.Equal(12, grid.Lines.Count(line => line.ConstantX));
            Assert.Equal(1, grid.Lines.Count(line => !line.ConstantX));
            Assert.Equal(0.0, grid.UpstreamEndpoint.X, 6);
            Assert.Equal(52800.0, grid.DownstreamEndpoint.X, 6);
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
            QuickLineColumnGrid flipped = QuickLineColumnGridPlanner.Plan(
                segments, "37/V", "48/V");
            Assert.Equal(37, flipped.NumericTicks[0].Number);
            Assert.Equal(48, flipped.NumericTicks[flipped.NumericTicks.Count - 1].Number);
        }

        [Fact]
        public void ColumnGrid_SameLetterRoute_ExtendsLetterAxisAcrossEnvelope()
        {
            // 用户 3.3.1 现场反馈「轴位不应该这样显示」的修正场景：路由在 Y 向
            // 跨 2 跨（0→9600）、两端字母同为 V → 字母轴按 4800 柱距沿坐标正向
            // 递推邻居 V/W/X（任务书口径「字母轴 U/V/W…」：锚定字母来自台账
            // 解析，邻居按柱距等差递推；台账没有信息能定字母递增的方位，约定
            // +坐标=A→Z 递增并文档化）。
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
            QuickLineColumnGrid grid = QuickLineColumnGridPlanner.Plan(
                segments, "48/V", "37/V");
            Assert.True(grid.NumericAxisIsX);
            // 字母轴锚在上游 Y=0（V）：包络 [0,9600] → 0(V)、4800(W)、9600(X)。
            Assert.Equal(new[] { "V", "W", "X" },
                grid.LetterLabels.Select(label => label.Letter).ToArray());
            Assert.Equal(0.0, grid.LetterLabels
                .Single(label => label.Letter == "V").Position, 6);
        }

        [Fact]
        public void ColumnGrid_DifferentLetters_InterpolateClampedBetweenEndpoints()
        {
            // 两端字母不同（V→U）：按字母轴跨度线性插值，夹在两端之间，
            // 与数字轴同口径（不做端点外推）。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(0, 9600, 0),
                    QuickLineSpatialAxis.Y, 9600, true)
            };
            // 位移全在 Y → 数字轴=Y、字母轴=X；两端字母 V/U 但两端 X 重合
            //（场景上无法定序）→ 字母不标（不猜）。
            QuickLineColumnGrid degenerate = QuickLineColumnGridPlanner.Plan(
                segments, "48/V", "37/U");
            Assert.False(degenerate.NumericAxisIsX);
            Assert.Empty(degenerate.LetterLabels);
            Assert.NotEmpty(degenerate.NumericTicks);

            // 字母位置有真实跨度：路由在 X 向 0→9600（数字轴=X），字母轴=Y，
            // 两端 Y 错开 9600（V@0、U@9600）→ 插值出 V/U 夹在中间。
            var diagonal = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(9600, 9600, 0),
                    QuickLineSpatialAxis.X, 13606, true)
            };
            QuickLineColumnGrid grid = QuickLineColumnGridPlanner.Plan(
                diagonal, "48/V", "37/U");
            Assert.True(grid.NumericAxisIsX);
            Assert.Equal(new[] { "V", "U" },
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
            QuickLineColumnGrid bad = QuickLineColumnGridPlanner.Plan(
                segments, "轴A", "37/V");
            Assert.Empty(bad.NumericTicks);
            Assert.Empty(bad.LetterLabels);
            Assert.NotEmpty(bad.Lines);
            // 两端都能解析但都没有数字轴（纯字母 V→P）：数字刻度不画；字母
            // 不同但两端字母轴坐标重合（路由纯 X 向、字母轴=X 两端同为 0）
            //——场景上无法定序，不猜，字母也不标。
            QuickLineColumnGrid letters = QuickLineColumnGridPlanner.Plan(
                segments, "V", "P");
            Assert.Empty(letters.NumericTicks);
            Assert.Empty(letters.LetterLabels);
            // 半边值（一端数字一端字母）：两向都不算。
            QuickLineColumnGrid mixed = QuickLineColumnGridPlanner.Plan(
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
            QuickLineColumnGrid grid = QuickLineColumnGridPlanner.Plan(
                segments, "48/V", "46/V");
            Assert.False(grid.NumericAxisIsX);
            Assert.Equal(new[] { 48, 47, 46 },
                grid.NumericTicks.Select(tick => tick.Number).ToArray());
            Assert.Equal(new[] { 0.0, 4800.0, 9600.0 },
                grid.NumericTicks.Select(tick => tick.Position).ToArray());
            // 字母轴=X 向（垂直于数字轴）：两端 X 重合、字母同为 V → 锚定 V。
            Assert.Equal("V", grid.LetterLabels.Single().Letter);
            Assert.Equal(100.0, grid.LetterLabels.Single().Position, 6);

            // 纯竖直路由（平面位移为零）：数字轴无跨度不标刻度；字母轴同字母
            // 锚定仍可标（网格只有一条 X 线，恰是 V）；网格照画。
            var vertical = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(100, 200, 0),
                    new QuickLineSpatialPoint(100, 200, 4000),
                    QuickLineSpatialAxis.Z, 4000, true)
            };
            QuickLineColumnGrid zOnly = QuickLineColumnGridPlanner.Plan(
                vertical, "48/V", "37/V");
            Assert.Empty(zOnly.NumericTicks);
            Assert.Equal("V", zOnly.LetterLabels.Single().Letter);
            Assert.NotEmpty(zOnly.Lines);
            Assert.Equal(100.0, zOnly.UpstreamEndpoint.X, 6);
            Assert.Equal(100.0, zOnly.DownstreamEndpoint.X, 6);
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
            QuickLineColumnGrid grid = QuickLineColumnGridPlanner.Plan(
                diagonal, "48/V", "37/V");
            Assert.True(grid.NumericAxisIsX, "位移平手取 X（不猜另一个）");
            Assert.Equal(0.0, grid.UpstreamEndpoint.Y, 6);
            // 下游端点平手取 max Y——两端钉分开。
            Assert.Equal(9600.0, grid.DownstreamEndpoint.Y, 6);

            var pureY = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(100, 0, 0),
                    new QuickLineSpatialPoint(100, 9600, 0),
                    QuickLineSpatialAxis.Y, 9600, true)
            };
            QuickLineColumnGrid yGrid = QuickLineColumnGridPlanner.Plan(
                pureY, "48/V", "46/V");
            Assert.False(yGrid.NumericAxisIsX);
            Assert.Equal(0.0, yGrid.UpstreamEndpoint.Y, 6);
            Assert.Equal(9600.0, yGrid.DownstreamEndpoint.Y, 6);
        }

        [Fact]
        public void ColumnGrid_AnchorExtensionSkipsLettersBelowA()
        {
            // 锚定递推越界（索引<1，如 A 的左邻）跳过，不猜负索引字母。
            IReadOnlyList<QuickLineColumnGridLetterLabel> labels =
                QuickLineColumnGridPlanner.InterpolateLetterLabels(
                    0.0, 1, 0.0, 1, 0.0, -4800.0, 4800.0, 4800.0);
            Assert.Equal(new[] { "A", "B" },
                labels.Select(label => label.Letter).ToArray());
            Assert.Equal(0.0, labels[0].Position, 6);
            Assert.Equal(4800.0, labels[1].Position, 6);
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
        public void ColumnGrid_RoundsSkipsOutOfRangeAndDuplicateNumbers()
        {
            // 刻度间距小于一号轴（span 9500、4 号差、柱距 1000）：相邻柱网线
            // 四舍五入到同一号→去重；端点外推值越界→跳过。
            IReadOnlyList<QuickLineColumnGridTick> ticks =
                QuickLineColumnGridPlanner.InterpolateTicks(
                    0.0, 48, 9500.0, 44, 0.0, 0.0, 9500.0, 1000.0);
            Assert.Equal(new[]
            {
                Tuple.Create(0.0, 48),
                Tuple.Create(2000.0, 47),
                Tuple.Create(4000.0, 46),
                Tuple.Create(6000.0, 45),
                Tuple.Create(9000.0, 44)
            }, ticks.Select(tick => Tuple.Create(tick.Position, tick.Number)).ToArray());

            // 超出两端轴号范围的取整值跳过：span 4000/Δ11，4800 处外推 35
            // 越过下游 37 → 只剩上游端 48。
            IReadOnlyList<QuickLineColumnGridTick> clipped =
                QuickLineColumnGridPlanner.InterpolateTicks(
                    0.0, 48, 4000.0, 37, 0.0, 0.0, 4000.0, 4800.0);
            Assert.Single(clipped);
            Assert.Equal(48, clipped[0].Number);
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

            // 多字母值（ABC=731）：同字母锚定递推，邻居 ABB/ABD/ABE 照推。
            IReadOnlyList<QuickLineColumnGridLetterLabel> labels =
                QuickLineColumnGridPlanner.InterpolateLetterLabels(
                    0.0, 731, 0.0, 731, 0.0, -4800.0, 9600.0, 4800.0);
            Assert.Equal(new[] { "ABB", "ABC", "ABD", "ABE" },
                labels.Select(label => label.Letter).ToArray());
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
