using System;
using System.IO;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 网络机台工作簿下载缓存（D-041 恢复）：失败下载残留 *.download-* 的清理，
    /// 以及按 URL 定位已下载 XLSX 的缓存路径。
    /// </summary>
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

        [Fact]
        public void TryGetCachedPath_LocatesDownloadByUrlAndIgnoresLocalPaths()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "uncad-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Uri uri = new Uri("https://example.test/tai/machines.xlsx?sign=1#frag");
                string cached = Path.Combine(directory,
                    "machine-" + MachineWorkbookSource.CacheKey(uri) + ".xlsx");
                File.WriteAllText(cached, "x");

                Assert.True(MachineWorkbookSource.TryGetCachedPath(
                    "https://example.test/tai/machines.xlsx?sign=1",
                    directory, out string found));
                Assert.Equal(cached, found);

                // 本地/UNC 路径没有下载缓存；URL 片段不参与缓存键。
                Assert.False(MachineWorkbookSource.TryGetCachedPath(
                    @"C:\data\machines.xlsx", directory, out string none));
                Assert.Null(none);
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }
    }
}
