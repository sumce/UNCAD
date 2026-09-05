using System;
using System.IO;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    public class FillWorkbookSnapshotTests
    {
        [Fact]
        public void Load_UsesExplicitRefreshAndIgnoresLaterSourceChanges()
        {
            string root = TempDirectory("snapshot_manual");
            string machinePath = Path.Combine(root, "machine.xlsx");
            var store = new MachineWorkbookSnapshotStore(Path.Combine(root, "machine.db"));
            try
            {
                WriteMachine(machinePath, "设备A");
                store.RefreshFromFile(machinePath, machinePath);

                FillWorkbookSnapshot first = FillWorkbookSnapshot.Load(machinePath, store);
                Assert.True(first.MachineCacheHit);
                Assert.Equal("设备A", Assert.Single(first.FindRows("cache01")).CircuitName);
                Assert.True(first.CatalogItemCount > 0);
                Assert.Contains(first.ListItems,
                    item => item.Code == "3.3" && item.Alias1 == "32mm");

                // 修改源文件不会绕过“仅手动刷新”策略。
                WriteMachine(machinePath, "设备B");
                FillWorkbookSnapshot unchanged = FillWorkbookSnapshot.Load(machinePath, store);
                Assert.Equal("设备A", Assert.Single(unchanged.FindRows("CACHE01")).CircuitName);

                // 再次显式刷新后才切换到新内容。
                store.RefreshFromFile(machinePath, machinePath);
                FillWorkbookSnapshot refreshed = FillWorkbookSnapshot.Load(machinePath, store);
                Assert.Equal("设备B", Assert.Single(refreshed.FindRows("CACHE01")).CircuitName);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void Load_DoesNotNeedTheSourceFileAfterRefresh()
        {
            string root = TempDirectory("snapshot_source_removed");
            string machinePath = Path.Combine(root, "machine.xlsx");
            var store = new MachineWorkbookSnapshotStore(Path.Combine(root, "machine.db"));
            try
            {
                WriteMachine(machinePath, "设备A");
                store.RefreshFromFile(machinePath, machinePath);
                File.Delete(machinePath);

                FillWorkbookSnapshot snapshot = FillWorkbookSnapshot.Load(machinePath, store);
                Assert.Equal("设备A", Assert.Single(snapshot.FindRows("CACHE01")).CircuitName);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void Load_RequiresAnExplicitRefresh()
        {
            string root = TempDirectory("snapshot_requires_refresh");
            string machinePath = Path.Combine(root, "machine.xlsx");
            var store = new MachineWorkbookSnapshotStore(Path.Combine(root, "machine.db"));
            try
            {
                WriteMachine(machinePath, "设备A");
                InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                    FillWorkbookSnapshot.Load(machinePath, store));
                Assert.Contains("刷新", error.Message);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void Load_EmbeddedCatalogIsDefensivelyClonedPerSnapshot()
        {
            string root = TempDirectory("snapshot_catalog_clone");
            string machinePath = Path.Combine(root, "machine.xlsx");
            var store = new MachineWorkbookSnapshotStore(Path.Combine(root, "machine.db"));
            try
            {
                WriteMachine(machinePath, "设备A");
                store.RefreshFromFile(machinePath, machinePath);
                FillWorkbookSnapshot first = FillWorkbookSnapshot.Load(machinePath, store);
                string original = first.ListItems[0].Code;
                first.ListItems[0].Code = "被篡改";

                FillWorkbookSnapshot second = FillWorkbookSnapshot.Load(machinePath, store);
                Assert.Equal(original, second.ListItems[0].Code);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void RefreshLockedSourceKeepsTheLastValidSnapshot()
        {
            string root = TempDirectory("snapshot_locked");
            string machinePath = Path.Combine(root, "machine.xlsx");
            var store = new MachineWorkbookSnapshotStore(Path.Combine(root, "machine.db"));
            try
            {
                WriteMachine(machinePath, "设备A");
                store.RefreshFromFile(machinePath, machinePath);
                using (var hold = new FileStream(machinePath, FileMode.Open,
                    FileAccess.ReadWrite, FileShare.None))
                {
                    Assert.Throws<IOException>(() =>
                        store.RefreshFromFile(machinePath, machinePath));
                }

                FillWorkbookSnapshot after = FillWorkbookSnapshot.Load(machinePath, store);
                Assert.Equal("设备A", Assert.Single(after.FindRows("CACHE01")).CircuitName);
            }
            finally
            {
                DeleteDirectory(root);
            }
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

        private static void WriteMachine(string path, string circuitName)
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("机台数据");
            string[] headers = { "U_区域", "U_机台ID", "回路名称", "U_电缆型号", "U_上游编号",
                "U_配电信息", "U_序号", "U_软管直径", "U_上游类型", "U_设备楼层",
                "U_设备轴位", "U_上游轴位", "U_上游楼层", "U_厂务开关" };
            string[] values = { "LAB", "CACHE01", circuitName, "ZB-YJVR-3*2.5",
                "3U-UPS28D1-PP-03", "U220 1P3W 1P20A", "236", "20",
                "插座盘", "2F", "2/T", "化学实验室", "1F", "1P20A" };
            var header = sheet.CreateRow(0);
            var row = sheet.CreateRow(1);
            for (int column = 0; column < headers.Length; column++)
            {
                header.CreateCell(column).SetCellValue(headers[column]);
                row.CreateCell(column).SetCellValue(values[column]);
            }
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                wb.Write(stream);
            wb.Close();
        }
    }
}
