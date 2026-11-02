using System;
using System.IO;
using UNCAD.Core.Submission;
using UNCAD.Features.Stat;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// XSTS 输出契约（D-035，2026-11-01 恢复 2.9.x 现场习惯）：
    /// 报告直接写入 &lt;输出根&gt;\UNCAD_XSTS_&lt;时间&gt;.xlsx，不按图纸/类型分层；
    /// 写盘前保留占用预检；未配置输出根时默认“我的文档\UNCAD输出”。
    /// </summary>
    public sealed class XstsOutputLayoutTests
    {
        private static readonly DateTime FixedNow =
            new DateTime(2026, 11, 1, 12, 34, 56, 789);

        [Fact]
        public void ResolveOutputFile_WritesReportDirectlyIntoRoot()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "uncad-xsts-layout-root"));

            string path = XstsFeature.ResolveOutputFile(root, FixedNow);

            Assert.Equal(Path.Combine(root,
                "UNCAD_XSTS_20261101_123456_789.xlsx"), path);
        }

        [Fact]
        public void ResolveOutputFile_UsesDefaultRootWhenUnconfigured()
        {
            string documents = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents)) return;   // 无“我的文档”的宿主不测默认值

            string root = OutputLayout.NormalizeRoot("");
            string path = XstsFeature.ResolveOutputFile(root, FixedNow);

            Assert.Equal(Path.Combine(documents, "UNCAD输出",
                "UNCAD_XSTS_20261101_123456_789.xlsx"), path);
        }

        [Fact]
        public void ValidateNotLocked_BlocksXstsReportHeldOpenBeforeWriting()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "uncad-xsts-lock-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = XstsFeature.ResolveOutputFile(directory, FixedNow);
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
