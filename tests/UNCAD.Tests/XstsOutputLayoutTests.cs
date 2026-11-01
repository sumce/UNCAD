using System;
using System.IO;
using UNCAD.Core.Submission;
using UNCAD.Features.Stat;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// XSTS 输出接入统一 OutputLayout 的契约（2.10.0）：
    /// 报告写入 &lt;输出根&gt;\&lt;图纸&gt;\XSTS\UNCAD_XSTS_&lt;时间&gt;.xlsx，
    /// 保留原文件名规则；写盘前做占用预检；未配置输出根时默认“我的文档\UNCAD输出”。
    /// </summary>
    public sealed class XstsOutputLayoutTests
    {
        private static readonly DateTime FixedNow =
            new DateTime(2026, 11, 1, 12, 34, 56, 789);

        [Fact]
        public void ResolveOutputFile_UsesXstsKindFolderAndKeepsFileNameRule()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "uncad-xsts-layout-root"));

            string byDrawingAndKind = XstsFeature.ResolveOutputFile(root, "一号厂房",
                FixedNow, true, true);
            Assert.Equal(Path.Combine(root, "一号厂房", "XSTS",
                "UNCAD_XSTS_20261101_123456_789.xlsx"), byDrawingAndKind);

            // 关闭“按图纸/分类”开关时退化为 <输出根>\UNCAD_XSTS_时间.xlsx。
            string flat = XstsFeature.ResolveOutputFile(root, "一号厂房", FixedNow,
                false, false);
            Assert.Equal(Path.Combine(root, "UNCAD_XSTS_20261101_123456_789.xlsx"), flat);
        }

        [Fact]
        public void ResolveOutputFile_SanitizesDrawingNameAndUsesDefaultRoot()
        {
            string documents = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents)) return;   // 无“我的文档”的宿主不测默认值

            string root = OutputLayout.NormalizeRoot("");
            string path = XstsFeature.ResolveOutputFile(root, "厂房/一期:*", FixedNow,
                true, true);

            Assert.Equal(Path.Combine(documents, "UNCAD输出", "厂房_一期__", "XSTS",
                "UNCAD_XSTS_20261101_123456_789.xlsx"), path);
        }

        [Fact]
        public void ValidateNotLocked_BlocksXstsReportHeldOpenBeforeWriting()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "uncad-xsts-lock-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = XstsFeature.ResolveOutputFile(directory, "图纸A", FixedNow,
                true, true);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "x");
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None))
                {
                    IOException error = Assert.Throws<IOException>(() =>
                        OutputLayout.ValidateNotLocked(new[] { path }));
                    Assert.Contains("正被其他程序占用", error.Message);
                    Assert.Contains("关闭", error.Message);
                    Assert.Contains(path, error.Message);
                }

                OutputLayout.ValidateNotLocked(new[] { path });
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }
    }
}
