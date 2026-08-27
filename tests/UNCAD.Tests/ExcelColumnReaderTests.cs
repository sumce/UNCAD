using System;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>Excel 读取测试：先写一个 xlsx，再按 UNC_FILL 的规则读回。</summary>
    public class ExcelColumnReaderTests
    {
        private static string CreateTempWorkbook(params string[] colA)
        {
            string path = Path.Combine(Path.GetTempPath(), "uncad_fill_" + Guid.NewGuid().ToString("N") + ".xlsx");
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("数据");
            for (int i = 0; i < colA.Length; i++)
            {
                var row = sheet.CreateRow(i);
                string v = colA[i];
                if (v == "(blank)")
                {
                    // 留空行
                }
                else if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out double d)
                         && v.IndexOfAny(new[] { 'm', '桥', '*' }) < 0)
                {
                    row.CreateCell(0).SetCellValue(d); // 数字单元格
                }
                else
                {
                    row.CreateCell(0).SetCellValue(v); // 文本单元格
                }
            }
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                wb.Write(fs);
            return path;
        }

        [Fact]
        public void ReadColumn_ReadsTextAndNumbers_Strictly()
        {
            string path = CreateTempWorkbook(
                "表头",              // 第 1 行：表头
                "桥架200*100 10格",  // 第 2 行
                "1200",              // 数字 → "1200"（去尾零）
                "(blank)",           // 空行跳过
                "13.5",              // 小数 → "13.5"
                "2000mm");
            try
            {
                var values = ExcelColumnReader.ReadColumn(path, 1, 2); // 从第 2 行起，跳过表头
                Assert.Equal(new System.Collections.Generic.List<string>
                {
                    "桥架200*100 10格",
                    "1200",
                    "13.5",
                    "2000mm"
                }, values);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void ReadColumn_EmptyColumn_ReturnsEmpty()
        {
            string path = CreateTempWorkbook("a", "b", "c");
            try
            {
                var values = ExcelColumnReader.ReadColumn(path, 3, 1); // 第 3 列不存在
                Assert.Empty(values);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void CellToString_NumericTrimsTrailingZeros()
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("t");
            var r = sheet.CreateRow(0);
            r.CreateCell(0).SetCellValue(1200.0);
            r.CreateCell(1).SetCellValue(13.50);
            r.CreateCell(2).SetCellValue("桥架400*100 10格");

            Assert.Equal("1200", ExcelColumnReader.CellToString(r.GetCell(0)));
            Assert.Equal("13.5", ExcelColumnReader.CellToString(r.GetCell(1)));
            Assert.Equal("桥架400*100 10格", ExcelColumnReader.CellToString(r.GetCell(2)));
            Assert.Equal("", ExcelColumnReader.CellToString(null));
        }
    }
}
