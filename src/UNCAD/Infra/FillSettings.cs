using UNCAD.Core.Fill;

namespace UNCAD.Infra
{
    public static class FillSettings
    {
        public static FillRuntimeOptions Current()
        {
            FillPlanningOptions planning = FillPlanningOptions.Create(
                Settings.GetDouble(ConfigKeys.FillFlexibleConduitMeters,
                    FillPlanningOptions.DefaultFlexibleConduitMeters),
                Settings.GetBool(ConfigKeys.FillIncludeUnmatchedConduits, false));
            return FillRuntimeOptions.Create(
                Settings.Get(ConfigKeys.FillExcelPath, ""),
                Settings.Get(ConfigKeys.FillCatalogPath, ""),
                SafeInt(Settings.GetDouble(ConfigKeys.FillTableRow,
                    FillRuntimeOptions.DefaultStartRow), FillRuntimeOptions.DefaultStartRow),
                SafeInt(Settings.GetDouble(ConfigKeys.FillClearRows,
                    TableClearPolicy.DefaultRows), TableClearPolicy.DefaultRows),
                Settings.GetDouble(ConfigKeys.FillTextHeight,
                    TableFillFormatter.DefaultTextHeight),
                Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0),
                Settings.Get(ConfigKeys.FillBridge, ""), planning);
        }

        private static int SafeInt(double value, int fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)
                || value < int.MinValue || value > int.MaxValue) return fallback;
            return (int)value;
        }
    }
}
