using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLinePlanUnfoldTests
    {
        private static QuickLineCreatedSegment Segment(string id, string start,
            string end, QuickLineSpatialAxis axis, int sign, double distance)
            => new QuickLineCreatedSegment(id, start, end, axis, sign, distance);

        [Fact]
        public void Build_EmptyInput_ReturnsEmpty()
        {
            Assert.Empty(QuickLinePlanUnfold.Build(
                new List<QuickLineCreatedSegment>()));
            Assert.Empty(QuickLinePlanUnfold.Build(null));
        }

        [Fact]
        public void Build_XThenY_UnfoldsToPlanAxes()
        {
            var segments = new List<QuickLineCreatedSegment>
            {
                Segment("S1", "N1", "N2", QuickLineSpatialAxis.X, 1, 2000),
                Segment("S2", "N2", "N3", QuickLineSpatialAxis.Y, 1, 1500)
            };
            var plan = QuickLinePlanUnfold.Build(segments);
            Assert.Equal(2, plan.Count);
            Assert.Equal(0, plan[0].StartX);
            Assert.Equal(0, plan[0].StartY);
            Assert.Equal(2000, plan[0].EndX);
            Assert.Equal(0, plan[0].EndY);
            Assert.Equal(2000, plan[1].StartX);
            Assert.Equal(0, plan[1].StartY);
            Assert.Equal(2000, plan[1].EndX);
            Assert.Equal(1500, plan[1].EndY);
        }

        [Fact]
        public void Build_NegativeSign_ReversesPlanDirection()
        {
            var segments = new List<QuickLineCreatedSegment>
            {
                Segment("S1", "N1", "N2", QuickLineSpatialAxis.X, -1, 800),
                Segment("S2", "N2", "N3", QuickLineSpatialAxis.Y, -1, 600)
            };
            var plan = QuickLinePlanUnfold.Build(segments);
            Assert.Equal(-800, plan[0].EndX);
            Assert.Equal(0, plan[0].EndY);
            Assert.Equal(-800, plan[1].StartX);
            Assert.Equal(-600, plan[1].EndY);
        }

        [Fact]
        public void Build_PureVerticalSegments_ProduceNoPlanLines()
        {
            var segments = new List<QuickLineCreatedSegment>
            {
                Segment("S1", "N1", "N2", QuickLineSpatialAxis.Z, 1, 3000),
                Segment("S2", "N2", "N3", QuickLineSpatialAxis.X, 1, 1200)
            };
            var plan = QuickLinePlanUnfold.Build(segments);
            var single = Assert.Single(plan);
            Assert.Equal("S2", single.Id);
            // 垂直段两端点在平面中重合于 N2 的展开位置。
            Assert.Equal(0, single.StartX);
            Assert.Equal(0, single.StartY);
            Assert.Equal(1200, single.EndX);
        }

        [Fact]
        public void Build_OpenLoopWithMismatch_KeepsVisitedNodeAndSkipsSegment()
        {
            // S3 从 N1 反向走 600,而 N2 已由 S1 推导在 X=1000:
            // 端点坐标不符 → 跳过该段,不让整条路线报废。
            var segments = new List<QuickLineCreatedSegment>
            {
                Segment("S1", "N1", "N2", QuickLineSpatialAxis.X, 1, 1000),
                Segment("S2", "N2", "N3", QuickLineSpatialAxis.Y, 1, 500),
                Segment("S3", "N3", "N1", QuickLineSpatialAxis.X, -1, 600)
            };
            var plan = QuickLinePlanUnfold.Build(segments);
            Assert.Equal(2, plan.Count);
            Assert.DoesNotContain(plan, item => item.Id == "S3");
        }

        [Fact]
        public void Build_ClosedLoopConsistent_ProducesAllSegments()
        {
            var segments = new List<QuickLineCreatedSegment>
            {
                Segment("S1", "N1", "N2", QuickLineSpatialAxis.X, 1, 1000),
                Segment("S2", "N2", "N3", QuickLineSpatialAxis.Y, 1, 500),
                Segment("S3", "N3", "N4", QuickLineSpatialAxis.X, -1, 1000),
                Segment("S4", "N4", "N1", QuickLineSpatialAxis.Y, -1, 500)
            };
            var plan = QuickLinePlanUnfold.Build(segments);
            Assert.Equal(4, plan.Count);
            Assert.All(plan, item =>
                Assert.Equal(item.DistanceMillimetres,
                    Math.Abs(item.EndX - item.StartX) + Math.Abs(item.EndY - item.StartY)));
        }

        [Fact]
        public void Build_NonPositiveDistance_SkipsSegment()
        {
            var segments = new List<QuickLineCreatedSegment>
            {
                Segment("S1", "N1", "N2", QuickLineSpatialAxis.X, 1, 0),
                Segment("S2", "N2", "N3", QuickLineSpatialAxis.X, 1, double.NaN),
                Segment("S3", "N3", "N4", QuickLineSpatialAxis.X, 1, 700)
            };
            var plan = QuickLinePlanUnfold.Build(segments);
            // 无效段不生成线也不推导节点坐标,后续段因起点未知一并跳过。
            Assert.Empty(plan);
        }
    }
}
