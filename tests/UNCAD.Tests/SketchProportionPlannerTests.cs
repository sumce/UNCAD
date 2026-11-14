using System;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// Xauto 比例重排纯逻辑：按毫米值重排、极短段钳位、包络不超、占位段
    /// 按当前长短缩放、含未标注/不可分类线的分量整分量跳过。
    /// </summary>
    public class SketchProportionPlannerTests
    {
        private const double Cos30 = 0.86602540378443864676;
        private const double Sin30 = 0.5;

        [Fact]
        public void LRoute_FinalLengthsAreProportionalToMillimetreLabels()
        {
            // 画的是 1000+1000 的 L 形；标注是 3000(X)+1000(Z)。
            // 比例系数 k = min(866/2598, 1500/2500) = 1/3：
            // X 段最终 1000，Z 段最终 333.33，比值 = 3 = 标注比值。
            var aEnd = new QuickLinePoint(1000 * Cos30, 1000 * Sin30);
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0), aEnd, 3000),
                Input("B", aEnd, new QuickLinePoint(aEnd.X, aEnd.Y + 1000), 1000)
            }, 10.0);

            Assert.Equal(2, plan.RelaidSegmentCount);
            SketchSegmentMove moveA = Move(plan, "A");
            SketchSegmentMove moveB = Move(plan, "B");

            Assert.Equal(1000.0, Length(moveA), 4);
            Assert.Equal(1000.0 / 3.0, Length(moveB), 4);
            Assert.Equal(3000.0 / 1000.0, Length(moveA) / Length(moveB), 6);

            // 方向吸附到精确轴测角：A 落在 30°，B 落在 90°。
            Assert.Equal(30.0, Angle(moveA), 4);
            Assert.Equal(90.0, Angle(moveB), 4);

            // 包络不超原大小；两段仍端点相连（同一节点投影一致）。
            Assert.Equal(moveA.NewEnd.X, moveB.NewStart.X, 6);
            Assert.Equal(moveA.NewEnd.Y, moveB.NewStart.Y, 6);
            Assert.Equal(1000 * Cos30, MaxX(plan), 4);
            Assert.True(MaxY(plan) <= 1500.0 + 1e-6);

            // 深度可用且沿视线轴：A 中点 (1500,0,0)→z−x−y=−1500；
            // B 中点 (3000,0,500)→−2500（更高但同时更远，x 项主导）。
            Assert.Equal(-1500.0, plan.Depths["A"].Closeness, 3);
            Assert.Equal(-2500.0, plan.Depths["B"].Closeness, 3);
        }

        [Fact]
        public void ShortRealSegment_IsClampedToMinimumReadableLength()
        {
            // 5400mm 与 200mm；k = 3000/5400 = 5/9。200mm 折算 111 < 450，
            // 钳到 450/k = 810 参与第二遍：5400→3000，200→450。
            var aEnd = new QuickLinePoint(3000 * Cos30, 3000 * Sin30);
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0), aEnd, 5400),
                Input("B", aEnd, new QuickLinePoint(aEnd.X, aEnd.Y + 3000), 200)
            }, 450.0);

            Assert.Equal(2, plan.RelaidSegmentCount);
            Assert.Equal(3000.0, Length(Move(plan, "A")), 4);
            Assert.Equal(450.0, Length(Move(plan, "B")), 4);
        }

        [Fact]
        public void PlaceholderLabel_ScalesByDrawnLengthNotLabelValue()
        {
            // B 是未确认的 2000mm 占位（画了 1000 单位）：参与缩放的是
            // 当前图上长度 1000，不是占位数值 2000。
            var aEnd = new QuickLinePoint(1000 * Cos30, 1000 * Sin30);
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0), aEnd, 3000),
                new SketchProportionInput("B", aEnd,
                    new QuickLinePoint(aEnd.X, aEnd.Y + 1000), 2000,
                    completed: false, placeholderLabel: true)
            }, 10.0);

            Assert.Equal(2, plan.RelaidSegmentCount);
            // k 仍由 3000:1000 的显示距离决定 = 1/3。
            Assert.Equal(1000.0, Length(Move(plan, "A")), 4);
            Assert.Equal(1000.0 / 3.0, Length(Move(plan, "B")), 4);
        }

        [Fact]
        public void ComponentWithUnclassifiableLine_IsSkippedAsAWhole()
        {
            var aEnd = new QuickLinePoint(1000 * Cos30, 1000 * Sin30);
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0), aEnd, 3000),
                Input("B", aEnd, new QuickLinePoint(aEnd.X + 1000, aEnd.Y + 1000),
                    2000)
            }, 450.0);

            Assert.Equal(0, plan.RelaidSegmentCount);
            Assert.Contains(plan.Diagnostics, item => item.Contains("不可分类"));
        }

        [Fact]
        public void ComponentWithUnlabelledLine_IsSkippedAsAWhole()
        {
            var aEnd = new QuickLinePoint(1000 * Cos30, 1000 * Sin30);
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0), aEnd, 3000),
                new SketchProportionInput("B", aEnd,
                    new QuickLinePoint(aEnd.X, aEnd.Y + 1000), null,
                    completed: false, placeholderLabel: false)
            }, 450.0);

            Assert.Equal(0, plan.RelaidSegmentCount);
            Assert.Contains(plan.Diagnostics, item => item.Contains("未标注"));
        }

        [Fact]
        public void SingleSegment_SnapsToExactAxisAngle()
        {
            // 手画的 29.66° ≈ 30°（容差内）；重排后方向精确 30°，
            // 长度 = 2000×k，k = min(870/1732, 495/1000) = 0.495。
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("S", new QuickLinePoint(0, 0),
                    new QuickLinePoint(870.0, 495.0), 2000)
            }, 10.0);

            Assert.Single(plan.Moves);
            SketchSegmentMove move = plan.Moves[0];
            Assert.Equal(30.0, Angle(move), 4);
            Assert.Equal(990.0, Length(move), 3);
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

        private static double Angle(SketchSegmentMove move)
        {
            double degrees = Math.Atan2(move.NewEnd.Y - move.NewStart.Y,
                move.NewEnd.X - move.NewStart.X) * 180.0 / Math.PI;
            if (degrees < 0) degrees += 360.0;
            return degrees;
        }

        private static double MaxX(SketchProportionPlan plan)
            => plan.Moves.Max(item => Math.Max(item.NewStart.X, item.NewEnd.X));

        private static double MaxY(SketchProportionPlan plan)
            => plan.Moves.Max(item => Math.Max(item.NewStart.Y, item.NewEnd.Y));
    }
}
