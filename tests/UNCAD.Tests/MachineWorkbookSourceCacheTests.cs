using System;
using System.IO;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>机台工作簿下载缓存清理（失败下载残留 *.download-*）。</summary>
    public sealed class MachineWorkbookSourceCacheTests
    {
        [Fact]
        public void CleanupStaleDownloads_DeletesOnlyOldTempFiles()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "uncad-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string stale = Path.Combine(directory, "machine-a.xlsx.download-1");
                string fresh = Path.Combine(directory, "machine-a.xlsx.download-2");
                string cache = Path.Combine(directory, "machine-a.xlsx");
                File.WriteAllText(stale, "x");
                File.WriteAllText(fresh, "x");
                File.WriteAllText(cache, "x");
                File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-3));
                File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddMinutes(-5));

                MachineWorkbookSource.CleanupStaleDownloads(directory);

                Assert.False(File.Exists(stale));
                Assert.True(File.Exists(fresh));
                Assert.True(File.Exists(cache));
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }
    }
}
