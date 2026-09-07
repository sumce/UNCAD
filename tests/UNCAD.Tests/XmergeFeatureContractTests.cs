using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XmergeFeatureContractTests
    {
        [Theory]
        [InlineData("frame")]
        [InlineData("frame_20260812")]
        [InlineData("xframe")]
        [InlineData("$12$xframe")]
        [InlineData("xframe$12$")]
        [InlineData("$12$xframe$3$")]
        public void Xmerge_AcceptsCurrentLegacyAndPreviouslyMangledFrames(string name)
            => Assert.True(UNCAD.Features.XLayout.XmergeService.IsSourceFrameName(name));

        [Theory]
        [InlineData("frameinfo_json")]
        [InlineData("$12$other")]
        public void Xmerge_RejectsUnrelatedBlockNames(string name)
            => Assert.False(UNCAD.Features.XLayout.XmergeService.IsSourceFrameName(name));

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
            Assert.Contains("ctx.Ed.Regen()", feature);
            Assert.Contains("WblockCloneObjects", service);
            Assert.Contains("XLayoutLayout.Arrange", service);
            Assert.Contains("Task.Run", form);
            Assert.Contains("DwgPathDiscovery.Find", form);
            Assert.Contains("AllowDrop = true", form);
            Assert.Contains("BlockTableRecord.ModelSpace", service);
            Assert.Contains("new Database(true, true)", service);
            Assert.Contains("FrameRegionCollector.CollectForLayout(", service);
            Assert.Contains("FrameIdentityReader.Read(transaction, group", service);
            Assert.Contains("DuplicateRecordCloning.Ignore", service);
            Assert.Contains("DuplicateRecordCloning.Replace", service);
            Assert.DoesNotContain("DuplicateRecordCloning.MangleName", service);
            Assert.Contains("ValidateClones", service);
            Assert.Contains("NormalizeInMemoryBlockNames", service);
            Assert.Contains("prepared.Database.WblockCloneObjects", service);
            Assert.Contains("AssignRemainingEntities", service);
            Assert.Contains("RecomputeClonedTables", service);
            Assert.Contains("已按源坐标导入", feature);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return parts.Length == 0 ? root : Path.Combine(root, Path.Combine(parts));
        }
    }
}
