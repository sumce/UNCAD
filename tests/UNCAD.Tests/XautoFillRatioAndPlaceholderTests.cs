using System;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.2.0.11（D-053 修订）现场回归锁：用户报告
    /// "Xauto 有问题 拉的太长了，纵向的线 就2M 一下弄特别长"——
    /// 横幅图框（可用区约 75000×7500，宽高比 10:1）里二分拟合的 k 完全由
    /// 宽度决定，2m 竖管按 2m×k 画出来占半框高度，视觉尺寸失控；且未确认
    /// 占位标（尾 00mm）的显示距离=当前画长，k&gt;1 拉长后下一次 Xauto 把
    /// 拉长后的长度当基线——越跑越长回不来（棘轮效应）。
    /// ① 目标模式按占用率缩放：最终 scale = k_fit × fillRatio（默认 0.65，
    ///    UNC_XAUTO_FILL_RATIO 可调，合法域 [0.2, 1.0]，1.0 恢复旧"铺满"）；
    /// ② 占位标只跟缩不跟涨：k&gt;1 时最终画长 ≤ max(原画长, 可读下限)，
    ///    已确认真实毫米的段行为完全不变。
    /// </summary>
    public sealed class XautoFillRatioAndPlaceholderTests
    {
        private const double Cos30 = 0.86602540378443864676;
        private const double Sin30 = 0.5;

        [Fact]
        public void FillRatio_BelowOne_ShrinksUnionEnvelope()
        {
            // 宽度主导场景：30000mm 的 X 向单段投影 25980.76×15000，目标
            // 30000×75000 → k_fit = min(30000/25980.76, 5) = 1.1547（由宽度
            // 决定）。fillRatio=0.65 → 最终包络宽 = 0.65×30000 = 19500，
            // 比 fillRatio=1.0 的 30000 明显小；包络仍落在目标内、仍居中。
            var plan = PlanSingle30000(1.0);
            double fullWidth = Width(plan);
            var shrunk = PlanSingle30000(0.65);
            double shrunkWidth = Width(shrunk);

            Assert.True(shrunkWidth <= 0.65 * 30000.0 + 1.0,
                "占用率 0.65 的联合包络宽不得超过 fillRatio×目标宽（实测 "
                + shrunkWidth.ToString("0") + " > 19500）。");
            Assert.True(shrunkWidth < 0.9 * fullWidth,
                "fillRatio=0.65 必须比 fillRatio=1.0 明显小（实测 "
                + shrunkWidth.ToString("0") + " vs " + fullWidth.ToString("0") + "）。");
            Assert.True(Width(shrunk) <= 30000.0 + 1e-3
                && Height(shrunk) <= 75000.0 + 1e-3,
                "缩小后的包络仍必须落在目标矩形内。");
            Assert.Equal(0.0, (MinX(shrunk) + MaxX(shrunk)) / 2.0, 1);
            Assert.Equal(0.0, (MinY(shrunk) + MaxY(shrunk)) / 2.0, 1);
        }

        [Fact]
        public void FillRatio_InvalidValues_ThrowArgumentOutOfRange()
        {
            // 合法域 [0.2, 1.0]：NaN/∞/过小/过大一律拒绝——太小草图挤成
            // 一团没法读，大于 1 违背"最终几何落在目标矩形内"的承诺。
            foreach (double bad in new[] { double.NaN,
                    double.PositiveInfinity, 0.1, 1.01, -0.5 })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    PlanSingle30000(bad));
            }
            // 边界值本身合法（0.2 与 1.0 都不抛）。
            PlanSingle30000(0.2);
            PlanSingle30000(1.0);
        }

        [Fact]
        public void FillRatio_One_MatchesLegacyFillBehaviour()
        {
            // 旧迁移锚点：fillRatio=1.0 与 3.2.0.1"铺满"行为逐值一致——
            // 3000(X)+1000(Z) 在 75000×7500、中心 (40000,2000) 的目标里
            // k_fit=3：A 最终 9000、B 最终 3000、联合包络居中于目标中心。
            var aEnd = new QuickLinePoint(1000 * Cos30, 1000 * Sin30);
            var target = new SketchProportionTarget(40000.0, 2000.0,
                75000.0, 7500.0);
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0), aEnd, 3000),
                Input("B", aEnd, new QuickLinePoint(aEnd.X, aEnd.Y + 1000), 1000)
            }, 450.0, 1.0, target, 1.0);

            Assert.Equal(2, plan.RelaidSegmentCount);
            Assert.Equal(9000.0, Length(Move(plan, "A")), 3);
            Assert.Equal(3000.0, Length(Move(plan, "B")), 3);
            Assert.Equal(40000.0, (MinX(plan) + MaxX(plan)) / 2.0, 2);
            Assert.Equal(2000.0, (MinY(plan) + MaxY(plan)) / 2.0, 2);
        }

        [Fact]
        public void PlaceholderSegment_DoesNotGrowWhenScaleAboveOne()
        {
            // 小跨度草图 + 大图框逼出 k>1：A=1000mm（真实）+ B=未确认
            // 2000mm 占位（当前画长 1000）。目标 75000×75000 →
            // k_fit = min(75000/866, 75000/1500) = 50，默认 fillRatio 0.65
            // → k = 32.5。旧版 B 最终 = 1000×32.5 = 32500（占半框高、下次
            // 还会被当新基线）；现在 B 最终画长 ≤ max(原画长 1000, 下限 450)，
            // 真实段 A 照常按毫米比例放大到 32500（行为不变）。
            var aEnd = new QuickLinePoint(1000 * Cos30, 1000 * Sin30);
            var target = new SketchProportionTarget(0.0, 0.0, 75000.0, 75000.0);
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0), aEnd, 1000),
                new SketchProportionInput("B", aEnd,
                    new QuickLinePoint(aEnd.X, aEnd.Y + 1000), 2000,
                    completed: false, placeholderLabel: true)
            }, 450.0, 1.0, target);

            Assert.Equal(2, plan.RelaidSegmentCount);
            double lengthB = Length(Move(plan, "B"));
            Assert.True(lengthB <= Math.Max(1000.0, 450.0) * 1.001,
                "k>1 时占位段不得被放大（实测 " + lengthB.ToString("0")
                + " > max(原画长 1000, 下限 450)）。");
            Assert.True(lengthB >= 450.0 * 0.999,
                "占位段仍受可读下限保护（实测 " + lengthB.ToString("0") + "）。");
            // 真实毫米段不受防棘轮约束：k = 50×0.65 = 32.5，A = 1000×32.5。
            Assert.Equal(32500.0, Length(Move(plan, "A")), 1);
            Assert.True(Width(plan) <= 75000.0 + 1e-3
                && Height(plan) <= 75000.0 + 1e-3,
                "最终包络仍必须落在目标矩形内。");
        }

        /// <summary>30000mm 的 X 向单段画进 30000×75000 的宽度主导目标。</summary>
        private static SketchProportionPlan PlanSingle30000(double fillRatio)
        {
            var target = new SketchProportionTarget(0.0, 0.0, 30000.0, 75000.0);
            return SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0),
                    new QuickLinePoint(30000 * Cos30, 30000 * Sin30), 30000)
            }, 10.0, 1.0, target, fillRatio);
        }

        private static SketchProportionInput Input(string id,
            QuickLinePoint start, QuickLinePoint end, double millimetres)
            => new SketchProportionInput(id, start, end, millimetres,
                completed: true, placeholderLabel: false);

        private static SketchSegmentMove Move(SketchProportionPlan plan,
            string id)
            => Assert.Single(plan.Moves, item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

        private static double Length(SketchSegmentMove move)
            => move.NewStart.DistanceTo(move.NewEnd);

        private static double MaxX(SketchProportionPlan plan)
            => plan.Moves.Max(item => Math.Max(item.NewStart.X, item.NewEnd.X));

        private static double MaxY(SketchProportionPlan plan)
            => plan.Moves.Max(item => Math.Max(item.NewStart.Y, item.NewEnd.Y));

        private static double MinX(SketchProportionPlan plan)
            => plan.Moves.Min(item => Math.Min(item.NewStart.X, item.NewEnd.X));

        private static double MinY(SketchProportionPlan plan)
            => plan.Moves.Min(item => Math.Min(item.NewStart.Y, item.NewEnd.Y));

        private static double Width(SketchProportionPlan plan)
            => MaxX(plan) - MinX(plan);

        private static double Height(SketchProportionPlan plan)
            => MaxY(plan) - MinY(plan);
    }
}
