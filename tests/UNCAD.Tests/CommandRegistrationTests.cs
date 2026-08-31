using System;
using System.IO;
using System.Linq;
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
                "U1L", "U1LX", "U1R", "U1Q1", "U1Q2", "U1Q4", "U1F", "U1U",
                "U1S", "U1C", "U1A", "U1SET", "U1DWG", "XLAYOUT", "U1HELP"
            }, CommandIds.Canonical);
            Assert.Equal(new[] { "UNL", "UNLX", "UNR", "UNQ1", "UNQ2", "UNQ4", "UNADD" },
                CommandIds.Legacy);
            Assert.Equal("U1LX;UNLX", CommandIds.QuickLineFeatureCommands);
            Assert.Equal(22, CommandIds.Registered.Count);
            Assert.Equal(22, CommandIds.Registered.Distinct(
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

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return parts.Aggregate(root, Path.Combine);
        }
    }
}
