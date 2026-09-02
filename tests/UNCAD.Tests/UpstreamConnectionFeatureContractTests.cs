using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class UpstreamConnectionFeatureContractTests
    {
        [Fact]
        public void FillAndBatchUpdate_EnsureUpstreamInfoConnections()
        {
            string fill = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "FillFeature.cs"));
            string batch = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "BatchFillUpdateCoordinator.cs"));
            string writer = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "UpstreamConnectionLineWriter.cs"));
            string collector = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "FillSelectionCollector.cs"));

            Assert.Contains("UpstreamConnectionLineWriter.Ensure", fill);
            Assert.Contains("UpstreamConnectionLineWriter.Ensure", batch);
            Assert.Contains("UNCAD_UPSTREAM_LINK", writer);
            Assert.Contains("ReadExistingLines", writer);
            Assert.Contains("SetLinkData", writer);
            Assert.Contains("TryCreatePath", writer);
            Assert.Contains(":stub", writer);
            Assert.Contains(":connector", writer);
            Assert.Contains("FacingPoint", writer);
            Assert.Contains("StartsWith(\"upstream_info\"", collector);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}
