using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Excel
{
    /// <summary>
    /// 读取 Excel 列数据（NPOI，纯 C# 可单测）。
    /// 用于 UNC_FILL：把 Excel 某一列的值按行序取出来，替换图纸中的文字。
    /// </summary>
    public static class ExcelColumnReader
    {
        /// <summary>
        /// 读取第一张工作表的指定列（1 基，1=A），从 startRow（1 基）开始。
        /// 跳过空白行/空单元格，返回去首尾空格后的非空文本列表。
        /// </summary>
        public static List<string> ReadColumn(string filePath, int col1Based, int startRow1Based)
        {
            var result = new List<string>();
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var wb = new XSSFWorkbook(fs);
                var sheet = wb.GetSheetAt(0);
                int col = Math.Max(0, col1Based - 1);
                int start = Math.Max(0, startRow1Based - 1);
                for (int r = start; r <= sheet.LastRowNum; r++)
                {
                    var row = sheet.GetRow(r);
                    if (row == null) continue;
                    string v = CellToString(row.GetCell(col));
                    if (!string.IsNullOrEmpty(v)) result.Add(v);
                }
            }
            return result;
        }

        /// <summary>单元格 → 文本。数字去尾零（1200 → "1200"、13.5 → "13.5"），公式取缓存结果。</summary>
        public static string CellToString(ICell cell)
        {
            if (cell == null) return "";
            switch (cell.CellType)
            {
                case CellType.String:
                    return cell.StringCellValue ?? "";
                case CellType.Numeric:
                    if (DateUtil.IsCellDateFormatted(cell))
                        return cell.DateCellValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return NumericToString(cell.NumericCellValue);
                case CellType.Boolean:
                    return cell.BooleanCellValue ? "TRUE" : "FALSE";
                case CellType.Formula:
                    // 读缓存的计算结果（NPOI 不重新计算公式）
                    switch (cell.CachedFormulaResultType)
                    {
                        case CellType.String: return cell.StringCellValue ?? "";
                        case CellType.Numeric: return NumericToString(cell.NumericCellValue);
                        case CellType.Boolean: return cell.BooleanCellValue ? "TRUE" : "FALSE";
                        default: return "";
                    }
                case CellType.Blank:
                default:
                    return "";
            }
        }

        private static string NumericToString(double d)
        {
            return Math.Abs(d - Math.Floor(d)) < 1e-9
                ? d.ToString("0", CultureInfo.InvariantCulture)
                : d.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
