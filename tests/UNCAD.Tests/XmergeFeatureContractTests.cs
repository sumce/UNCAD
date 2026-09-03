using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XmergeFeatureContractTests
    {
        [Fact]
        public void Xmerge_ExposesCommandGuiAndLayoutImportService()
        {
            string feature = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "XLayout", "XmergeFeature.cs"));
            string service = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "XLayout", "XmergeService.cs"));
            string form = File.ReadAllText(RepoFile("src", "UNCAD", "UI", "XmergeForm.cs"));
            Assert.Contains("CommandMethod(CommandIds.Merge)", feature);
            Assert.Contains("ShowModalDialog", feature);
            Assert.Contains("WblockCloneObjects", service);
            Assert.Contains("XLayoutLayout.Arrange", service);
            Assert.Contains("Task.Run", form);
            Assert.Contains("DwgPathDiscovery.Find", form);
            Assert.Contains("AllowDrop = true", form);
            Assert.Contains("BlockTableRecord.ModelSpace", service);
            Assert.Contains("DuplicateRecordCloning.MangleName", service);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return parts.Length == 0 ? root : Path.Combine(root, Path.Combine(parts));
        }
    }
}
