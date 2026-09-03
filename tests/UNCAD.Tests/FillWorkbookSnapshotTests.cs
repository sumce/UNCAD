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
        public void Load_CachesMachineRowsAndMergesEmbeddedCatalog()
        {
            string machinePath = Path.Combine(Path.GetTempPath(),
                "uncad_machine_source_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                WriteMachine(machinePath, "设备A");

                FillWorkbookSnapshot first = FillWorkbookSnapshot.Load(machinePath,
                    MachineWorkbookLayout.A1);
                Assert.False(first.MachineCacheHit);
                Assert.Equal("设备A", Assert.Single(first.FindRows("CACHE01")).CircuitName);
                Assert.True(first.CatalogItemCount > 0);
                Assert.Contains(first.ListItems,
                    item => item.Code == "3.3" && item.Alias1 == "32mm");

                // 内嵌清单只随插件版本变化，不随机台文件缓存失效。
                first.FindRows("CACHE01")[0].CircuitName = "缓存副本被修改";
                FillWorkbookSnapshot second = FillWorkbookSnapshot.Load(machinePath,
                    MachineWorkbookLayout.A1);
                Assert.True(second.MachineCacheHit);
                Assert.Equal("设备A", Assert.Single(second.FindRows("CACHE01")).CircuitName);

                WriteMachine(machinePath, "设备B");
                File.SetLastWriteTimeUtc(machinePath, DateTime.UtcNow.AddSeconds(5));
                FillWorkbookSnapshot refreshedMachine = FillWorkbookSnapshot.Load(machinePath,
                    MachineWorkbookLayout.A1);
                Assert.False(refreshedMachine.MachineCacheHit);
                Assert.Equal("设备B",
                    Assert.Single(refreshedMachine.FindRows("CACHE01")).CircuitName);
            }
            finally
            {
                if (File.Exists(machinePath)) File.Delete(machinePath);
            }
        }

        [Fact]
        public void Load_EmbeddedCatalogIsDefensivelyClonedPerSnapshot()
        {
            string machinePath = Path.Combine(Path.GetTempPath(),
                "uncad_machine_clone_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                WriteMachine(machinePath, "设备A");
                FillWorkbookSnapshot first = FillWorkbookSnapshot.Load(machinePath,
                    MachineWorkbookLayout.A1);
                string original = first.ListItems[0].Code;
                first.ListItems[0].Code = "被篡改";

                FillWorkbookSnapshot second = FillWorkbookSnapshot.Load(machinePath,
                    MachineWorkbookLayout.A1);
                Assert.Equal(original, second.ListItems[0].Code);
            }
            finally
            {
                if (File.Exists(machinePath)) File.Delete(machinePath);
            }
        }

        [Fact]
        public void Load_ReportsClearMessageWhenMachineWorkbookIsLocked()
        {
            string machinePath = Path.Combine(Path.GetTempPath(),
                "uncad_machine_locked_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                WriteMachine(machinePath, "设备A");
                using (var hold = new FileStream(machinePath, FileMode.Open,
                    FileAccess.ReadWrite, FileShare.None))
                {
                    IOException error = Assert.Throws<IOException>(() =>
                    FillWorkbookSnapshot.Load(machinePath, MachineWorkbookLayout.A1));
                    // 文件被占用时应提示用户，而不是笼统说“正在更新”。
                    Assert.Contains("占用", error.Message);
                }
                FillWorkbookSnapshot after = FillWorkbookSnapshot.Load(machinePath,
                    MachineWorkbookLayout.A1);
                Assert.Equal("设备A", Assert.Single(after.FindRows("CACHE01")).CircuitName);
            }
            finally
            {
                if (File.Exists(machinePath)) File.Delete(machinePath);
            }
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
            for (int c = 0; c < headers.Length; c++)
            {
                header.CreateCell(c).SetCellValue(headers[c]);
                row.CreateCell(c).SetCellValue(values[c]);
            }
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                wb.Write(stream);
            wb.Close();
        }
    }
}
