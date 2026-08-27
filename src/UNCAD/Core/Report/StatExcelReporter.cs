using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;

namespace UNCAD.Core.Report
{
    /// <summary>
    /// 统计报表导出（NPOI，纯 C# 可单测）：CableStatResult → xlsx 文件。
    /// 输出端以后要加 CSV/其他格式，新增一个 Reporter 即可，统计引擎不动。
    /// </summary>
    public static class StatExcelReporter
    {
        public static void WriteToFile(CableStatResult r, string filePath)
        {
            var wb = CreateWorkbook(r);
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    wb.Write(fs);
            }
            finally
            {
                wb.Close();
            }
        }

        public static IWorkbook CreateWorkbook(CableStatResult r)
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("统计");
            int rowIdx = 0;

            // 标题
            var title = sheet.CreateRow(rowIdx++);
            SetCell(title, 0, "UNCAD 统计报表", wb, true);
            var titleFont = wb.CreateFont();
            titleFont.IsBold = true;
            titleFont.FontHeightInPoints = 14;
            var titleStyle = wb.CreateCellStyle();
            titleStyle.SetFont(titleFont);
            title.GetCell(0).CellStyle = titleStyle;

            SetCell(sheet.CreateRow(rowIdx++), 0, "生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            rowIdx++; // 空行

            // 电缆长度
            SetCell(sheet.CreateRow(rowIdx++), 0, "电缆长度", wb, true);
            if (r.CableFormatted.Count > 0)
            {
                SetCell(sheet.CreateRow(rowIdx++), 0, TextFormatter.Join(r.CableFormatted, "+"));
                SetCell(sheet.CreateRow(rowIdx++), 0, "合计: " + TextFormatter.FormatNum(r.CableSum) + " M");
            }
            else
            {
                SetCell(sheet.CreateRow(rowIdx++), 0, "无电缆长度数据");
            }

            rowIdx++; // 空行

            // 桥架
            var header = sheet.CreateRow(rowIdx++);
            SetCell(header, 0, "桥架规格", wb, true);
            SetCell(header, 1, "格数明细", wb, true);
            SetCell(header, 2, "总格数", wb, true);
            SetCell(header, 3, "总长度(mm)", wb, true);
            SetCell(header, 4, "总长度(M)", wb, true);

            if (r.Bridges.Count == 0)
            {
                SetCell(sheet.CreateRow(rowIdx++), 0, "无桥架数据");
            }
            else
            {
                foreach (var b in r.Bridges)
                {
                    var row = sheet.CreateRow(rowIdx++);
                    SetCell(row, 0, b.Spec);
                    SetCell(row, 1, TextFormatter.Join(b.Grids.Select(TextFormatter.FormatNum).ToList(), "+"));
                    SetCell(row, 2, TextFormatter.FormatNum(b.TotalGrids));
                    SetCell(row, 3, TextFormatter.FormatNum(b.TotalMm));
                    SetCell(row, 4, TextFormatter.FormatNum(b.TotalM));
                }
            }

            rowIdx++; // 空行

            // 线管
            var conduitHeader = sheet.CreateRow(rowIdx++);
            SetCell(conduitHeader, 0, "线管规格", wb, true);
            SetCell(conduitHeader, 1, "长度明细(M)", wb, true);
            SetCell(conduitHeader, 2, "总长度(M)", wb, true);
            if (r.Conduits.Count == 0)
            {
                SetCell(sheet.CreateRow(rowIdx++), 0, "无线管数据");
            }
            else
            {
                foreach (var c in r.Conduits)
                {
                    var row = sheet.CreateRow(rowIdx++);
                    SetCell(row, 0, c.Spec);
                    SetCell(row, 1, TextFormatter.Join(c.LengthsMm
                        .Select(v => TextFormatter.FormatNum(v / 1000.0)).ToList(), "+"));
                    SetCell(row, 2, TextFormatter.FormatNum(c.TotalM));
                }
            }

            // 列宽
            sheet.SetColumnWidth(0, 22 * 256);
            sheet.SetColumnWidth(1, 30 * 256);
            sheet.SetColumnWidth(2, 10 * 256);
            sheet.SetColumnWidth(3, 14 * 256);
            sheet.SetColumnWidth(4, 14 * 256);

            return wb;
        }

        private static void SetCell(IRow row, int idx, string value, IWorkbook wb = null, bool bold = false)
        {
            var cell = row.CreateCell(idx);
            cell.SetCellValue(value ?? "");
            if (bold && wb != null)
            {
                var style = wb.CreateCellStyle();
                var font = wb.CreateFont();
                font.IsBold = true;
                style.SetFont(font);
                cell.CellStyle = style;
            }
        }
    }
}
