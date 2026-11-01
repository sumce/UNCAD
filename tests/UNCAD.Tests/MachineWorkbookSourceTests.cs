using System;
using System.IO;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 机台数据源刷新契约（2.10.0）：只支持本地/共享文件夹工作簿，**不再下载远程数据**。
    /// 网络地址必须在刷新入口给出明确中文提示，绝不发起网络请求。
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

        [Theory]
        [InlineData("https://example.test/machines.xlsx")]
        [InlineData("http://127.0.0.1:8080/machines.xlsx?sign=test")]
        public void Refresh_RejectsHttpSourceWithoutAnyNetworkRequest(string url)
        {
            Assert.True(MachineWorkbookSource.IsRemote(url));
            string database = Path.Combine(Path.GetTempPath(),
                "uncad-remote-" + Guid.NewGuid().ToString("N") + ".db");
            var store = new MachineWorkbookSnapshotStore(database);
            try
            {
                InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                    MachineWorkbookSource.Refresh(url, store));
                Assert.Contains("不再支持网络机台数据源", error.Message);
                Assert.Contains("U1SET", error.Message);
                Assert.False(File.Exists(database), "拒绝网络源时不得创建本地快照数据库");
            }
            finally
            {
                try { if (File.Exists(database)) File.Delete(database); }
                catch { }
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

        private static byte[] WorkbookBytes(bool includeData = true)
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
            using (var stream = new MemoryStream())
            {
                workbook.Write(stream);
                workbook.Close();
                return stream.ToArray();
            }
        }
    }
}
