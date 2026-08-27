using UNCAD.Core.Stat;

namespace UNCAD.Infra
{
    public sealed class StatisticsSettingsSnapshot
    {
        public bool IncludeText { get; set; }
        public bool IncludeMText { get; set; }
        public StatCalculationOptions Calculation { get; set; }

        public string SelectionFilter
        {
            get
            {
                if (IncludeText && IncludeMText) return "TEXT,MTEXT";
                if (IncludeText) return "TEXT";
                if (IncludeMText) return "MTEXT";
                return "";
            }
        }
    }

    public static class StatisticsSettings
    {
        public static StatisticsSettingsSnapshot Current()
        {
            return new StatisticsSettingsSnapshot
            {
                IncludeText = Settings.GetBool(ConfigKeys.UnaddTextEnabled, true),
                IncludeMText = Settings.GetBool(ConfigKeys.UnaddMTextEnabled, true),
                Calculation = new StatCalculationOptions
                {
                    MmPerGrid = Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0),
                    IncludeCable = Settings.GetBool(ConfigKeys.UnaddCableEnabled, true),
                    IncludeBridge = Settings.GetBool(ConfigKeys.UnaddBridgeEnabled, true),
                    IncludeConduit = Settings.GetBool(ConfigKeys.UnaddConduitEnabled, true)
                }
            };
        }
    }
}
