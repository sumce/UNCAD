using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.3.0（D-054 特性②）：回路连通性纯分析器——连通分量、孤立段（度数=0）、
    /// 「每段至少一端相接」违规清单与确认窗文案口径；端点容差与 U1LX 同口径。
    /// </summary>
    public sealed class CircuitConnectivityAnalyzerTests
    {
        private static CircuitConnectivityAnalyzer.InputSegment Seg(string id,
            double x1, double y1, double x2, double y2)
            => new CircuitConnectivityAnalyzer.InputSegment(id,
                new QuickLinePoint(x1, y1), new QuickLinePoint(x2, y2));

        [Fact]
        public void Chain_ProducesSingleComponentAndSingleChainText()
        {
            CircuitConnectivityReport report = CircuitConnectivityAnalyzer.Analyze(
                new[]
                {
                    Seg("a", 0, 0, 100, 0),
                    Seg("b", 100, 0, 100, 100),
                    Seg("c", 100, 100, 200, 100)
                });
            Assert.Single(report.Components);
            Assert.Equal(3, report.TotalSegments);
            Assert.Empty(report.IsolatedSegmentIds);
            Assert.True(report.IsSingleChain);
            Assert.Equal("✔ 3 段一条链", report.ToDisplayText());
        }

        [Fact]
        public void IsolatedSegments_AreNamedAsViolations()
        {
            CircuitConnectivityReport report = CircuitConnectivityAnalyzer.Analyze(
                new[]
                {
                    Seg("a", 0, 0, 100, 0),
                    Seg("b", 100, 0, 200, 0),
                    Seg("lonely1", 500, 500, 900, 500),
                    Seg("lonely2", -300, -300, -200, -200)
                });
            // 三个分量：链 a-b + 两个孤立段；孤立段=度数 0，两端都无邻居。
            Assert.Equal(3, report.Components.Count);
            Assert.Equal(2, report.IsolatedSegmentIds.Count);
            Assert.Contains("lonely1", report.IsolatedSegmentIds);
            Assert.Contains("lonely2", report.IsolatedSegmentIds);
            Assert.False(report.IsSingleChain);
            Assert.StartsWith("⚠ 孤立段：", report.ToDisplayText(),
                StringComparison.Ordinal);
            Assert.Contains("lonely1", report.ToDisplayText(), StringComparison.Ordinal);
            Assert.Contains("lonely2", report.ToDisplayText(), StringComparison.Ordinal);
        }

        [Fact]
        public void SingleLoneSegment_CountsAsAChainNotAViolation()
        {
            // 单段路线（一条链的退化情形）是合法路线：✔ 1 段一条链，
            // 不当「孤立段」报警——每个 XLAYOUT 单线图框都长这样。
            CircuitConnectivityReport report = CircuitConnectivityAnalyzer.Analyze(
                new[] { Seg("only", 0, 0, 1000, 0) });
            Assert.Single(report.Components);
            Assert.True(report.IsSingleChain);
            Assert.Equal("✔ 1 段一条链", report.ToDisplayText());
        }

        [Fact]
        public void TwoChainsWithoutIsolation_ReportChainCount()
        {
            CircuitConnectivityReport report = CircuitConnectivityAnalyzer.Analyze(
                new[]
                {
                    Seg("a", 0, 0, 100, 0),
                    Seg("b", 100, 0, 200, 0),
                    Seg("c", 500, 0, 600, 0),
                    Seg("d", 600, 0, 700, 0)
                });
            Assert.Equal(2, report.Components.Count);
            Assert.Empty(report.IsolatedSegmentIds);
            Assert.Equal("4 段 2 条链", report.ToDisplayText());
        }

        [Fact]
        public void EndpointTolerance_FollowsQuickLineGraphConvention()
        {
            // U1LX 口径：端点距离 ≤1.0 视为相接。0.5 的缝隙应当连通；
            // 2.0 的缝隙不连通（孤立段）。
            CircuitConnectivityReport tight = CircuitConnectivityAnalyzer.Analyze(
                new[]
                {
                    Seg("a", 0, 0, 100, 0),
                    Seg("b", 100.5, 0, 200, 0)
                });
            Assert.Single(tight.Components);

            CircuitConnectivityReport loose = CircuitConnectivityAnalyzer.Analyze(
                new[]
                {
                    Seg("a", 0, 0, 100, 0),
                    Seg("b", 102.0, 0, 200, 0)
                });
            Assert.Equal(2, loose.Components.Count);
            Assert.Equal(2, loose.IsolatedSegmentIds.Count);
        }

        [Fact]
        public void EmptyScope_IsDisplayedAsDash()
        {
            CircuitConnectivityReport report = CircuitConnectivityAnalyzer.Analyze(
                new CircuitConnectivityAnalyzer.InputSegment[0]);
            Assert.Equal(0, report.TotalSegments);
            Assert.Equal("—", report.ToDisplayText());
        }

        [Fact]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CircuitConnectivityAnalyzer.Analyze(null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CircuitConnectivityAnalyzer.Analyze(
                    new[] { Seg("a", 0, 0, 1, 0) }, -1.0));
            Assert.Throws<ArgumentException>(() =>
                CircuitConnectivityAnalyzer.Analyze(
                    new[] { Seg("", 0, 0, 1, 0) }));
        }
    }
}
