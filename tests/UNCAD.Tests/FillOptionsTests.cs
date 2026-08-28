using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class FillOptionsTests
    {
        [Fact]
        public void PlanningOptions_ValidateFlexibleMetersAndSelectionPolicy()
        {
            FillPlanningOptions custom = FillPlanningOptions.Create(2.25, true);
            Assert.Equal(2.25, custom.FlexibleConduitMeters);
            Assert.True(custom.IncludeUnmatchedConduitsByDefault);

            Assert.Equal(1.5, FillPlanningOptions.Create(-1, false)
                .FlexibleConduitMeters);
            Assert.Equal(1.5, FillPlanningOptions.Create(double.NaN, false)
                .FlexibleConduitMeters);
            Assert.Equal(1.5, FillPlanningOptions.Create(101, false)
                .FlexibleConduitMeters);
        }

        [Fact]
        public void RuntimeOptions_ClampUnsafePersistedValues()
        {
            FillRuntimeOptions options = FillRuntimeOptions.Create(" source.xlsx ",
                " catalog.xlsx ", -8, 999, -1, double.PositiveInfinity,
                " bridge ", null);

            Assert.Equal("source.xlsx", options.MachineWorkbookPath);
            Assert.Equal("catalog.xlsx", options.CatalogWorkbookPath);
            Assert.Equal(1, options.StartRow);
            Assert.Equal(TableClearPolicy.DefaultRows, options.ClearRows);
            Assert.Equal(TableFillFormatter.DefaultTextHeight, options.TextHeight);
            Assert.Equal(250, options.MmPerGrid);
            Assert.Equal("bridge", options.BridgeInfo);
            Assert.Same(FillPlanningOptions.Default, options.Planning);
        }
    }
}
