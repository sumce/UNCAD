using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-050 连续段分趟规划器：端点容差连通分组 + 链排序。锁定：
    /// 每个连通组各成一趟、链方向翻转的成员照样拼成连续链、分叉不猜
    /// （拆回单段趟）、容差边界、总长度求和。
    /// </summary>
    public sealed class ParallelRunPlannerTests
    {
        [Fact]
        public void TwoConnectedLines_MergeIntoOneRun()
        {
            var runs = ParallelRunPlanner.Plan(new List<ParallelRunMember>
            {
                Member("A", 1000, P(0, 0), P(1000, 0)),
                Member("B", 1000, P(1000, 0), P(1000, 1000))
            });
            var run = Assert.Single(runs);
            Assert.True(run.Merged);
            Assert.Equal(2, run.Members.Count);
            Assert.Equal(2000, run.TotalLengthMm, 6);
            // 连续链：三个顶点、两段、无缺口。
            Assert.Equal(3, run.Vertices.Count);
            Assert.Equal(1000, run.Vertices[1].DistanceTo(run.Vertices[0]), 6);
            Assert.Equal(1000, run.Vertices[2].DistanceTo(run.Vertices[1]), 6);
        }

        [Fact]
        public void DisconnectedLines_EachBecomesOwnRun()
        {
            var runs = ParallelRunPlanner.Plan(new List<ParallelRunMember>
            {
                Member("A", 100, P(0, 0), P(100, 0)),
                Member("B", 100, P(5000, 0), P(5100, 0))
            });
            Assert.Equal(2, runs.Count);
            Assert.All(runs, run => Assert.False(run.Merged));
            Assert.All(runs, run => Assert.Equal(100, run.TotalLengthMm, 6));
        }

        [Fact]
        public void ReversedMiddleMember_StillFormsContinuousChain()
        {
            // 三段链，中间段绘制方向相反（Z 形）：链排序必须翻转它。
            var runs = ParallelRunPlanner.Plan(new List<ParallelRunMember>
            {
                Member("A", 1000, P(0, 0), P(1000, 0)),
                Member("B", 1000, P(2000, 0), P(1000, 0)), // 反向
                Member("C", 1000, P(2000, 0), P(2000, 1000))
            });
            var run = Assert.Single(runs);
            Assert.Equal(3, run.Members.Count);
            Assert.Equal(4, run.Vertices.Count);
            // 顶点必须构成连续路径（相邻间距 = 原段长）。
            for (int i = 0; i < 3; i++)
                Assert.Equal(1000, run.Vertices[i + 1].DistanceTo(run.Vertices[i]),
                    6);
            Assert.Equal(3000, run.TotalLengthMm, 6);
        }

        [Fact]
        public void StarJunction_DoesNotGuess_SplitsToSingleRuns()
        {
            // 三段共端点（分叉）：不猜，全部退回单段趟（旧行为）。端点接在
            // 别段中点的"T 接"在端点聚类模型下视为不连通，同样各自成趟。
            var runs = ParallelRunPlanner.Plan(new List<ParallelRunMember>
            {
                Member("A", 1000, P(0, 0), P(1000, 0)),
                Member("B", 1000, P(0, 0), P(0, 1000)),
                Member("C", 1000, P(0, 0), P(-1000, 0))
            });
            Assert.Equal(3, runs.Count);
            Assert.All(runs, run => Assert.False(run.Merged));
        }

        [Fact]
        public void EndpointTolerance_Boundary()
        {
            // 间隙 0.5 < 容差 1.0：连通。
            var connected = ParallelRunPlanner.Plan(new List<ParallelRunMember>
            {
                Member("A", 100, P(0, 0), P(100, 0)),
                Member("B", 100, P(100.5, 0), P(200.5, 0))
            });
            Assert.Single(connected);
            // 间隙 2.0 > 容差：两趟。
            var split = ParallelRunPlanner.Plan(new List<ParallelRunMember>
            {
                Member("A", 100, P(0, 0), P(100, 0)),
                Member("B", 100, P(102, 0), P(202, 0))
            });
            Assert.Equal(2, split.Count);
        }

        [Fact]
        public void PolylineMember_JoinsChainWithItsVertices()
        {
            // 折线成员（弧已细分为多顶点）参与链拼接，细分点不丢。
            var runs = ParallelRunPlanner.Plan(new List<ParallelRunMember>
            {
                MemberWithVertices("A", 2000, P(0, 0), P(500, 0), P(1000, 0),
                    P(1000, 500), P(1000, 1000)),
                Member("B", 1000, P(1000, 1000), P(2000, 1000))
            });
            var run = Assert.Single(runs);
            Assert.Equal(6, run.Vertices.Count);
            Assert.Equal(3000, run.TotalLengthMm, 6);
        }

        [Fact]
        public void ClosedTriangle_MergesIntoLoopRun()
        {
            var runs = ParallelRunPlanner.Plan(new List<ParallelRunMember>
            {
                Member("A", 1000, P(0, 0), P(1000, 0)),
                Member("B", 1000, P(1000, 0), P(500, 800)),
                Member("C", 1000, P(500, 800), P(0, 0))
            });
            var run = Assert.Single(runs);
            Assert.Equal(3, run.Members.Count);
            Assert.Equal(3000, run.TotalLengthMm, 6);
        }

        [Fact]
        public void Deterministic_StartClusterIsLexicographicallySmallest()
        {
            // 同一路径两次规划（成员输入顺序不同）必须得到同向链（可重跑幂等的前提）。
            List<ParallelRunMember> first = new List<ParallelRunMember>
            {
                Member("A", 1000, P(0, 0), P(1000, 0)),
                Member("B", 1000, P(1000, 0), P(1000, 1000))
            };
            List<ParallelRunMember> second = new List<ParallelRunMember>
            {
                Member("B", 1000, P(1000, 1000), P(1000, 0)),
                Member("A", 1000, P(1000, 0), P(0, 0))
            };
            var run1 = Assert.Single(ParallelRunPlanner.Plan(first));
            var run2 = Assert.Single(ParallelRunPlanner.Plan(second));
            Assert.Equal(run1.Vertices[0].X, run2.Vertices[0].X, 6);
            Assert.Equal(run1.Vertices[0].Y, run2.Vertices[0].Y, 6);
            Assert.Equal(run1.Vertices[run1.Vertices.Count - 1].X,
                run2.Vertices[run2.Vertices.Count - 1].X, 6);
        }

        [Fact]
        public void InvalidMembers_AreIgnored()
        {
            var runs = ParallelRunPlanner.Plan(new List<ParallelRunMember>
            {
                null,
                new ParallelRunMember { Handle = "", Vertices = Points(P(0, 0), P(1, 1)) },
                new ParallelRunMember { Handle = "X" }, // 无顶点
                Member("A", 100, P(0, 0), P(100, 0))
            });
            var run = Assert.Single(runs);
            Assert.Equal("A", run.Members[0].Handle);
        }

        private static ParallelRunMember Member(string handle, double length,
            params QuickLinePoint[] vertices)
            => MemberWithVertices(handle, length, vertices);

        private static ParallelRunMember MemberWithVertices(string handle,
            double length, params QuickLinePoint[] vertices)
            => new ParallelRunMember
            {
                Handle = handle,
                LengthMm = length,
                Vertices = vertices.ToList()
            };

        private static List<QuickLinePoint> Points(params QuickLinePoint[] points)
            => points.ToList();

        private static QuickLinePoint P(double x, double y)
            => new QuickLinePoint(x, y);
    }
}
