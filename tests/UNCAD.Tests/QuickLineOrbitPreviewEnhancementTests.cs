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
    /// 3.3.1（D-054 Amendment）预览增强的纯逻辑锁死：标签显隐阈值（特性B）、
    /// 柱网/数字轴插值刻度规划（特性C）、桥架宽度解析（特性D）、FromScene 对
    /// 毫米标原文/通道归属/轴位原文的带入，以及阶段类接线契约。
    /// </summary>
    public sealed class QuickLineOrbitPreviewEnhancementTests
    {
        // ---- 特性B：标签显隐判定 ----------------------------------------------

        [Fact]
        public void LabelPolicy_HidesShortSpansAndNonFiniteValues()
        {
            Assert.True(QuickLineOrbitLabelPolicy.ShouldDrawLabel(24.0));
            Assert.True(QuickLineOrbitLabelPolicy.ShouldDrawLabel(240.0));
            Assert.False(QuickLineOrbitLabelPolicy.ShouldDrawLabel(23.9),
                "投影长度低于 24px 的段标签隐藏（防糊成一片）");
            Assert.False(QuickLineOrbitLabelPolicy.ShouldDrawLabel(0.0));
            Assert.False(QuickLineOrbitLabelPolicy.ShouldDrawLabel(
                double.PositiveInfinity));
            // 阈值可调（控件以后想收紧/放宽不必改判定函数）。
            Assert.True(QuickLineOrbitLabelPolicy.ShouldDrawLabel(10.0, 10.0));
            Assert.False(QuickLineOrbitLabelPolicy.ShouldDrawLabel(9.9, 10.0));
        }

        // ---- 特性C：柱网 + 数字轴插值刻度 --------------------------------------

        [Fact]
        public void ColumnGrid_PlansLinesAndInterpolatedTicks_48DownTo37()
        {
            // 52800mm = 11 跨 × 4800：柱网 12 根常量 X 线 + 1 根常量 Y 线，
            // 刻度 48→37 每根柱网线一号；锚定上游端点（0 处恰是 48）。
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

            Assert.Equal(12, grid.Ticks.Count);
            Assert.Equal(48, grid.Ticks[0].Number);
            Assert.Equal(0.0, grid.Ticks[0].X, 6);
            Assert.Equal(37, grid.Ticks[grid.Ticks.Count - 1].Number);
            Assert.Equal(52800.0, grid.Ticks[grid.Ticks.Count - 1].X, 6);
            // 方向以端点标签为准：48 在上游端（X 小）一侧，单调递减。
            Assert.Equal(Enumerable.Range(37, 12).Reverse().ToList(),
                grid.Ticks.Select(tick => tick.Number).ToList());

            // 反过来 37 在上游：刻度方向翻转（不按 X 正方向硬编码）。
            QuickLineColumnGrid flipped = QuickLineColumnGridPlanner.Plan(
                segments, "37/V", "48/V");
            Assert.Equal(37, flipped.Ticks[0].Number);
            Assert.Equal(48, flipped.Ticks[flipped.Ticks.Count - 1].Number);
        }

        [Fact]
        public void ColumnGrid_UnparseableOrLetterOnlyLabels_GridWithoutTicks()
        {
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(9600, 0, 0),
                    QuickLineSpatialAxis.X, 9600, true)
            };
            // 任一端解析失败（「轴A」）：只画网格不画刻度（不猜）。
            QuickLineColumnGrid bad = QuickLineColumnGridPlanner.Plan(
                segments, "轴A", "37/V");
            Assert.Empty(bad.Ticks);
            Assert.NotEmpty(bad.Lines);
            // 两端都能解析但都没有数字轴（纯字母）：数字刻度同样不画。
            QuickLineColumnGrid letters = QuickLineColumnGridPlanner.Plan(
                segments, "V", "P");
            Assert.Empty(letters.Ticks);
            Assert.NotEmpty(letters.Lines);
            // 半边值（一端数字一端字母）不算。
            QuickLineColumnGrid mixed = QuickLineColumnGridPlanner.Plan(
                segments, "48", "V");
            Assert.Empty(mixed.Ticks);
        }

        [Fact]
        public void ColumnGrid_DegenerateSpan_HasGridButNoTicks()
        {
            // 纯 Y/Z 向路由：两端 X 重合（上游=下游端点），插值无跨度→无刻度。
            var segments = new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(100, 0, 0),
                    new QuickLineSpatialPoint(100, 4000, 0),
                    QuickLineSpatialAxis.Y, 4000, true)
            };
            QuickLineColumnGrid grid = QuickLineColumnGridPlanner.Plan(
                segments, "48/V", "37/V");
            Assert.Empty(grid.Ticks);
            Assert.NotEmpty(grid.Lines);
            Assert.Equal(100.0, grid.UpstreamEndpoint.X, 6);
            Assert.Equal(100.0, grid.DownstreamEndpoint.X, 6);
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
            }, ticks.Select(tick => Tuple.Create(tick.X, tick.Number)).ToArray());

            // 超出两端轴号范围的取整值跳过：span 4000/Δ11，4800 处外推 35
            // 越过下游 37 → 只剩上游端 48。
            IReadOnlyList<QuickLineColumnGridTick> clipped =
                QuickLineColumnGridPlanner.InterpolateTicks(
                    0.0, 48, 4000.0, 37, 0.0, 0.0, 4000.0, 4800.0);
            Assert.Single(clipped);
            Assert.Equal(48, clipped[0].Number);
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
