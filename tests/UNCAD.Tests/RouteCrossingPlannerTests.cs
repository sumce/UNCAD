using System;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// Xauto 交叉检测与拱桥裁定：严格内部交叉才开洞、共享端点/丁字连接
    /// 不开洞、靠近相机的线开洞、等深/缺深度跳过并点名、洞距与端点间距校验。
    /// </summary>
    public class RouteCrossingPlannerTests
    {
        [Fact]
        public void XCrossing_NearerSegmentHopsAtInterior()
        {
            var plan = RouteCrossingPlanner.Plan(new[]
            {
                Seg("A", 0, 0, 1000, 0, closeness: 0),
                Seg("B", 500, -500, 500, 500, closeness: 5)
            }, 300.0);

            Assert.Empty(plan.Skipped);
            Assert.Empty(plan.DroppedHops);
            RouteHopPlan hop = Assert.Single(plan.Hops);
            Assert.Equal("B", hop.SegmentId);
            Assert.Equal(500.0, Assert.Single(hop.Centers), 4);
        }

        [Fact]
        public void SharedEndpointJunction_IsNotACrossing()
        {
            var plan = RouteCrossingPlanner.Plan(new[]
            {
                Seg("A", 0, 0, 1000, 0, closeness: 0),
                Seg("B", 1000, 0, 1200, 200, closeness: 5)
            }, 300.0);

            Assert.Empty(plan.Hops);
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public void TJunction_EndpointOnInteriorIsNotACrossing()
        {
            var plan = RouteCrossingPlanner.Plan(new[]
            {
                Seg("A", 0, 0, 1000, 0, closeness: 0),
                Seg("B", 500, 0, 500, 500, closeness: 5)
            }, 300.0);

            Assert.Empty(plan.Hops);
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public void MissingDepthOnEitherSide_CrossingIsSkippedAndNamed()
        {
            var plan = RouteCrossingPlanner.Plan(new[]
            {
                Seg("A", 0, 0, 1000, 0, closeness: 0),
                Seg("B", 500, -500, 500, 500, closeness: null)
            }, 300.0);

            Assert.Empty(plan.Hops);
            Assert.Contains(plan.Skipped, item =>
                item.Contains("A×B") && item.Contains("缺少等测深度"));
        }

        [Fact]
        public void EqualDepth_TrueIntersectionIsSkippedAndNamed()
        {
            var plan = RouteCrossingPlanner.Plan(new[]
            {
                Seg("A", 0, 0, 1000, 0, closeness: 3),
                Seg("B", 500, -500, 500, 500, closeness: 3.0)
            }, 300.0);

            Assert.Empty(plan.Hops);
            Assert.Contains(plan.Skipped, item => item.Contains("三维同点"));
        }

        [Fact]
        public void MixedProjectionFamilies_AreTreatedAsUndeterminable()
        {
            var plan = RouteCrossingPlanner.Plan(new[]
            {
                Seg("A", 0, 0, 1000, 0, closeness: 1,
                    mode: QuickLineProjectionMode.Isometric),
                Seg("B", 500, -500, 500, 500, closeness: 9,
                    mode: QuickLineProjectionMode.Orthographic)
            }, 300.0);

            Assert.Empty(plan.Hops);
            Assert.Contains(plan.Skipped, item => item.Contains("缺少等测深度"));
        }

        [Fact]
        public void TwoHopsCloserThanDiameter_SecondIsDropped()
        {
            // 两条远处的竖线分别与 A 交叉 → 两个洞都落在 A 上（1000 与 1100），
            // 间距 100 < 直径 300：保留第一个，丢弃第二个并点名。
            var plan = RouteCrossingPlanner.Plan(new[]
            {
                Seg("A", 0, 0, 10000, 0, closeness: 0),
                Seg("B1", 1000, -500, 1000, 500, closeness: -5),
                Seg("B2", 1100, -500, 1100, 500, closeness: -5)
            }, 300.0);

            RouteHopPlan hop = Assert.Single(plan.Hops);
            Assert.Equal("A", hop.SegmentId);
            Assert.Equal(1000.0, Assert.Single(hop.Centers), 4);
            Assert.Single(plan.DroppedHops);
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public void HopTooCloseToEndpoint_IsDropped()
        {
            var plan = RouteCrossingPlanner.Plan(new[]
            {
                Seg("A", 0, 0, 1000, 0, closeness: 0),
                Seg("B", 100, -500, 100, 500, closeness: -5)
            }, 300.0);

            Assert.Empty(plan.Hops);
            Assert.Single(plan.DroppedHops);
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public void WellSpacedHops_BothKeptInOrder()
        {
            var plan = RouteCrossingPlanner.Plan(new[]
            {
                Seg("A", 0, 0, 10000, 0, closeness: 0),
                Seg("B1", 1000, -500, 1000, 500, closeness: -5),
                Seg("B2", 2000, -500, 2000, 500, closeness: -5)
            }, 300.0);

            RouteHopPlan hop = Assert.Single(plan.Hops);
            Assert.Equal("A", hop.SegmentId);
            Assert.Equal(2, hop.Centers.Count);
            Assert.Equal(1000.0, hop.Centers[0], 4);
            Assert.Equal(2000.0, hop.Centers[1], 4);
        }

        [Fact]
        public void ParallelAndCollinearSegments_NeverProduceHops()
        {
            var plan = RouteCrossingPlanner.Plan(new[]
            {
                Seg("A", 0, 0, 1000, 0, closeness: 0),
                Seg("B", 0, 100, 1000, 100, closeness: 5),
                // 共线重叠（同向）
                Seg("C", 500, 0, 1500, 0, closeness: -5)
            }, 300.0);

            Assert.Empty(plan.Hops);
            Assert.Empty(plan.Skipped);
        }

        private static RouteCrossingSegment Seg(string id,
            double x1, double y1, double x2, double y2, double? closeness,
            QuickLineProjectionMode mode = QuickLineProjectionMode.Isometric)
            => new RouteCrossingSegment(id, new QuickLinePoint(x1, y1),
                new QuickLinePoint(x2, y2), closeness, mode);
    }
}
