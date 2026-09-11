using UNCAD.Core.Fill;

namespace UNCAD.Infra
{
    public static class FillSettings
    {
        public static FillRuntimeOptions Current()
        {
            FillAutoFillOptions defaults = FillAutoFillOptions.Default;
            FillPlanningOptions planning = FillPlanningOptions.Create(
                Settings.GetDouble(ConfigKeys.FillFlexibleConduitMeters,
                    FillPlanningOptions.DefaultFlexibleConduitMeters), false);
            FillAutoFillOptions autoFill = FillAutoFillOptions.Create(
                Settings.GetBool(ConfigKeys.FillAutofillCable, defaults.Cable),
                Settings.GetBool(ConfigKeys.FillAutofillBreaker, defaults.Breaker),
                Settings.GetBool(ConfigKeys.FillAutofillPanel, defaults.Panel),
                Settings.GetBool(ConfigKeys.FillAutofillFlexibleConduit,
                    defaults.FlexibleConduit),
                Settings.GetBool(ConfigKeys.FillAutofillRigidConduit,
                    defaults.RigidConduit),
                Settings.GetBool(ConfigKeys.FillAutofillBridge, defaults.Bridge),
                Settings.GetBool(ConfigKeys.FillAutofillOutletPanel,
                    defaults.OutletPanel),
                Settings.GetBool(ConfigKeys.FillAutofillBusPlugBox,
                    defaults.BusPlugBox));
            // 机台/设备表是用户唯一需要提供的 Excel；固定清单随插件内嵌发布。
            return FillRuntimeOptions.Create(
                Settings.Get(ConfigKeys.FillExcelPath, ""),
                SafeInt(Settings.GetDouble(ConfigKeys.FillTableRow,
                    FillRuntimeOptions.DefaultStartRow), FillRuntimeOptions.DefaultStartRow),
                SafeInt(Settings.GetDouble(ConfigKeys.FillClearRows,
                    TableClearPolicy.DefaultRows), TableClearPolicy.DefaultRows),
                Settings.GetDouble(ConfigKeys.FillTextHeight,
                    TableFillFormatter.DefaultTextHeight),
                Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0),
                Settings.Get(ConfigKeys.FillBridge, ""), planning, autoFill);
        }

        private static int SafeInt(double value, int fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)
                || value < int.MinValue || value > int.MaxValue) return fallback;
            return (int)value;
        }
    }
}
