using UNCAD.Core.Fill;
using UNCAD.Core.Excel;
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
            Assert.False(custom.IncludeUnmatchedConduitsByDefault);

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
                -8, 999, -1, double.PositiveInfinity, " bridge ", null);

            // 用户唯一提供的文件就是机台/设备表；不再存在清单路径配置。
            Assert.Equal("source.xlsx", options.MachineWorkbookPath);
            Assert.Equal(1, options.StartRow);
            Assert.Equal(TableClearPolicy.DefaultRows, options.ClearRows);
            Assert.Equal(TableFillFormatter.DefaultTextHeight, options.TextHeight);
            Assert.Equal(250, options.MmPerGrid);
            Assert.Equal("bridge", options.BridgeInfo);
            Assert.Same(FillPlanningOptions.Default, options.Planning);
        }
    }
}
