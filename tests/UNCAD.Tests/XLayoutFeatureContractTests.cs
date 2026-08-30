using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public class XLayoutFeatureContractTests
    {
        [Fact]
        public void Feature_UsesSharedFrameCollectionAndRunsBeforeSummary()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "XLayout", "XLayoutFeature.cs"));
            Assert.Contains("CommandMethod(CommandIds.XLayout", source);
            Assert.Contains("CollectForLayout", source);
            Assert.Contains("FrameIdentityReader.Read", source);
            Assert.Contains("XLayoutLayout.Arrange", source);
            Assert.Contains("transaction.Commit();", source);
            Assert.Contains("XLayoutSummaryForm", source);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}
