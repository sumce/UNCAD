using System;
using System.IO;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 机台数据源刷新契约（D-041）：本地/UNC 文件与 HTTP/HTTPS 网络地址都支持。
    /// 网络下载只在显式刷新时进行（GET、零上传）；下载失败且无历史快照时给明确
    /// 中文错误，有历史快照时回退旧快照（UsedCachedFallback）。
    /// </summary>
    public sealed class MachineWorkbookSourceTests
    {
        [Fact]
        public void Refresh_LocalWorkbookReplacesSnapshot()
        {
            string root = TempDirectory("source_local");
            try
            {
                string workbookPath = Path.Combine(root, "machines.xlsx");
                File.WriteAllBytes(workbookPath, WorkbookBytes());
                var store = new MachineWorkbookSnapshotStore(Path.Combine(root, "machine.db"));

                MachineWorkbookSourceResult result = MachineWorkbookSource.Refresh(
                    workbookPath, store);

                Assert.True(result.Updated);
                Assert.False(result.UsedCachedFallback);
                Assert.Equal(Path.GetFullPath(workbookPath), result.LocalPath);
                Assert.NotNull(result.Snapshot);
                Assert.Equal(1, result.Snapshot.RowCount);
                Assert.Equal("设备A", Assert.Single(
                    store.FindRows(workbookPath, "NET01")).CircuitName);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void Refresh_LocalWorkbookReportsUnfilledUnifiedMachineIdRows()
        {
            string root = TempDirectory("source_missing_identity");
            try
            {
                string workbookPath = Path.Combine(root, "machines.xlsx");
                File.WriteAllBytes(workbookPath,
                    WorkbookBytes(includeUnboundCircuit: true));
                var store = new MachineWorkbookSnapshotStore(
                    Path.Combine(root, "machine.db"));

                MachineWorkbookSourceResult result = MachineWorkbookSource.Refresh(
                    workbookPath, store);

                Assert.Equal(1, result.Snapshot.RowCount);
                Assert.Equal(1, result.Snapshot.MissingMachineIdRowCount);
                Assert.Equal(new[] { 3 }, result.Snapshot.MissingMachineIdRowNumbers);
                Assert.Contains("U_机台ID 为空", result.Snapshot.MissingMachineIdWarning);
                Assert.Contains("Excel 行 3", result.Snapshot.MissingMachineIdWarning);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void Refresh_LocalWorkbookMissing_ReportsChinesePath()
        {
            string missing = Path.Combine(Path.GetTempPath(),
                "uncad-missing-" + Guid.NewGuid().ToString("N") + ".xlsx");
            var store = new MachineWorkbookSnapshotStore(Path.Combine(Path.GetTempPath(),
                "uncad-missing-" + Guid.NewGuid().ToString("N") + ".db"));
            try
            {
                FileNotFoundException error = Assert.Throws<FileNotFoundException>(() =>
                    MachineWorkbookSource.Refresh(missing, store));
                Assert.Contains("机台数据 Excel 不存在", error.Message);
                Assert.Equal(missing, error.FileName);
            }
            finally
            {
                try { if (File.Exists(store.DatabasePath)) File.Delete(store.DatabasePath); }
                catch { }
            }
        }

        [Fact]
        public void Refresh_RemoteUnreachableWithoutSnapshot_ThrowsChineseError()
        {
            // 端口 1 在本机几乎必然立即连接拒绝：快速失败，不依赖外网可达性。
            string url = "http://127.0.0.1:1/machines.xlsx";
            Assert.True(MachineWorkbookSource.IsRemote(url));
            string root = TempDirectory("source_remote_dead");
            try
            {
                var store = new MachineWorkbookSnapshotStore(
                    Path.Combine(root, "machine.db"));

                IOException error = Assert.Throws<IOException>(() =>
                    MachineWorkbookSource.Refresh(url, store,
                        Path.Combine(root, "cache")));

                Assert.Contains("无法下载网络机台 Excel", error.Message);
                Assert.Contains("SQLite 快照", error.Message);
                Assert.False(store.TryGetSnapshot(url, out _), "下载失败不得写入快照");
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void Refresh_RemoteUnreachableWithPreviousSnapshot_FallsBack()
        {
            string url = "http://127.0.0.1:1/machines.xlsx";
            string root = TempDirectory("source_remote_fallback");
            try
            {
                string workbookPath = Path.Combine(root, "machines.xlsx");
                File.WriteAllBytes(workbookPath, WorkbookBytes());
                var store = new MachineWorkbookSnapshotStore(
                    Path.Combine(root, "machine.db"));
                // 用同一 URL 键先写入一份成功快照，模拟"上一次刷新成功"。
                store.RefreshFromFile(url, workbookPath);

                MachineWorkbookSourceResult result = MachineWorkbookSource.Refresh(
                    url, store, Path.Combine(root, "cache"));

                Assert.True(result.UsedCachedFallback);
                Assert.False(result.Updated);
                Assert.NotNull(result.Snapshot);
                Assert.Equal(1, result.Snapshot.RowCount);
                Assert.False(string.IsNullOrWhiteSpace(result.Warning),
                    "回退时必须带上失败原因");
                // 旧快照仍在：命令可继续使用。
                Assert.True(store.TryGetSnapshot(url, out MachineWorkbookSnapshotInfo kept));
                Assert.Equal(1, kept.RowCount);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void IsRemote_OnlyMatchesHttpAndHttps()
        {
            Assert.True(MachineWorkbookSource.IsRemote("https://example.test/a.xlsx"));
            Assert.True(MachineWorkbookSource.IsRemote("HTTP://example.test/a.xlsx"));
            Assert.False(MachineWorkbookSource.IsRemote(@"C:\data\machines.xlsx"));
            Assert.False(MachineWorkbookSource.IsRemote(@"\\server\share\machines.xlsx"));
            Assert.False(MachineWorkbookSource.IsRemote(""));
            Assert.False(MachineWorkbookSource.IsRemote(null));
        }

        [Fact]
        public void ValidateWorkbook_AcceptsValidSchemaWithoutDataRows()
        {
            string path = Path.Combine(Path.GetTempPath(), "uncad_empty_"
                + Guid.NewGuid().ToString("N") + ".xlsx");
            File.WriteAllBytes(path, WorkbookBytes(false));
            try { MachineWorkbookSource.ValidateWorkbook(path); }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void ValidateWorkbook_ReportsHtmlAsInvalidData()
        {
            string path = Path.Combine(Path.GetTempPath(), "uncad_html_"
                + Guid.NewGuid().ToString("N") + ".xlsx");
            File.WriteAllText(path, "<html>login required</html>");
            try
            {
                InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                    MachineWorkbookSource.ValidateWorkbook(path));
                Assert.Contains("无法解析", error.Message);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static string TempDirectory(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "uncad_" + name + "_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { }
        }

        private static byte[] WorkbookBytes(bool includeData = true,
            bool includeUnboundCircuit = false)
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("机台数据");
            string[] headers = { "U_区域", "U_机台ID", "回路名称", "U_电缆型号", "U_上游编号",
                "U_配电信息", "U_序号", "U_软管直径", "U_上游类型", "U_设备楼层",
                "U_设备轴位", "U_上游轴位", "U_上游楼层", "U_厂务开关" };
            string[] values = { "LAB", "NET01", "设备A", "ZB-YJVR-3*2.5", "FR01",
                "U220 1P3W 1P20A", "236", "20", "插座盘", "2F", "2/T", "化学实验室",
                "1F", "1P20A" };
            var header = sheet.CreateRow(0);
            for (int index = 0; index < headers.Length; index++)
            {
                header.CreateCell(index).SetCellValue(headers[index]);
            }
            if (includeData)
            {
                var row = sheet.CreateRow(1);
                for (int index = 0; index < headers.Length; index++)
                    row.CreateCell(index).SetCellValue(values[index]);
            }
            if (includeData && includeUnboundCircuit)
                sheet.CreateRow(2).CreateCell(2).SetCellValue("新增回路");
            using (var stream = new MemoryStream())
            {
                workbook.Write(stream);
                workbook.Close();
                return stream.ToArray();
            }
        }
    }
}
