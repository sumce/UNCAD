using System.Collections.Generic;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public class SummationModuleTests
    {
        [Fact]
        public void Execute_ReportsSourceAndCategoryMatchCounts()
        {
            var output = SummationModule.Execute(new SummationRequest(new[]
            {
                "2500mm",
                "桥架200*100 2格",
                "⌀25线管 1500mm",
                "(共用)桥架200*100 12格"
            }, new StatCalculationOptions { MmPerGrid = 250 }));

            Assert.Equal("SUM-STAT/求和统计", SummationModule.Descriptor.Label);
            Assert.Equal(4, output.SourceLineCount);
            Assert.Equal(1, output.CableMatchCount);
            Assert.Equal(1, output.BridgeMatchCount);
            Assert.Equal(1, output.ConduitMatchCount);
            Assert.Equal(3, output.TotalMatchCount);
            Assert.Equal(2.5, output.Statistics.CableSum);
            Assert.Equal(0.5, Assert.Single(output.Statistics.Bridges).TotalM);
            Assert.Equal(1.5, Assert.Single(output.Statistics.Conduits).TotalM);
        }

        [Fact]
        public void Request_SnapshotsLinesAndOptionsAtModuleBoundary()
        {
            var lines = new List<string> { "1000mm" };
            var options = new StatCalculationOptions { IncludeCable = true };
            var request = new SummationRequest(lines, options);
            lines[0] = "9000mm";
            options.IncludeCable = false;

            SummationOutput output = SummationModule.Execute(request);

            Assert.Equal(1, output.Statistics.CableSum);
            Assert.Equal(1, output.CableMatchCount);
        }

        [Fact]
        public void MeasurementState_DistinguishesIncompleteAndConfirmedEmptyScopes()
        {
            CableStatResult statistics = SummationModule.Execute(new SummationRequest(
                new[] { "普通说明文字" }, new StatCalculationOptions
                {
                    IncludeCable = true,
                    IncludeBridge = true,
                    IncludeConduit = false
                })).Statistics;

            Assert.Equal(MeasurementState.Unknown, statistics.CableState);
            Assert.Equal(MeasurementState.Unknown, statistics.BridgeState);
            statistics.ApplySourceCoverage(true);
            Assert.Equal(MeasurementState.ConfirmedEmpty, statistics.CableState);
            Assert.Equal(MeasurementState.ConfirmedEmpty, statistics.BridgeState);
            Assert.Equal(MeasurementState.Unknown, statistics.ConduitState);
        }

        [Fact]
        public void MeasurementState_PreservesMeasuredCategoryInCompleteScope()
        {
            CableStatResult statistics = StatCalculator.Calculate(
                new[] { "2500mm" }, new StatCalculationOptions());

            statistics.ApplySourceCoverage(true);

            Assert.Equal(MeasurementState.Measured, statistics.CableState);
            Assert.Equal(MeasurementState.ConfirmedEmpty, statistics.BridgeState);
            Assert.Equal(MeasurementState.ConfirmedEmpty, statistics.ConduitState);
        }
    }
}
