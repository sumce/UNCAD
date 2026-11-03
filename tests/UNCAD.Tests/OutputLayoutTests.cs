using System;
using System.IO;
using UNCAD.Core.Submission;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 输出布局契约（D-042，2026-11-03）：一个机台一个文件夹，
    /// BOQ/DWG/PDF 同夹、不按图纸名或类别分层：
    /// <c>&lt;root&gt;\&lt;机台&gt;\[BOQ]&lt;机台&gt;.xlsx</c>。
    /// 写占用预检给出中文原因。
    /// </summary>
    public sealed class OutputLayoutTests
    {
        [Fact]
        public void ComposeBoqPath_PutsBoqInsideTheMachineFolder()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "uncad-layout-root"));

            Assert.Equal(Path.Combine(root, "M-01", "[BOQ]M-01.xlsx"),
                OutputLayout.ComposeBoqPath(root, "M-01"));
        }

        [Fact]
        public void ComposeDwgPath_PutsDwgInsideTheSameMachineFolder()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "uncad-layout-root"));

            Assert.Equal(Path.Combine(root, "M-01", "M-01.dwg"),
                OutputLayout.ComposeDwgPath(root, "M-01"));
        }

        [Fact]
        public void ComposePdfPath_PutsPdfInsideTheSameMachineFolder()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "uncad-layout-root"));

            Assert.Equal(Path.Combine(root, "M-01", "M-01_C-1.pdf"),
                OutputLayout.ComposePdfPath(root, "M-01", "M-01_C-1.pdf"));
        }

        [Fact]
        public void ResolveMachineDirectory_RejectsBlankMachineId()
            => Assert.Throws<ArgumentException>(() =>
                OutputLayout.ResolveMachineDirectory(@"C:\out", "   "));

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
    }
}
