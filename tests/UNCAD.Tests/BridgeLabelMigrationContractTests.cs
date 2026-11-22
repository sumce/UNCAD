using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public class BridgeLabelMigrationContractTests
    {
        [Fact]
        public void MigrationWriterReplacesLegacyDbTextWithBoqMText()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "BridgeLabelMigrationWriter.cs"));

            Assert.Contains("AnnotationLabelPair.TryUpgradeBridgeLabel", source);
            Assert.Contains("var replacement = new MText()", source);
            Assert.Contains("source.Erase()", source);
        }

        [Fact]
        public void U1FAndU1U_DoNotMigrateLegacyBridgeLabels()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "FillFeature.cs"));
            Assert.DoesNotContain("BridgeLabelMigrationWriter.Migrate(", source);
            Assert.DoesNotContain("migratedBridgeLabels", source);
        }

        [Fact]
        public void BatchU1U_DoesNotMigrateLegacyBridgeLabels()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "BatchFillUpdateCoordinator.cs"));
            Assert.DoesNotContain("BridgeLabelMigrationWriter.Migrate(", source);
            Assert.DoesNotContain("migratedBridgeLabels", source);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}
