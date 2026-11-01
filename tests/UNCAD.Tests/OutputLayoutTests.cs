using System;
using System.IO;
using UNCAD.Core.Submission;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 输出布局契约（2.10.0）：
    /// <c>&lt;root&gt;\&lt;图纸&gt;\BOQ\&lt;机台&gt;\[BOQ]&lt;机台&gt;.xlsx</c>，
    /// DWG/PDF 各自分类；图纸名清洗/截断/空名回退；写占用预检给出中文原因。
    /// </summary>
    public sealed class OutputLayoutTests
    {
        [Theory]
        [InlineData("A/B:C*D?", "A_B_C_D_")]
        [InlineData("  图纸A  ", "图纸A")]
        [InlineData("末尾有点...", "末尾有点")]
        [InlineData("", "未命名图纸")]
        [InlineData("   ", "未命名图纸")]
        [InlineData(null, "未命名图纸")]
        public void SanitizeDrawingName_ReplacesIllegalCharactersAndFallsBack(
            string drawingName, string expected)
            => Assert.Equal(expected, OutputLayout.SanitizeDrawingName(drawingName));

        [Fact]
        public void SanitizeDrawingName_TruncatesToSixtyCharacters()
        {
            string name = OutputLayout.SanitizeDrawingName(new string('图', 100));
            Assert.Equal(60, name.Length);
            Assert.Equal(new string('图', 60), name);
        }

        [Fact]
        public void ResolveKindDirectory_FollowsByDrawingAndSeparateKindsSwitches()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "uncad-layout-root"));

            Assert.Equal(Path.Combine(root, "图纸A", "BOQ"),
                OutputLayout.ResolveKindDirectory(root, "图纸A", OutputLayout.KindBoq,
                    true, true));
            Assert.Equal(Path.Combine(root, "BOQ"),
                OutputLayout.ResolveKindDirectory(root, "图纸A", OutputLayout.KindBoq,
                    false, true));
            Assert.Equal(Path.Combine(root, "图纸A"),
                OutputLayout.ResolveKindDirectory(root, "图纸A", OutputLayout.KindBoq,
                    true, false));
            Assert.Equal(root,
                OutputLayout.ResolveKindDirectory(root, "图纸A", OutputLayout.KindBoq,
                    false, false));
        }

        [Fact]
        public void ComposeBoqPath_MatchesTheRequiredStructure()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "uncad-layout-root"));

            string path = OutputLayout.ComposeBoqPath(root, "一号厂房", "M-01", true, true);

            Assert.Equal(Path.Combine(root, "一号厂房", "BOQ", "M-01", "[BOQ]M-01.xlsx"),
                path);
        }

        [Fact]
        public void ComposeDwgAndPdfPaths_UseTheirOwnKindFolders()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "uncad-layout-root"));

            Assert.Equal(Path.Combine(root, "一号厂房", "DWG", "M-01", "M-01.dwg"),
                OutputLayout.ComposeDwgPath(root, "一号厂房", "M-01", true, true));
            Assert.Equal(Path.Combine(root, "一号厂房", "PDF", "M-01_C-1.pdf"),
                OutputLayout.ComposePdfPath(root, "一号厂房", "M-01_C-1.pdf", true, true));
        }

        [Fact]
        public void ComposePaths_SanitizeDrawingNameBeforeUsingItAsAFolder()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "uncad-layout-root"));

            string path = OutputLayout.ComposeBoqPath(root, "厂房/一期:*", "M1", true, true);

            Assert.Equal(Path.Combine(root, "厂房_一期__", "BOQ", "M1", "[BOQ]M1.xlsx"),
                path);
        }

        [Fact]
        public void NormalizeRoot_UsesDocumentsUncadOutputWhenEmpty()
        {
            string documents = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents)) return;   // 无"我的文档"的宿主不测默认值

            Assert.True(OutputLayout.UsesDefaultRoot(""));
            Assert.True(OutputLayout.UsesDefaultRoot("   "));
            Assert.False(OutputLayout.UsesDefaultRoot(@"C:\out"));
            Assert.Equal(Path.Combine(documents, "UNCAD输出"),
                OutputLayout.NormalizeRoot(""));
            Assert.Equal(Path.Combine(documents, "UNCAD输出"),
                OutputLayout.NormalizeRoot("   "));
            Assert.Equal(Path.GetFullPath(@"C:\out"), OutputLayout.NormalizeRoot(@"C:\out"));
        }

        [Fact]
        public void ValidateNotLocked_ThrowsChineseReasonWhenFileIsHeldOpen()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "uncad-layout-lock-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "[BOQ]M1.xlsx");
            File.WriteAllText(path, "x");
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None))
                {
                    IOException error = Assert.Throws<IOException>(() =>
                        OutputLayout.ValidateNotLocked(new[] { path }));
                    Assert.Contains("正被其他程序占用", error.Message);
                    Assert.Contains(path, error.Message);
                    Assert.Contains("关闭", error.Message);
                }

                OutputLayout.ValidateNotLocked(new[] { path });
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }

        [Fact]
        public void ValidateNotLocked_IgnoresMissingPathsAndBlankEntries()
        {
            string missing = Path.Combine(Path.GetTempPath(),
                "uncad-missing-" + Guid.NewGuid().ToString("N") + ".xlsx");
            OutputLayout.ValidateNotLocked(new[] { missing, "", "   ", null });
        }

        [Fact]
        public void ResolveKindDirectory_RejectsBlankKindWhenSeparateKindsEnabled()
            => Assert.Throws<ArgumentException>(() =>
                OutputLayout.ResolveKindDirectory(@"C:\out", "图纸", "", true, true));
    }
}
