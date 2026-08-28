using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            hdr.CreateCell(4).SetCellValue("类");
            hdr.CreateCell(5).SetCellValue("别名");

            string[,] data = {
                { "1", "电缆", "", "", "", "" },
                { "1.1", "多芯电缆 XLPE", "1.名称:0.6/1kV-YJVR-2.5mm2*3C 多芯电缆", "m", "电缆", "3*2.5" },
                { "1.25", "单芯电缆 XLPE", "1.名称:0.6/1kV-YJV-70mm2*1C*3+PVC-35mm2*1C 单芯电缆", "m", "电缆", "3*70+1*35" },
                { "1.29", "单芯电缆 XLPE", "1.名称:0.6/1kV-YJV-95mm2*1C*3+PVC-50mm2*1C 单芯电缆", "m", "电缆", "3*95+1*50" },
                { "", "小计", "", "", "", "" },
                { "3", "配管 PIPE", "", "", "", "" },
                { "3.4", "镀锌穿线管", "1.名称:镀锌穿线管EMT PIPE 51mm(2\")", "m", "线管", "51mm" },
                { "3.8", "包塑金属软管(波纹管)", "1.名称:51mm(2\") 包塑金属软管(波纹管)附镀锌接头", "m", "软管", "51mm" },
                { "3.9", "包塑金属软管(波纹管)", "1.名称:75mm(3\") 包塑金属软管", "m", "软管", "75mm" }
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
        public void ReadList_BindsTsCategoryAliasAndMigrationAliasHeaders()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad_alias_" + Guid.NewGuid().ToString("N") + ".xlsx");
            var workbook = new XSSFWorkbook();
            try
            {
                ISheet sheet = workbook.CreateSheet("Sheet1");
                string[] headers =
                {
                    "类", "项次编码", "项目名称", "项目特征", "单位", "别名", "别名1"
                };
                IRow header = sheet.CreateRow(0);
                for (int column = 0; column < headers.Length; column++)
                    header.CreateCell(column).SetCellValue(headers[column]);
                IRow row = sheet.CreateRow(1);
                string[] values =
                {
                    "线管", "3.3", "镀锌穿线管", "38mm清单特征", "m", "38mm", "32mm"
                };
                for (int column = 0; column < values.Length; column++)
                    row.CreateCell(column).SetCellValue(values[column]);

                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                    workbook.Write(stream);
                ListItem item = Assert.Single(ListItemReader.ReadList(path));

                Assert.Equal("线管", item.Category);
                Assert.Equal("38mm", item.Alias);
                Assert.Equal("38mm", item.Spec);
                Assert.Equal("32mm", item.Alias1);
            }
            finally
            {
                workbook.Close();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void ParseEmbeddedTsv_RoundTripsEscapesAndKeepsBlankCategoriesBlank()
        {
            string tsv = "3.3\t线管\t镀锌穿线管\t1.名称:穿线管\\n2.说明:tab\\tinside\tm\t38mm\t32mm\n"
                + "# 注释行不参与解析\n"
                + "8.4\t\t变压器\t10KVA\t台\t\t\n";

            List<ListItem> items = ListItemReader.ParseEmbeddedTsv(tsv);

            Assert.Equal(2, items.Count);
            Assert.Equal("线管", items[0].Category);
            Assert.Contains("\n", items[0].Feature);
            Assert.Contains("\t", items[0].Feature);
            Assert.Equal("38mm", items[0].Alias);
            Assert.Equal("32mm", items[0].Alias1);
            // 没有"类"的不判定：空分类保持空字符串，而不是推断。
            Assert.Equal("", items[1].Category);
            Assert.Equal("变压器", items[1].Name);
        }

        [Fact]
        public void ReadEmbedded_ExposesTheShippedFixedCatalogContract()
        {
            List<ListItem> items = ListItemReader.ReadEmbedded();

            Assert.True(items.Count >= 100);
            Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Code)));
            Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Name)));

            // ts.xlsx 第60行：输入 32mm 显式迁移到 3.3 / 38mm 材料。
            ListItem rigid38 = items.Single(item => item.Code == "3.3");
            Assert.Equal("镀锌穿线管", rigid38.Name);
            Assert.Equal("38mm", rigid38.Alias);
            Assert.Equal("32mm", rigid38.Alias1);

            // 新版 ts.xlsx：旧/简化电缆型号通过全局别名1迁移到正式清单行。
            ListItem cable35 = items.Single(item => item.Code == "1.20");
            Assert.Equal("电缆", cable35.Category);
            Assert.Equal("3*35+1*16", cable35.Alias);
            Assert.Equal("3*35+1*25", cable35.Alias1);
            Assert.Equal("1.20", new BoqCatalogIndex(items)
                .FindCable("ZB-YJV-3*35+1*25")?.Code);
        }

        [Fact]
        public void ParseEmbeddedTsv_RejectsMalformedRowsInsteadOfSilentlyDroppingThem()
        {
            string malformed = "3.3\t线管\t镀锌穿线管\tm\t38mm\t32mm\n";

            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => ListItemReader.ParseEmbeddedTsv(malformed));

            Assert.Contains("第 1 行", error.Message);
            Assert.Contains("7 列", error.Message);
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
