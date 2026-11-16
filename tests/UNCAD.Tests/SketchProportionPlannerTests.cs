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

        [Fact]
        public void TargetSpace_SketchScalesUpToFillFrameAndCentres()
        {
            // 3.2.0.1（D-053 修订）：图框可用空间 75000×7500、中心 (40000,2000)。
            // 3000(X)+1000(Z) 的投影包络 = 2598.08×2500 → k = min(28.87, 3) = 3：
            // A 最终 9000、B 最终 3000，联合包络 7794.23×7500 恰好贴住高度，
            // 包络中心精确落在目标中心（不再是旧的"原图范围内原地不动"）。
            var aEnd = new QuickLinePoint(1000 * Cos30, 1000 * Sin30);
            var target = new SketchProportionTarget(40000.0, 2000.0, 75000.0, 7500.0);
            // 3.2.0.11 起 Plan 默认 fillRatio=0.65；本组旧断言锚定"铺满"
            // 行为，显式传 1.0 保持旧期望值（迁移锚点，不改断言意图）。
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0), aEnd, 3000),
                Input("B", aEnd, new QuickLinePoint(aEnd.X, aEnd.Y + 1000), 1000)
            }, 450.0, 1.0, target, 1.0);

            Assert.Equal(2, plan.RelaidSegmentCount);
            Assert.Equal(9000.0, Length(Move(plan, "A")), 3);
            Assert.Equal(3000.0, Length(Move(plan, "B")), 3);
            Assert.Equal(30.0, Angle(Move(plan, "A")), 4);
            Assert.Equal(90.0, Angle(Move(plan, "B")), 4);

            Assert.Equal(40000.0, (MinX(plan) + MaxX(plan)) / 2.0, 2);
            Assert.Equal(2000.0, (MinY(plan) + MaxY(plan)) / 2.0, 2);
            Assert.True(MaxY(plan) - MinY(plan) <= 7500.0 + 1e-3,
                "联合包络高度不得超出目标空间");
            Assert.True(MaxX(plan) - MinX(plan) <= 75000.0 + 1e-3,
                "联合包络宽度不得超出目标空间");
        }

        [Fact]
        public void ShortSegment_NeverShorterThanItsMillimetreLabel()
        {
            // 现场报告"短段比毫米标还短"：A=54000mm 主导把 k 压到
            // min(75000/46765, 7500/27200)=0.2757，200mm 段本会缩到 55；
            // 标注文字宽 1000 单位 → 下限 = max(450, 1000×1.15) = 1150，
            // B 最终被钳到恰好 1150（不再比自己的毫米标短）。
            var aEnd = new QuickLinePoint(5400 * Cos30, 5400 * Sin30);
            var target = new SketchProportionTarget(0.0, 0.0, 75000.0, 7500.0);
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0), aEnd, 54000),
                new SketchProportionInput("B", aEnd,
                    new QuickLinePoint(aEnd.X, aEnd.Y + 200), 200,
                    completed: true, placeholderLabel: false, labelWidthUnits: 1000.0)
            }, 450.0, 1.0, target, 1.0);

            Assert.Equal(2, plan.RelaidSegmentCount);
            Assert.Equal(1150.0, Length(Move(plan, "B")), 3);
            Assert.True(Length(Move(plan, "B")) >= 1150.0 * 0.999,
                "短段必须放得下自己的毫米标");
        }

        [Fact]
        public void TwoComponents_ShareOneScaleAndKeepRelativeOffset()
        {
            // 两个相距 20000 的独立单段（各 1000mm，30°）。联合包络随 k：
            // 宽 = 20000+866k ≤ 75000、高 = 500k ≤ 7500 → k = 15。
            // 两段等长（同比例）、中心距保持 20000（相对位置不变）、居中目标。
            var target = new SketchProportionTarget(0.0, 0.0, 75000.0, 7500.0);
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("P", new QuickLinePoint(0, 0),
                    new QuickLinePoint(1000 * Cos30, 1000 * Sin30), 1000),
                Input("Q", new QuickLinePoint(20000.0, 0),
                    new QuickLinePoint(20000.0 + 1000 * Cos30, 1000 * Sin30), 1000)
            }, 10.0, 1.0, target, 1.0);

            Assert.Equal(2, plan.RelaidSegmentCount);
            double lengthP = Length(Move(plan, "P"));
            double lengthQ = Length(Move(plan, "Q"));
            Assert.Equal(15000.0, lengthP, 2);
            Assert.Equal(lengthP, lengthQ, 4);

            double centreOffset = MoveCentreX(plan, "Q") - MoveCentreX(plan, "P");
            Assert.Equal(20000.0, centreOffset, 2);
            Assert.Equal(0.0, (MinX(plan) + MaxX(plan)) / 2.0, 2);
            Assert.Equal(0.0, (MinY(plan) + MaxY(plan)) / 2.0, 2);
        }

        [Fact]
        public void ClampedLayout_FinalEnvelopeNeverExceedsTarget()
        {
            // 3.2.0.3：钳位下限会撑大二遍包络（二分只按一遍估计）——装后校验
            // 收缩环必须把最终几何收回目标内（用户现场报告"超了"）。54000mm
            // 主导 + 200mm 短段（标注宽 1000 → 下限 1150），窄目标逼钳位生效。
            var aEnd = new QuickLinePoint(5400 * Cos30, 5400 * Sin30);
            var target = new SketchProportionTarget(0.0, 0.0, 40000.0, 5000.0);
            var plan = SketchProportionPlanner.Plan(new[]
            {
                Input("A", new QuickLinePoint(0, 0), aEnd, 54000),
                new SketchProportionInput("B", aEnd,
                    new QuickLinePoint(aEnd.X, aEnd.Y + 200), 200,
                    completed: true, placeholderLabel: false, labelWidthUnits: 1000.0)
            }, 450.0, 1.0, target, 1.0);

            Assert.Equal(2, plan.RelaidSegmentCount);
            Assert.True(MaxX(plan) - MinX(plan) <= 40000.0 + 0.5,
                "钳位后联合包络宽必须收回目标内（实测 "
                + (MaxX(plan) - MinX(plan)).ToString("0") + " > 40000）。");
            Assert.True(MaxY(plan) - MinY(plan) <= 5000.0 + 0.5,
                "钳位后联合包络高必须收回目标内（实测 "
                + (MaxY(plan) - MinY(plan)).ToString("0") + " > 5000）。");
            // 收缩不牺牲短段下限精神：B 仍明显长于洞径下限 450 的比例值。
            Assert.True(Length(Move(plan, "B")) > 450.0,
                "收缩后短段仍应保住可读下限的精神（实测 "
                + Length(Move(plan, "B")).ToString("0") + "）。");
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

        private static double MinX(SketchProportionPlan plan)
            => plan.Moves.Min(item => Math.Min(item.NewStart.X, item.NewEnd.X));

        private static double MinY(SketchProportionPlan plan)
            => plan.Moves.Min(item => Math.Min(item.NewStart.Y, item.NewEnd.Y));

        private static double MoveCentreX(SketchProportionPlan plan, string id)
        {
            SketchSegmentMove move = Move(plan, id);
            return (move.NewStart.X + move.NewEnd.X) / 2.0;
        }
    }
}
