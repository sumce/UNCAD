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
            // 3.0 WPF 窗体（D-038）：扫描仍在窗体后台线程，拖放/确认走 Fluent 窗体。
            string window = File.ReadAllText(RepoFile("src", "UNCAD", "UI", "Wpf",
                "XmergeWindow.xaml.cs"));
            string featureXaml = File.ReadAllText(RepoFile("src", "UNCAD", "UI", "Wpf",
                "XmergeWindow.xaml"));
            Assert.Contains("CommandMethod(CommandIds.Merge)", feature);
            Assert.Contains("ShowModalWindow", feature);
            Assert.Contains("ctx.Ed.Regen()", feature);
            Assert.Contains("WblockCloneObjects", service);
            Assert.Contains("XLayoutLayout.Arrange", service);
            Assert.Contains("Task.Run", window);
            Assert.Contains("DwgPathDiscovery.Find", window);
            Assert.Contains("AllowDrop = true", window);
            Assert.DoesNotContain("XmergeForm", window);
            Assert.Contains("XmergeWindow", feature);
            Assert.Contains("FluentWindow", featureXaml);
            Assert.Contains("BlockTableRecord.ModelSpace", service);
            Assert.Contains("new Database(true, true)", service);
            Assert.Contains("FrameRegionCollector.CollectForLayout(", service);
            Assert.Contains("FrameIdentityReader.Read(transaction, group", service);
            Assert.Contains("DuplicateRecordCloning.Ignore", service);
            Assert.Contains("DuplicateRecordCloning.Replace", service);
            Assert.Contains("DuplicateRecordCloning.MangleName", service);
            Assert.Contains(
                "policy != DuplicateRecordCloning.MangleName", service);
            // The service exposes a Database entry point so the Replace/Ignore branch can
            // be exercised without a running AutoCAD Document, and the CadContext overload
            // delegates to it instead of duplicating the merge logic.
            Assert.Contains("public static XmergeResult Merge(Database database,", service);
            Assert.Contains("return Merge(ctx.Db, filePaths)", service);
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
