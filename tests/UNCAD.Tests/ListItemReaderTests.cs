using System;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>清单编号匹配测试：电缆型号/软管直径 ↔ 编号。</summary>
    public class ListItemReaderTests
    {
        private static string CreateTempWorkbook()
        {
            string path = Path.Combine(Path.GetTempPath(), "uncad_list_" + Guid.NewGuid().ToString("N") + ".xlsx");
            var wb = new XSSFWorkbook();

            // 清单表（表头含"项目特征"）
            var ws = wb.CreateSheet("清单");
            var hdr = ws.CreateRow(0);
            hdr.CreateCell(0).SetCellValue("编号");
            hdr.CreateCell(1).SetCellValue("项目名称");
            hdr.CreateCell(2).SetCellValue("项目特征");
            hdr.CreateCell(3).SetCellValue("单位");

            string[,] data = {
                { "1", "电缆", "", "", "", "" },
                { "1.1", "多芯电缆 XLPE", "1.名称:0.6/1kV-YJVR-2.5mm2*3C 多芯电缆", "m", "", "3*2.5" },
                { "1.25", "单芯电缆 XLPE", "1.名称:0.6/1kV-YJV-70mm2*1C*3+PVC-35mm2*1C 单芯电缆", "m", "", "3*70+1*35" },
                { "1.29", "单芯电缆 XLPE", "1.名称:0.6/1kV-YJV-95mm2*1C*3+PVC-50mm2*1C 单芯电缆", "m", "", "3*95+1*50" },
                { "", "小计", "", "", "", "" },
                { "3", "配管 PIPE", "", "", "", "" },
                { "3.4", "镀锌穿线管", "1.名称:镀锌穿线管EMT PIPE 51mm(2\")", "m", "", "51mm" },
                { "3.8", "包塑金属软管(波纹管)", "1.名称:51mm(2\") 包塑金属软管(波纹管)附镀锌接头", "m", "", "51mm" },
                { "3.9", "包塑金属软管(波纹管)", "1.名称:75mm(3\") 包塑金属软管", "m", "", "75mm" }
            };
            for (int r = 0; r < data.GetLength(0); r++)
            {
                var row = ws.CreateRow(r + 1);
                for (int c = 0; c < data.GetLength(1); c++)
                    if (!string.IsNullOrEmpty(data[r, c])) row.CreateCell(c).SetCellValue(data[r, c]);
            }

            // 无关表设为活动表
            var junk = wb.CreateSheet("材料");
            junk.CreateRow(0).CreateCell(0).SetCellValue("x");
            wb.SetActiveSheet(1);

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                wb.Write(fs);
            return path;
        }

        [Fact]
        public void ReadList_BindsReorderedCatalogColumnsByHeader()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad_list_headers_" + Guid.NewGuid().ToString("N") + ".xlsx");
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("固定清单");
            string[] headers = { "规格", "单位", "项目特征", "编号", "项目名称" };
            string[] values = { "20~30A", "个", "1.名称:插座20A~30A", "8.3", "插座" };
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
            try
            {
                ListItem item = Assert.Single(ListItemReader.ReadList(path));
                Assert.Equal("8.3", item.Code);
                Assert.Equal("插座", item.Name);
                Assert.Equal("20~30A", item.Spec);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void ReadList_SkipsCategoryAndSummaryRows()
        {
            string path = CreateTempWorkbook();
            try
            {
                var items = ListItemReader.ReadList(path);
                Assert.Equal(6, items.Count); // 1.1, 1.25, 1.29, 3.4, 3.8, 3.9（分类行/小计被跳过）
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void FindCable_MatchesBySpec()
        {
            string path = CreateTempWorkbook();
            try
            {
                var items = ListItemReader.ReadList(path);
                Assert.Equal("1.25", ListItemReader.FindCable(items, "ZB-YJV-3*70+1*35")?.Code);
                Assert.Equal("1.29", ListItemReader.FindCable(items, "ZB-YJV-3*95+1*50")?.Code);
                Assert.Equal("1.1", ListItemReader.FindCable(items, "ZB-YJVR-3*2.5")?.Code);
                Assert.Null(ListItemReader.FindCable(items, "ZB-YJVR-4*2.5用4*4")); // 脏数据不匹配
                Assert.Null(ListItemReader.FindCable(items, null));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void FindConduit_MatchesDiameterAndSoftConduit()
        {
            string path = CreateTempWorkbook();
            try
            {
                var items = ListItemReader.ReadList(path);
                // 51mm 有 穿线管(3.4) 和 软管(3.8)，必须匹配软管
                Assert.Equal("3.8", ListItemReader.FindConduit(items, "51")?.Code);
                Assert.Equal("3.9", ListItemReader.FindConduit(items, "75")?.Code);
                Assert.Null(ListItemReader.FindConduit(items, "999"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
