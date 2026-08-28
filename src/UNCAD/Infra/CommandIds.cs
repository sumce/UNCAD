using System.Collections.Generic;

namespace UNCAD.Infra
{
    /// <summary>
    /// Public AutoCAD command contract. U1 names are the current interface; the six short
    /// traditional commands remain registered only for established keyboard workflows.
    /// </summary>
    public static class CommandIds
    {
        public const string Fill = "U1F";
        public const string FillUpdate = "U1U";
        public const string Settings = "U1S";
        public const string About = "U1A";
        public const string Conduit = "U1C";
        public const string Tray100 = "U1Q1";
        public const string Tray200 = "U1Q2";
        public const string Tray400 = "U1Q4";
        public const string Line = "U1L";
        public const string Arch = "U1R";

        public const string LegacyLine = "UNL";
        public const string LegacyArch = "UNR";
        public const string LegacyTray100 = "UNQ1";
        public const string LegacyTray200 = "UNQ2";
        public const string LegacyTray400 = "UNQ4";
        public const string LegacyStatistics = "UNADD";

        public const string AboutFeatureCommands = About;
        public const string FillFeatureCommands = Fill + ";" + FillUpdate;
        public const string ConduitFeatureCommands = Conduit;
        public const string TrayFeatureCommands = Tray100 + ";" + Tray200 + ";" + Tray400;
        public const string LineFeatureCommands = Line;
        public const string ArchFeatureCommands = Arch;
        public const string StatisticsFeatureCommands = LegacyStatistics;

        public static IReadOnlyList<string> Canonical { get; } = new[]
        {
            Line, Arch, Tray100, Tray200, Tray400, Fill, FillUpdate, Conduit, About, Settings
        };

        public static IReadOnlyList<string> Legacy { get; } = new[]
        {
            LegacyLine, LegacyArch, LegacyTray100, LegacyTray200,
            LegacyTray400, LegacyStatistics
        };

        public static IReadOnlyList<string> Registered { get; } = new[]
        {
            Line, Arch, Tray100, Tray200, Tray400, Fill, FillUpdate, Conduit, About, Settings,
            LegacyLine, LegacyArch, LegacyTray100, LegacyTray200, LegacyTray400, LegacyStatistics
        };
    }
}
