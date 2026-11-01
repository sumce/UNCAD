using System;
using System.IO;
using UNCAD.Cad;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FrameRegionCollectorTests
    {
        [Theory]
        [InlineData("frame")]
        [InlineData("frame_20260812")]
        [InlineData("xframe")]
        [InlineData("xframe_v2")]
        [InlineData("xframe$0$")]
        [InlineData("xframe_v2$0$")]
        [InlineData("XFRAME_V2$12$$3$")]
        [InlineData("FRAME_20260812$12$$3$")]
        public void SupportedFrameNamesIncludeLegacyAndCurrentDefinitions(string name)
        {
            Assert.True(FrameRegionCollector.IsSupportedFrameName(name));
        }

        [Theory]
        [InlineData("")]
        [InlineData("*U42")]
        [InlineData("frameinfo_json")]
        [InlineData("xframe_backup")]
        [InlineData("xframe_v2_backup")]
        [InlineData("xframe_v3")]
        public void UnsupportedFrameNamesAreNotAccepted(string name)
        {
            Assert.False(FrameRegionCollector.IsSupportedFrameName(name));
        }

        [Fact]
        public void LegacyFrameClassificationExcludesCurrentXframe()
        {
            Assert.True(FrameRegionCollector.IsLegacyFrameName("frame"));
            Assert.True(FrameRegionCollector.IsLegacyFrameName("frame_20260812"));
            Assert.True(FrameRegionCollector.IsLegacyFrameName("xframe"));
            Assert.True(FrameRegionCollector.IsLegacyFrameName("xframe$0$"));
            Assert.False(FrameRegionCollector.IsLegacyFrameName("xframe_v2"));
            Assert.False(FrameRegionCollector.IsLegacyFrameName("XFRAME_V2$12$$3$"));
            Assert.False(FrameRegionCollector.IsLegacyFrameName("frameinfo_json"));
        }

        [Fact]
        public void CurrentTemplateBlockIsXFrameV2()
        {
            Assert.Equal("xframe_v2", FrameRegionCollector.CurrentFrameName);
            Assert.Equal("frame_20260812/frame/xframe/xframe_v2",
                FrameRegionCollector.SupportedFrameDescription);
        }

        [Fact]
        public void MigrationServiceUsesCurrentTemplate()
        {
            string source = ReadSource("src", "UNCAD", "Features", "Fill",
                "XFrameMigrationService.cs");

            Assert.Contains("FrameRegionCollector.CurrentFrameName", source);
            Assert.DoesNotContain("FrameRegionCollector.XFrameName", source);
            Assert.Contains("新版图框模板中缺少 xframe_v2 定义", source);
            Assert.Contains("无法导入 xframe_v2 图框定义", source);
        }

        [Fact]
        public void MigrationServiceRemovesAttributesMissingFromNewDefinition()
        {
            string source = ReadSource("src", "UNCAD", "Features", "Fill",
                "XFrameMigrationService.cs");

            Assert.Contains("definitionTags.Contains(stale.Key)", source);
            Assert.Contains("stale.Value.Erase()", source);
            Assert.DoesNotContain("Tag = \"FRAMEINFO_JSON\"", source);
            Assert.DoesNotContain("Tag = \"BRIDGE_FRAME_INFO\"", source);
            Assert.DoesNotContain("Tag = \"CONDUIT_INFO\"", source);
        }

        [Fact]
        public void HelpTextListsXFrameV2AsSupportedFrame()
        {
            CommandHelpEntry submit = CommandHelpCatalog.Find("U1S");
            CommandHelpEntry export = CommandHelpCatalog.Find("U1DWG");

            Assert.NotNull(submit);
            Assert.NotNull(export);
            Assert.Contains("xframe_v2", submit.Notes);
            Assert.Contains("xframe_v2", export.Function);
        }

        [Fact]
        public void AnnotationOwnership_UsesTextAnchorBeforeGeometricExtents()
        {
            string source = ReadSource("src", "UNCAD", "Cad", "FrameRegionCollector.cs");
            int annotationAnchor = source.IndexOf(
                "TryAnnotationAnchor(entity, out anchor)", StringComparison.Ordinal);
            int extentsFallback = source.IndexOf(
                "return TryExtentsCenter(entity, out anchor);", annotationAnchor,
                StringComparison.Ordinal);

            Assert.True(annotationAnchor >= 0);
            Assert.True(extentsFallback > annotationAnchor);
            Assert.Contains("anchor = mtext.Location;", source);
            Assert.Contains("text.AlignmentPoint", source);
            Assert.Contains("dimension.TextPosition", source);
            Assert.Contains("!includeAllEntities", source);
            Assert.Contains("TryAssignAtAnchor(boundaryIndex, annotationAnchor, id)", source);
        }

        [Fact]
        public void SingleFrameCollection_UsesAllFramesAsOwnershipBoundaries()
        {
            string source = ReadSource("src", "UNCAD", "Cad", "FrameRegionCollector.cs");

            Assert.Contains("var ownershipFrameIds = new HashSet<ObjectId>", source);
            Assert.Contains("ownership.Groups.AddRange(groups);", source);
            Assert.Contains("ownershipFrameIds.Contains(id)", source);
        }

        private static string ReadSource(params string[] parts)
        {
            string path = RepoRoot();
            foreach (string part in parts) path = Path.Combine(path, part);
            return File.ReadAllText(Path.GetFullPath(path));
        }

        private static string RepoRoot()
        {
            // 用编译期文件路径定位仓库根，避免测试输出目录变化（CI/自定义 OutputPath）
            // 导致相对路径推算失效。
            DirectoryInfo directory = new DirectoryInfo(
                Path.GetDirectoryName(SourceFile()) ?? AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "src", "UNCAD"))
                    && Directory.Exists(Path.Combine(directory.FullName, "tests",
                        "UNCAD.Tests")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException(
                "找不到仓库根目录（应包含 src/UNCAD 与 tests/UNCAD.Tests）。");
        }

        private static string SourceFile(
            [System.Runtime.CompilerServices.CallerFilePath] string path = "")
            => path;
    }
}
