using System.Collections.Generic;

namespace UNCAD.Infra
{
    /// <summary>UNCAD command names shared by attributes, Ribbon definitions, and tests.</summary>
    public static class CommandIds
    {
        public const string Fill = "UNC_FILL";
        public const string FillUpdate = "UNC_FILL_UPDATE";
        public const string Submit = "UNC_SUBMIT";
        public const string Settings = "UNC_SET";
        public const string About = "UNC_ABOUT";
        public const string Ribbon = "UNC_RIBBON";

        public const string Conduit = "UNC_CONDUIT";
        public const string Conduit20 = "UNC_CONDUIT20";
        public const string Conduit25 = "UNC_CONDUIT25";
        public const string Conduit32 = "UNC_CONDUIT32";
        public const string ConduitSettings = "UNC_CONDUIT_SET";

        public const string Tray = "UNC_TRAY";
        public const string Tray100 = "UNC_TRAY100";
        public const string Tray200 = "UNC_TRAY200";
        public const string Tray400 = "UNC_TRAY400";
        public const string TraySettings = "UNC_TRAY_SET";

        public const string Line = "UNC_LINE";
        public const string LineSettings = "UNC_LINE_SET";
        public const string Arch = "UNC_ARCH";
        public const string ArchSettings = "UNC_ARCH_SET";
        public const string Statistics = "UNC_STAT";
        public const string StatisticsExcel = "UNC_STAT_EX";

        public const string LegacyLine = "UNL";
        public const string LegacyLineSettings = "OPUNL";
        public const string LegacyTray100 = "UNQ1";
        public const string LegacyTray200 = "UNQ2";
        public const string LegacyTray400 = "UNQ4";
        public const string LegacyTraySettings = "OPUNQ";
        public const string LegacyStatistics = "UNADD";
        public const string LegacyStatisticsExcel = "UNADDX";
        public const string LegacyArch = "UNR";
        public const string LegacyArchSettings = "OPUNR";

        public const string AboutFeatureCommands = About + ";" + Ribbon;
        public const string FillFeatureCommands = Fill + ";" + FillUpdate;
        public const string ConduitFeatureCommands = Conduit + ";" + Conduit20 + ";"
            + Conduit25 + ";" + Conduit32 + ";" + ConduitSettings;
        public const string TrayFeatureCommands = Tray + ";" + Tray100 + ";"
            + Tray200 + ";" + Tray400 + ";" + TraySettings;
        public const string LineFeatureCommands = Line + ";" + LineSettings;
        public const string ArchFeatureCommands = Arch + ";" + ArchSettings;
        public const string StatisticsFeatureCommands = Statistics + ";" + StatisticsExcel;

        public static IReadOnlyList<string> Canonical { get; } = new[]
        {
            Fill, FillUpdate, Submit, Settings, About, Ribbon, Conduit, Conduit20, Conduit25, Conduit32,
            ConduitSettings, Tray, Tray100, Tray200, Tray400, TraySettings, Line,
            LineSettings, Arch, ArchSettings, Statistics, StatisticsExcel
        };
    }
}
