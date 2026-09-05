using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FillFeatureWorkbookSourceContractTests
    {
        [Fact]
        public void RemoteWorkbookResolution_UsesManualCacheOnly()
        {
            string source = File.ReadAllText(PathOf("src", "UNCAD", "Features",
                "Fill", "FillFeature.cs"));

            Assert.Contains("TryGetSnapshot(path", source);
            Assert.Contains("SQLite", source);
            Assert.DoesNotContain("TryGetCachedPath(path", source);
            Assert.DoesNotContain("MachineWorkbookSource.Refresh(path)", source);
        }

        [Fact]
        public void Xsts_RemoteWorkbookResolution_UsesManualCacheOnly()
        {
            string source = File.ReadAllText(PathOf("src", "UNCAD", "Features",
                "Stat", "XstsFeature.cs"));

            Assert.Contains("TryGetSnapshot(configuredPath", source);
            Assert.Contains("SQLite", source);
            Assert.DoesNotContain("TryGetCachedPath(configuredPath", source);
            Assert.DoesNotContain("MachineWorkbookSource.Refresh", source);
        }

        [Fact]
        public void CommandPaths_NeverParseTheSourceWorkbookDirectly()
        {
            string[] files =
            {
                PathOf("src", "UNCAD", "Features", "Fill", "FillFeature.cs"),
                PathOf("src", "UNCAD", "Features", "Fill", "BatchFillUpdateCoordinator.cs"),
                PathOf("src", "UNCAD", "Features", "Stat", "XstsFeature.cs"),
                PathOf("src", "UNCAD", "Features", "XLayout", "XLayoutFeature.cs")
            };
            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                Assert.DoesNotContain("ExcelMachineReader.ReadRows", source);
                Assert.DoesNotContain("MachineWorkbookSource.Refresh", source);
            }
        }

        private static string PathOf(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
    }
}
