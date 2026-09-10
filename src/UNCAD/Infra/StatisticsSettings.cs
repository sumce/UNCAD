using UNCAD.Core.Stat;

namespace UNCAD.Infra
{
    public sealed class StatisticsSettingsSnapshot
    {
        public bool IncludeText { get; set; }
        public bool IncludeMText { get; set; }
        public bool IncludeDimension { get; set; }
        public StatCalculationOptions Calculation { get; set; }

        /// <summary>
        /// 文字来源开关对应的 DXF 选择过滤值。对齐/转角标注的人工文字覆盖由
        /// IncludeDimension 独立开关控制。
        /// </summary>
        public string SelectionFilter
        {
            get
            {
                string dimension = IncludeDimension ? ",DIMENSION" : "";
                if (IncludeText && IncludeMText) return "TEXT,MTEXT" + dimension;
                if (IncludeText) return "TEXT" + dimension;
                if (IncludeMText) return "MTEXT" + dimension;
                if (IncludeDimension) return "DIMENSION";
                return "";
            }
        }
    }

    public static class StatisticsSettings
    {
        internal const bool DefaultIncludeMText = false;
        internal const bool DefaultIncludeDimension = true;

        public static StatisticsSettingsSnapshot Current()
        {
            return new StatisticsSettingsSnapshot
            {
                IncludeText = Settings.GetBool(ConfigKeys.UnaddTextEnabled, true),
                IncludeMText = Settings.GetBool(ConfigKeys.UnaddMTextEnabled,
                    DefaultIncludeMText),
                IncludeDimension = Settings.GetBool(ConfigKeys.UnaddDimensionEnabled,
                    DefaultIncludeDimension),
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
