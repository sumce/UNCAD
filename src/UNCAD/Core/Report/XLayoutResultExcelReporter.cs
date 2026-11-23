using System;
using System.Globalization;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Dwg;

namespace UNCAD.Core.Report
{
    /// <summary>
    /// 把 XLAYOUT 结果报告写成独立 xlsx（D-055，结构克隆 XstsExcelReporter）：
    /// 清单结果 sheet（导入清单时逐条 Done/未参与排版/找不到）+ 排版图框 sheet。
    /// 临时文件+替换的原子写，半写失败不留半个 xlsx。
    /// </summary>
    public static class XLayoutResultExcelReporter
    {
        public static void WriteToFile(XLayoutResultReport report, string filePath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("结果报表路径不能为空。", nameof(filePath));
            string directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            IWorkbook workbook = CreateWorkbook(report);
            // 原子写：与 XstsExcelReporter 同款（tmp+Replace），防半写残留。
            string temporary = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create,
                    FileAccess.Write, FileShare.Read)) workbook.Write(stream);
                if (File.Exists(filePath))
                {
                    try { File.Replace(temporary, filePath, null, true); }
                    catch (PlatformNotSupportedException) { FallbackSwap(); }
                    catch (NotSupportedException) { FallbackSwap(); }
                }
                else File.Move(temporary, filePath);
            }
            catch
            {
                TryDeleteFile(temporary);
                throw;
            }
            finally { workbook.Close(); }

            void FallbackSwap()
            {
                File.Delete(filePath);
                File.Move(temporary, filePath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public static IWorkbook CreateWorkbook(XLayoutResultReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var workbook = new XSSFWorkbook();
            ICellStyle bodyStyle = workbook.CreateCellStyle();
            bodyStyle.WrapText = true;
            bodyStyle.VerticalAlignment = VerticalAlignment.Top;

            if (report.HasFilterList)
            {
                ISheet sheet = workbook.CreateSheet("清单结果");
                int row = 0;
                Set(sheet.CreateRow(row++), 0, "UNCAD XLAYOUT 清单结果", workbook, true);
                Set(sheet.CreateRow(row++), 0, "生成时间: " + report.GeneratedAt.ToString(
                    "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                Set(sheet.GetRow(row - 1), 2, "清单: "
                    + Path.GetFileName(report.FilterSourcePath));
                Set(sheet.CreateRow(row++), 0, "机台ID", workbook, true);
                Set(sheet.GetRow(row - 1), 1, "回路/设备名称", workbook, true);
                Set(sheet.GetRow(row - 1), 2, "状态", workbook, true);
                Set(sheet.GetRow(row - 1), 3, "图框句柄", workbook, true);
                Set(sheet.GetRow(row - 1), 4, "备注", workbook, true);
                for (int index = 0; index < report.FilterRows.Count; index++)
                {
                    XLayoutResultListRow item = report.FilterRows[index];
                    IRow current = sheet.CreateRow(row++);
                    Set(current, 0, item.MachineId);
                    Set(current, 1, item.CircuitName);
                    Set(current, 2, item.OutcomeText);
                    // 句柄是标识符（含纯数字形态），一律按文本写，防 Excel 数值化。
                    Set(current, 3, item.FrameHandles);
                    Set(current, 4, item.Note);
                }
                int[] widths = { 20, 26, 14, 22, 40 };
                for (int i = 0; i < widths.Length; i++)
                    sheet.SetColumnWidth(i, widths[i] * 256);
                // 表头在第 2 行（0 标题、1 生成时间），数据从第 3 行起。
                FormatTable(sheet, 2, 2 + report.FilterRows.Count, 5, bodyStyle);
            }

            ISheet frames = workbook.CreateSheet("排版图框");
            int frameRow = 0;
            Set(frames.CreateRow(frameRow++), 0, "UNCAD XLAYOUT 排版图框", workbook, true);
            Set(frames.CreateRow(frameRow++), 0, "生成时间: " + report.GeneratedAt.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Set(frames.CreateRow(frameRow++), 0, "机台ID", workbook, true);
            Set(frames.GetRow(frameRow - 1), 1, "回路/设备名称", workbook, true);
            Set(frames.GetRow(frameRow - 1), 2, "图框句柄", workbook, true);
            Set(frames.GetRow(frameRow - 1), 3, "备注", workbook, true);
            for (int index = 0; index < report.FrameRows.Count; index++)
            {
                XLayoutResultFrameRow item = report.FrameRows[index];
                IRow current = frames.CreateRow(frameRow++);
                Set(current, 0, item.MachineId);
                Set(current, 1, item.CircuitName);
                Set(current, 2, item.FrameHandle);
                Set(current, 3, item.Note);
            }
            int[] frameWidths = { 20, 26, 22, 40 };
            for (int i = 0; i < frameWidths.Length; i++)
                frames.SetColumnWidth(i, frameWidths[i] * 256);
            FormatTable(frames, 2, 2 + report.FrameRows.Count, 4, bodyStyle);
            return workbook;
        }

        private static void FormatTable(ISheet sheet, int headerRow, int lastDataRow,
            int columns, ICellStyle bodyStyle)
        {
            sheet.CreateFreezePane(0, headerRow + 1);
            if (lastDataRow > headerRow)
                sheet.SetAutoFilter(new CellRangeAddress(headerRow, lastDataRow, 0,
                    columns - 1));
            for (int row = headerRow + 1; row <= lastDataRow; row++)
            {
                IRow current = sheet.GetRow(row);
                if (current == null) continue;
                foreach (ICell cell in current.Cells) cell.CellStyle = bodyStyle;
            }
        }

        private static void Set(IRow row, int index, string value,
            IWorkbook workbook = null, bool bold = false)
        {
            ICell cell = row.CreateCell(index);
            cell.SetCellValue(value ?? "");
            if (!bold || workbook == null) return;
            ICellStyle style = BoldStyle(workbook);
            cell.CellStyle = style;
        }

        private static ICellStyle BoldStyle(IWorkbook workbook)
        {
            foreach (ICellStyle existing in GetStyles(workbook))
            {
                if (existing != null && existing.GetFont(workbook) is IFont font
                    && font.IsBold)
                    return existing;
            }
            IFont boldFont = workbook.CreateFont();
            boldFont.IsBold = true;
            ICellStyle style = workbook.CreateCellStyle();
            style.SetFont(boldFont);
            return style;
        }

        private static System.Collections.Generic.List<ICellStyle> GetStyles(
            IWorkbook workbook)
        {
            var styles = new System.Collections.Generic.List<ICellStyle>();
            short count = (short)workbook.NumCellStyles;
            for (short i = 0; i < count; i++)
            {
                try { styles.Add(workbook.GetCellStyleAt(i)); }
                catch { }
            }
            return styles;
        }
    }
}
