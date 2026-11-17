using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1B 软管轮廓几何（HoseOutlinePlanner，3.3.3 D-054 Amendment）的逐值断言：
    /// 直线段偏移、L 形外弧+内截断、U 形双拐角、端帽半圆、退化输入清理、
    /// 按长度取点（引线中点）。期望值全部手工推导（见各用例注释）。
    /// </summary>
    public sealed class HoseOutlinePlannerTests
    {
        private const double Tolerance = 1e-6;

        private static List<QuickLinePoint> Path(params double[] coordinates)
        {
            var points = new List<QuickLinePoint>();
            for (int i = 0; i + 1 < coordinates.Length; i += 2)
                points.Add(new QuickLinePoint(coordinates[i], coordinates[i + 1]));
            return points;
        }

        private static void AssertVertex(List<HoseOutlineVertex> outline, int index,
            double x, double y, double bulge)
        {
            Assert.True(index < outline.Count,
                "outline vertex " + index + " missing (count=" + outline.Count + ")");
            Assert.Equal(x, outline[index].Point.X, 6);
            Assert.Equal(y, outline[index].Point.Y, 6);
            Assert.Equal(bulge, outline[index].Bulge, Tolerance);
        }

        [Fact]
        public void StraightPath_BuildsStadiumWithSemicircleCaps()
        {
            // (0,0)→(1000,0)，d=100（r=50）：矩形双线 + 两端半圆帽（|bulge|=1，
            // 鼓出侧背向路径）。手工推导：
            // (0,-50)帽弧→(0,50)→(1000,50)帽弧→(1000,-50)→闭合。
            bool ok = HoseOutlinePlanner.TryPlan(Path(0, 0, 1000, 0), 100,
                out List<HoseOutlineVertex> outline, out double length);
            Assert.True(ok);
            Assert.Equal(4, outline.Count);
            AssertVertex(outline, 0, 0, -50, -1);
            AssertVertex(outline, 1, 0, 50, 0);
            AssertVertex(outline, 2, 1000, 50, -1);
            AssertVertex(outline, 3, 1000, -50, 0);
            Assert.Equal(1000, length, Tolerance);
        }

        [Fact]
        public void LShape_InnerCornerMiterOuterCornerArc()
        {
            // (0,0)→(1000,0)→(1000,1000)：左转 90°。左=内侧→偏移线交点 (950,50)
            // 截断；右=外侧→半径 50 圆弧（切点 (1050,0)/(1000,-50)，四分之一弧
            // bulge=-tan(π/8)）。端帽在 (0,0)（鼓向 -x）与 (1000,1000)（鼓向 +y）。
            bool ok = HoseOutlinePlanner.TryPlan(Path(0, 0, 1000, 0, 1000, 1000), 100,
                out List<HoseOutlineVertex> outline, out double length);
            Assert.True(ok);
            Assert.Equal(7, outline.Count);
            AssertVertex(outline, 0, 0, -50, -1);        // 起点端帽（鼓向 -x）
            AssertVertex(outline, 1, 0, 50, 0);
            AssertVertex(outline, 2, 950, 50, 0);        // 左侧内角 miter
            AssertVertex(outline, 3, 950, 1000, -1);     // 终点端帽（鼓向 +y）
            AssertVertex(outline, 4, 1050, 1000, 0);
            AssertVertex(outline, 5, 1050, 0, -Math.Tan(Math.PI / 8)); // 右侧外角弧
            AssertVertex(outline, 6, 1000, -50, 0);
            Assert.Equal(2000, length, Tolerance);
        }

        [Fact]
        public void UShape_HandlesBothTurnDirections()
        {
            // (0,0)→(1000,0)→(1000,1000)→(0,1000)：两次左转——左侧两个内 miter
            // (950,50)/(950,950)，右侧两个外弧（切点 (1000,1050)/(1050,1000) 与
            // (1050,0)/(1000,-50)）；终点方向 -x，端帽鼓向 -x。
            bool ok = HoseOutlinePlanner.TryPlan(
                Path(0, 0, 1000, 0, 1000, 1000, 0, 1000), 100,
                out List<HoseOutlineVertex> outline, out double length);
            Assert.True(ok);
            Assert.Equal(10, outline.Count);
            double quarter = -Math.Tan(Math.PI / 8);
            AssertVertex(outline, 0, 0, -50, -1);          // 起点端帽
            AssertVertex(outline, 1, 0, 50, 0);
            AssertVertex(outline, 2, 950, 50, 0);          // 左侧内 miter ①
            AssertVertex(outline, 3, 950, 950, 0);         // 左侧内 miter ②
            AssertVertex(outline, 4, 0, 950, -1);          // 终点端帽（鼓向 -x）
            AssertVertex(outline, 5, 0, 1050, 0);
            AssertVertex(outline, 6, 1000, 1050, quarter); // 右侧外弧 ②
            AssertVertex(outline, 7, 1050, 1000, 0);
            AssertVertex(outline, 8, 1050, 0, quarter);    // 右侧外弧 ①
            AssertVertex(outline, 9, 1000, -50, 0);
            Assert.Equal(3000, length, Tolerance);
        }

        [Fact]
        public void RightTurn_MirrorsArcAndMiterSides()
        {
            // (0,0)→(1000,0)→(1000,-1000)：右转 90°——内 miter 换到下侧右链
            // (950,-50)，外弧换到上侧左链（切点 (1000,50)/(1050,0)）。与 LShape
            // 镜像，证明侧向判定不偏科。
            bool ok = HoseOutlinePlanner.TryPlan(Path(0, 0, 1000, 0, 1000, -1000), 100,
                out List<HoseOutlineVertex> outline, out _);
            Assert.True(ok);
            Assert.Equal(7, outline.Count);
            AssertVertex(outline, 0, 0, -50, -1);                    // 起点端帽
            AssertVertex(outline, 1, 0, 50, 0);
            AssertVertex(outline, 2, 1000, 50, -Math.Tan(Math.PI / 8)); // 左侧外弧
            AssertVertex(outline, 3, 1050, 0, 0);
            AssertVertex(outline, 4, 1050, -1000, -1);               // 终点端帽（鼓向 -y）
            AssertVertex(outline, 5, 950, -1000, 0);
            AssertVertex(outline, 6, 950, -50, 0);                   // 右侧内 miter
        }

        [Fact]
        public void DegenerateInputs_CollapseOrReject()
        {
            // 重复点：与直线两点同形。
            Assert.True(HoseOutlinePlanner.TryPlan(
                Path(0, 0, 0, 0, 1000, 0, 1000, 0), 100,
                out List<HoseOutlineVertex> deduped, out _));
            Assert.Equal(4, deduped.Count);

            // 共线中间点：剔除，同直线两点。
            Assert.True(HoseOutlinePlanner.TryPlan(
                Path(0, 0, 500, 0, 1000, 0), 100,
                out List<HoseOutlineVertex> collinear, out _));
            Assert.Equal(4, collinear.Count);
            AssertVertex(collinear, 2, 1000, 50, -1);

            // 点数不足 / 全重点 → false（调用方礼貌退出）。
            Assert.False(HoseOutlinePlanner.TryPlan(Path(0, 0), 100, out _, out _));
            Assert.False(HoseOutlinePlanner.TryPlan(
                Path(0, 0, 0, 0, 1e-9, 0), 100, out _, out _));

            // 直径非法 → false。
            Assert.False(HoseOutlinePlanner.TryPlan(Path(0, 0, 1000, 0), 0, out _, out _));
            Assert.False(HoseOutlinePlanner.TryPlan(Path(0, 0, 1000, 0), -100, out _, out _));
            Assert.False(HoseOutlinePlanner.TryPlan(
                Path(0, 0, 1000, 0), double.NaN, out _, out _));
            Assert.False(HoseOutlinePlanner.TryPlan(null, 100, out _, out _));
        }

        [Fact]
        public void NearReversalSpike_FallsBackToBoundedBevel()
        {
            // (0,0)→(1000,0)→(5,5)：近 180° 折返。内侧偏移线交点射远（≈ r/cos(δ/2)
            // 上万），超过 MiterLimitFactor×r → 退化为 bevel（直连两偏移端点），
            // 输出有界、无异常。
            bool ok = HoseOutlinePlanner.TryPlan(Path(0, 0, 1000, 0, 5, 5), 100,
                out List<HoseOutlineVertex> outline, out double length);
            Assert.True(ok);
            Assert.Equal(1000 + Math.Sqrt(995 * 995 + 25), length, 1e-3);
            foreach (HoseOutlineVertex vertex in outline)
            {
                Assert.True(Math.Abs(vertex.Point.X) < 1200
                    && Math.Abs(vertex.Point.Y) < 1200,
                    "bevel 兜底后顶点必须有界: " + vertex.Point);
                Assert.False(double.IsNaN(vertex.Bulge)
                    || double.IsInfinity(vertex.Bulge));
            }
        }

        [Fact]
        public void PointAtDistance_WalksCenterline()
        {
            var path = Path(0, 0, 1000, 0, 1000, 1000);
            Assert.Equal(500, HoseOutlinePlanner.PointAtDistance(path, 500).X, 6);
            Assert.Equal(0, HoseOutlinePlanner.PointAtDistance(path, 500).Y, 6);
            // 全长 2000 的长度中点=拐角 (1000,0)（边界归属第一段末）。
            QuickLinePoint mid = HoseOutlinePlanner.PointAtDistance(path, 1000);
            Assert.Equal(1000, mid.X, 6);
            Assert.Equal(0, mid.Y, 6);
            // 第二段内部：1500 → (1000,500)。
            QuickLinePoint second = HoseOutlinePlanner.PointAtDistance(path, 1500);
            Assert.Equal(1000, second.X, 6);
            Assert.Equal(500, second.Y, 6);
            Assert.Equal(0, HoseOutlinePlanner.PointAtDistance(path, 0).X, 6);
            QuickLinePoint tail = HoseOutlinePlanner.PointAtDistance(path, 99999);
            Assert.Equal(1000, tail.X, 6);
            Assert.Equal(1000, tail.Y, 6);
        }

        [Fact]
        public void OutlineVertexOrder_IsContiguous()
        {
            // 任意路径的相邻输出点必须能在同一偏移线/圆弧上连续衔接——这里用
            // “每条输出边的长度都有限且无 NaN”与“轮廓点数 ≥ 4”兜底连续性，
            // 具体形状已由上面的逐值用例锁定。
            bool ok = HoseOutlinePlanner.TryPlan(
                Path(0, 0, 800, 0, 800, 600, 200, 600, 200, 200), 80,
                out List<HoseOutlineVertex> outline, out _);
            Assert.True(ok);
            Assert.True(outline.Count >= 4);
            for (int i = 0; i < outline.Count; i++)
            {
                QuickLinePoint a = outline[i].Point;
                QuickLinePoint b = outline[(i + 1) % outline.Count].Point;
                double distance = a.DistanceTo(b);
                Assert.False(double.IsNaN(distance) || double.IsInfinity(distance));
                Assert.True(distance < 2000, "edge " + i + " too long: " + distance);
            }
        }
    }
}
