using System;
using System.Collections.Generic;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-050 解析式偏移构建器：用户报告"桥架红线与基线交叉"（旧路径
    /// GetOffsetCurves 不校验翻侧）。本套测试锁定：正常拐角 miter 精确、
    /// 急弯外角 bevel、病态短段整体拒绝（调用方退回逐段偏移）、任何成功
    /// 结果全体顶点/中点都严格在偏移侧——交叉从构造上不可能。
    /// </summary>
    public sealed class OffsetPolylineBuilderTests
    {
        private const double Off = 15.0;

        [Fact]
        public void SingleSegment_BuildsExactParallel()
        {
            var chain = new List<QuickLinePoint>
            {
                new QuickLinePoint(0, 0), new QuickLinePoint(1000, 0)
            };
            bool ok = OffsetPolylineBuilder.TryBuild(chain, Off, true,
                out List<QuickLinePoint> result);
            Assert.True(ok);
            Assert.Equal(2, result.Count);
            // 左手侧（+X 走向 → +Y）：两端各上移 Off。
            AssertPoint(result[0], 0, Off);
            AssertPoint(result[1], 1000, Off);
            AssertAllOnOffsetSide(chain, result, Off, +1);
        }

        [Fact]
        public void RightAngleOuterCorner_MitersExactly()
        {
            // 右转 L（行进 +X 后 -Y）：行进者左手 = +Y 后转 +X，即拐角外侧。
            var chain = new List<QuickLinePoint>
            {
                new QuickLinePoint(0, 0), new QuickLinePoint(1000, 0),
                new QuickLinePoint(1000, -1000)
            };
            bool ok = OffsetPolylineBuilder.TryBuild(chain, Off, true,
                out List<QuickLinePoint> result);
            Assert.True(ok);
            Assert.Equal(3, result.Count); // 起点 + miter + 终点
            AssertPoint(result[1], 1000 + Off, Off); // 外角斜接点（东北）
            AssertAllOnOffsetSide(chain, result, Off, +1);
        }

        [Fact]
        public void RightAngleInnerCorner_MitersExactly()
        {
            // 左转 L（行进 +X 后 +Y），左手侧 = 内角侧。
            var chain = new List<QuickLinePoint>
            {
                new QuickLinePoint(0, 0), new QuickLinePoint(1000, 0),
                new QuickLinePoint(1000, 1000)
            };
            bool ok = OffsetPolylineBuilder.TryBuild(chain, Off, true,
                out List<QuickLinePoint> result);
            Assert.True(ok);
            Assert.Equal(3, result.Count);
            AssertPoint(result[1], 1000 - Off, Off); // 内角斜接点
            AssertAllOnOffsetSide(chain, result, Off, +1);
        }

        [Fact]
        public void CollinearChain_StaysStraight()
        {
            var chain = new List<QuickLinePoint>
            {
                new QuickLinePoint(0, 0), new QuickLinePoint(500, 0),
                new QuickLinePoint(1000, 0)
            };
            bool ok = OffsetPolylineBuilder.TryBuild(chain, Off, false,
                out List<QuickLinePoint> result);
            Assert.True(ok);
            // 共线拐角共享端点：三点共线（中间点合并且仍在线上）。
            Assert.True(result.Count == 2 || result.Count == 3);
            foreach (QuickLinePoint point in result)
                Assert.Equal(-Off, point.Y, 6); // 右手侧（-Y）整体平移
            AssertAllOnOffsetSide(chain, result, Off, -1);
        }

        [Fact]
        public void HairpinTurn_BevelsInsteadOfMiter()
        {
            // 掉头急拐（方向变化 ≈163°，内角 ≈17°）：行进者左手在楔角外侧，
            // miter 腿 = Off/sin(α/2) ≈ 103 >> 4×Off，必须 bevel，不得射出长刺。
            var chain = new List<QuickLinePoint>
            {
                new QuickLinePoint(0, 0), new QuickLinePoint(1000, 0),
                new QuickLinePoint(900, -30)
            };
            bool ok = OffsetPolylineBuilder.TryBuild(chain, Off, true,
                out List<QuickLinePoint> result);
            Assert.True(ok);
            // bevel = 中间两个端点（比 miter 的 1 个点多一个）。
            Assert.Equal(4, result.Count);
            // 无超长斜接刺：合法偏移点到整条链的距离 ≈ Off；长刺会远离全链。
            foreach (QuickLinePoint point in result)
            {
                (double distance, _, _) = Nearest(chain, point);
                Assert.True(distance <= OffsetPolylineBuilder.MiterLimitFactor * Off,
                    "bevel 后仍存在超长斜接腿: " + point + " 距链 " + distance);
            }
            AssertAllOnOffsetSide(chain, result, Off, +1);
        }

        [Fact]
        public void ShortSegmentInnerCorner_IsRejected_NotCrossed()
        {
            // 病态：段长 2 << 偏移 15 的内角——旧行为的交叉高发区。新构建器
            // 必须整体拒绝（false），由调用方退回逐段独立偏移，绝不输出翻侧折线。
            var chain = new List<QuickLinePoint>
            {
                new QuickLinePoint(0, 0), new QuickLinePoint(2, 0),
                new QuickLinePoint(2, 1000)
            };
            Assert.False(OffsetPolylineBuilder.TryBuild(chain, Off, true, out _));
        }

        [Fact]
        public void ZigZag_BuildsSinglePolyline_AllSidesValid()
        {
            // 现实桥架路径：多拐连续链。
            var chain = new List<QuickLinePoint>
            {
                new QuickLinePoint(0, 0), new QuickLinePoint(3000, 0),
                new QuickLinePoint(3000, 2000), new QuickLinePoint(6000, 2000),
                new QuickLinePoint(6000, 0), new QuickLinePoint(9000, 0)
            };
            bool ok = OffsetPolylineBuilder.TryBuild(chain, Off, true,
                out List<QuickLinePoint> result);
            Assert.True(ok);
            Assert.Equal(chain.Count, result.Count); // 全部 90° 拐角都能 miter
            AssertAllOnOffsetSide(chain, result, Off, +1);
        }

        [Fact]
        public void InvalidInput_ReturnsFalse()
        {
            Assert.False(OffsetPolylineBuilder.TryBuild(null, Off, true, out _));
            Assert.False(OffsetPolylineBuilder.TryBuild(
                new List<QuickLinePoint>(), Off, true, out _));
            Assert.False(OffsetPolylineBuilder.TryBuild(
                new List<QuickLinePoint> { new QuickLinePoint(0, 0) }, Off, true,
                out _));
            Assert.False(OffsetPolylineBuilder.TryBuild(
                new List<QuickLinePoint> { new QuickLinePoint(0, 0),
                    new QuickLinePoint(1000, 0) }, 0, true, out _));
        }

        /// <summary>成功结果的全量侧向校验（独立于实现重算，防自证）。</summary>
        private static void AssertAllOnOffsetSide(List<QuickLinePoint> chain,
            List<QuickLinePoint> result, double offset, double sign)
        {
            var samples = new List<QuickLinePoint>(result);
            for (int i = 0; i < result.Count - 1; i++)
                samples.Add(new QuickLinePoint(
                    (result[i].X + result[i + 1].X) / 2,
                    (result[i].Y + result[i + 1].Y) / 2));
            foreach (QuickLinePoint point in samples)
            {
                (double distance, int segment, bool interior) =
                    Nearest(chain, point);
                if (!interior) continue;
                Assert.True(distance >= offset * 0.85,
                    "点贴回/穿越基线: " + point + " 距离 " + distance);
                double cross = (chain[segment + 1].X - chain[segment].X)
                    * (point.Y - chain[segment].Y)
                    - (chain[segment + 1].Y - chain[segment].Y)
                    * (point.X - chain[segment].X);
                Assert.True(Math.Sign(cross) == Math.Sign(sign),
                    "点翻到错误侧: " + point + " cross=" + cross);
            }
        }

        private static (double, int, bool) Nearest(List<QuickLinePoint> chain,
            QuickLinePoint point)
        {
            double best = double.PositiveInfinity;
            int bestSegment = -1;
            bool bestInterior = false;
            for (int i = 0; i < chain.Count - 1; i++)
            {
                double dx = chain[i + 1].X - chain[i].X;
                double dy = chain[i + 1].Y - chain[i].Y;
                double t = ((point.X - chain[i].X) * dx + (point.Y - chain[i].Y) * dy)
                    / (dx * dx + dy * dy);
                double c = Math.Max(0, Math.Min(1, t));
                double px = chain[i].X + dx * c, py = chain[i].Y + dy * c;
                double d = Math.Sqrt((point.X - px) * (point.X - px)
                    + (point.Y - py) * (point.Y - py));
                if (d < best)
                {
                    best = d;
                    bestSegment = i;
                    bestInterior = c > 1e-9 && c < 1 - 1e-9;
                }
            }
            return (best, bestSegment, bestInterior);
        }

        private static void AssertPoint(QuickLinePoint actual, double x, double y)
        {
            Assert.Equal(x, actual.X, 6);
            Assert.Equal(y, actual.Y, 6);
        }
    }
}
