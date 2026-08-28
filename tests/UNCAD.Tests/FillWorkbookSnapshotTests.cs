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

                FillWorkbookSnapshot first = FillWorkbookSnapshot.Load(machinePath);
                Assert.False(first.MachineCacheHit);
                Assert.Equal("设备A", Assert.Single(first.FindRows("CACHE01")).CircuitName);
                Assert.True(first.CatalogItemCount > 0);
                Assert.Contains(first.ListItems,
                    item => item.Code == "3.3" && item.Alias1 == "32mm");

                // 内嵌清单只随插件版本变化，不随机台文件缓存失效。
                first.FindRows("CACHE01")[0].CircuitName = "缓存副本被修改";
                FillWorkbookSnapshot second = FillWorkbookSnapshot.Load(machinePath);
                Assert.True(second.MachineCacheHit);
                Assert.Equal("设备A", Assert.Single(second.FindRows("CACHE01")).CircuitName);

                WriteMachine(machinePath, "设备B");
                File.SetLastWriteTimeUtc(machinePath, DateTime.UtcNow.AddSeconds(5));
                FillWorkbookSnapshot refreshedMachine = FillWorkbookSnapshot.Load(machinePath);
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
                FillWorkbookSnapshot first = FillWorkbookSnapshot.Load(machinePath);
                string original = first.ListItems[0].Code;
                first.ListItems[0].Code = "被篡改";

                FillWorkbookSnapshot second = FillWorkbookSnapshot.Load(machinePath);
                Assert.Equal(original, second.ListItems[0].Code);
            }
            finally
            {
                if (File.Exists(machinePath)) File.Delete(machinePath);
            }
        }

        private static void WriteMachine(string path, string circuitName)
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("Sheet1");
            string[] headers = { "所属区域", "机台ID", "回路名称", "电缆型号", "FR",
                "详情", "项目序号", "软管直径", "NEXT", "下游轴位", "上游轴位" };
            string[] values = { "LAB", "CACHE01", circuitName, "ZB-YJVR-3*2.5",
                "3U-UPS28D1-PP-03", "U220 1P3W 1P20A", "236", "20",
                "插座盘", "2/T", "化学实验室" };
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
