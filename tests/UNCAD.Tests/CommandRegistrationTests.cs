using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class CommandRegistrationTests
    {
        [Fact]
        public void Catalog_ContainsOnlyU1AndRetainedTraditionalCommands()
        {
            Assert.Equal(new[]
            {
                "U1L", "U1LX", "U1DT", "U1R", "U1Q1", "U1Q2", "U1Q4", "U1F", "U1U",
                "U1S", "U1C", "U1A", "U1SET", "U1DATA", "U1DWG", "XLAYOUT", "XSTS", "Xmerge", "U1HELP"
            }, CommandIds.Canonical);
            Assert.Equal(new[] { "U1D" }, CommandIds.Aliases);
            Assert.Equal(new[] { "UNL", "UNLX", "UNR", "UNQ1", "UNQ2", "UNQ4", "UNADD" },
                CommandIds.Legacy);
            Assert.Equal("U1LX;UNLX", CommandIds.QuickLineFeatureCommands);
            Assert.Equal("U1DWG;U1D", CommandIds.DwgExportFeatureCommands);
            Assert.Equal(27, CommandIds.Registered.Count);
            Assert.Equal(27, CommandIds.Registered.Distinct(
                System.StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotContain(CommandIds.Registered, command =>
                command.StartsWith("UNC_", System.StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(CommandIds.Registered, command =>
                command.StartsWith("OPUN", System.StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("UNADDX", CommandIds.Registered);
        }

        [Fact]
        public void UnlxAlias_IsRegisteredAndForwardsToCanonicalQuickLineHandler()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Unl", "QuickLineAliasCommand.cs"));

            Assert.Contains("[CommandMethod(CommandIds.LegacyLineQuick)]", source);
            Assert.Contains("new QuickLineFeature().QuickLine()", source);
        }

        [Fact]
        public void U1D_IsRegisteredOnlyAsTheU1DwgShortcut()
        {
            string export = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "DwgExport", "DwgExportFeature.cs"));
            string dimension = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Dimension", "DimensionTextFeature.cs"));

            Assert.Contains("[CommandMethod(CommandIds.DwgExportShort", export);
            Assert.Contains("public void ExportDwgShort() => Run()", export);
            Assert.Contains("[CommandMethod(CommandIds.DimensionText", dimension);
            Assert.DoesNotContain("CommandIds.DwgExportShort", dimension);
        }

        [Fact]
        public void SourceCommandMethods_RegisterEachPublicCommandExactlyOnce()
        {
            string features = RepoFile("src", "UNCAD", "Features");
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(features, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                foreach (Match match in Regex.Matches(source,
                    @"\[CommandMethod\(CommandIds\.([A-Za-z0-9_]+)"))
                {
                    string constant = match.Groups[1].Value;
                    counts[constant] = counts.TryGetValue(constant, out int count)
                        ? count + 1 : 1;
                }
            }

            string[] registeredConstants =
            {
                "Line", "LineQuick", "DimensionText", "Arch", "Tray100", "Tray200", "Tray400",
                "Fill", "FillUpdate", "Submit", "Conduit", "About", "Settings", "DataRefresh",
                "DwgExport", "DwgExportShort", "XLayout", "Statistics", "Merge", "Help", "LegacyLine", "LegacyLineQuick",
                "LegacyArch", "LegacyTray100", "LegacyTray200", "LegacyTray400",
                "LegacyStatistics"
            };
            Assert.Equal(registeredConstants.Length, counts.Count);
            foreach (string constant in registeredConstants)
                Assert.True(counts.TryGetValue(constant, out int count) && count == 1,
                    "CommandIds." + constant + " must have exactly one [CommandMethod] registration.");
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return parts.Aggregate(root, Path.Combine);
        }
    }
}
