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
        public void Load_RefreshesMachineRows_AndCachesCatalogUntilCatalogChanges()
        {
            string machinePath = Path.Combine(Path.GetTempPath(),
                "uncad_machine_source_" + Guid.NewGuid().ToString("N") + ".xlsx");
            string catalogPath = Path.Combine(Path.GetTempPath(),
                "uncad_catalog_source_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                WriteMachine(machinePath, "设备A");
                WriteCatalog(catalogPath, "8.3", "插座20~30A");

                FillWorkbookSnapshot first = FillWorkbookSnapshot.Load(machinePath, catalogPath);
                Assert.False(first.CatalogCacheHit);
                Assert.Equal("设备A", Assert.Single(first.FindRows("CACHE01")).CircuitName);
                Assert.Equal("8.3", Assert.Single(first.ListItems).Code);

                FillWorkbookSnapshot second = FillWorkbookSnapshot.Load(machinePath, catalogPath);
                Assert.True(second.CatalogCacheHit);

                WriteMachine(machinePath, "设备B");
                FillWorkbookSnapshot refreshedMachine = FillWorkbookSnapshot.Load(machinePath, catalogPath);
                Assert.True(refreshedMachine.CatalogCacheHit);
                Assert.Equal("设备B", Assert.Single(refreshedMachine.FindRows("CACHE01")).CircuitName);

                WriteCatalog(catalogPath, "8.30", "插座20~30A（新版固定清单）");
                File.SetLastWriteTimeUtc(catalogPath, DateTime.UtcNow.AddSeconds(5));
                FillWorkbookSnapshot refreshedCatalog = FillWorkbookSnapshot.Load(machinePath, catalogPath);
                Assert.False(refreshedCatalog.CatalogCacheHit);
                Assert.Equal("8.30", Assert.Single(refreshedCatalog.ListItems).Code);
            }
            finally
            {
                if (File.Exists(machinePath)) File.Delete(machinePath);
                if (File.Exists(catalogPath)) File.Delete(catalogPath);
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

        private static void WriteCatalog(string path, string code, string feature)
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("Sheet2");
            string[] headers = { "编号", "项目名称", "项目特征", "单位", "", "规格" };
            var header = sheet.CreateRow(0);
            for (int c = 0; c < headers.Length; c++) header.CreateCell(c).SetCellValue(headers[c]);
            var row = sheet.CreateRow(1);
            row.CreateCell(0).SetCellValue(code);
            row.CreateCell(1).SetCellValue("插座");
            row.CreateCell(2).SetCellValue(feature);
            row.CreateCell(3).SetCellValue("个");
            row.CreateCell(5).SetCellValue("20~30A");
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                wb.Write(stream);
            wb.Close();
        }
    }
}
