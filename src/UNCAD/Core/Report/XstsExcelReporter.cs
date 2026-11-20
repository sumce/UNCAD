using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;

namespace UNCAD.Core.Report
{
    /// <summary>Writes the XSTS circuit coverage report to a standalone xlsx file.</summary>
    public static class XstsExcelReporter
    {
        public static void WriteToFile(XstsReport report, string filePath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("统计报表路径不能为空。", nameof(filePath));
            string directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            IWorkbook workbook = CreateWorkbook(report);
            // 3.2.0.6（审计修复）：临时文件+替换的原子写——此前 FileMode.Create
            // 直写最终目标（全仓唯一一个直写目标的导出器），NPOI 写到一半失败
            //（磁盘满/杀毒拦截）会留下半个 xlsx，与失败文案"未生成 Excel"矛盾。
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

        public static IWorkbook CreateWorkbook(XstsReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var workbook = new XSSFWorkbook();
            ICellStyle bodyStyle = workbook.CreateCellStyle();
            bodyStyle.WrapText = true;
            bodyStyle.VerticalAlignment = VerticalAlignment.Top;
            ISheet sheet = workbook.CreateSheet("回路统计");
            int row = 0;
            Set(sheet.CreateRow(row++), 0, "UNCAD XSTS 机台回路统计", workbook, true);
            Set(sheet.CreateRow(row++), 0, "生成时间: " + DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Set(sheet.CreateRow(row++), 0, "框选图框数量");
            Set(sheet.GetRow(row - 1), 1, report.FrameCount);
            Set(sheet.GetRow(row - 1), 2, "机台数量");
            Set(sheet.GetRow(row - 1), 3, report.Machines.Count);
            Set(sheet.CreateRow(row++), 0, "回路基准状态");
            Set(sheet.GetRow(row - 1), 1, ExpectedStatusText(report));
            Set(sheet.GetRow(row - 1), 2, report.ExpectedDataDetail);
            Set(sheet.CreateRow(row++), 0, "机台 ID", workbook, true);
            Set(sheet.GetRow(row - 1), 1, "批次", workbook, true);
            Set(sheet.GetRow(row - 1), 2, "序号", workbook, true);
            Set(sheet.GetRow(row - 1), 3, "已选回路", workbook, true);
            Set(sheet.GetRow(row - 1), 4, "应有回路", workbook, true);
            Set(sheet.GetRow(row - 1), 5, "缺少回路", workbook, true);
            Set(sheet.GetRow(row - 1), 6, "多出回路", workbook, true);
            foreach (XstsMachineSummary item in report.Machines)
            {
                IRow current = sheet.CreateRow(row++);
                Set(current, 0, item.MachineId);
                Set(current, 1, item.Batch);
                Set(current, 2, item.Seq);
                Set(current, 3, item.SelectedCircuitCount);
                if (item.ExpectedDataAvailable) Set(current, 4, item.ExpectedCircuitCount);
                else Set(current, 4, item.ExpectedCircuitText);
                Set(current, 5, item.MissingText);
                Set(current, 6, item.UnexpectedText);
            }
            Set(sheet.CreateRow(row++), 0, "合计", workbook, true);
            Set(sheet.GetRow(row - 1), 3, report.SelectedCircuitCount);
            if (report.ExpectedDataAvailable && !report.HasUnknownMachineBaseline)
                Set(sheet.GetRow(row - 1), 4, report.ExpectedCircuitCount);
            else Set(sheet.GetRow(row - 1), 4, report.ExpectedCircuitText);
            Set(sheet.GetRow(row - 1), 5, report.MissingCircuitText);
            Set(sheet.GetRow(row - 1), 6, report.UnexpectedCircuitText);

            ISheet frames = workbook.CreateSheet("图框明细");
            IRow frameHeader = frames.CreateRow(0);
            string[] frameColumns = new[] { "机台 ID", "回路名称", "图框ID" }
                .Concat(report.MaterialColumns.SelectMany(column =>
                    new[] { column.ModelHeader, column.QuantityHeader })).ToArray();
            ICellStyle frameHeaderStyle = workbook.CreateCellStyle();
            frameHeaderStyle.CloneStyleFrom(BoldStyle(workbook));
            frameHeaderStyle.WrapText = true;
            frameHeader.HeightInPoints = 36;
            for (int index = 0; index < frameColumns.Length; index++)
            {
                Set(frameHeader, index, frameColumns[index], workbook, true);
                frameHeader.GetCell(index).CellStyle = frameHeaderStyle;
            }
            IDrawing frameNotes = frames.CreateDrawingPatriarch();
            ICreationHelper helper = workbook.GetCreationHelper();
            for (int index = 0; index < report.Frames.Count; index++)
            {
                XstsFrameDetail item = report.Frames[index];
                IRow frameRow = frames.CreateRow(index + 1);
                Set(frameRow, 0, item.MachineId);
                Set(frameRow, 1, item.CircuitName);
                // Handles are identifiers, including digit-only ones; keep them as text.
                Set(frameRow, 2, item.FrameHandle);
                for (int column = 0; column < item.Cells.Count; column++)
                {
                    XstsFrameItem material = item.Cells[column];
                    int modelColumn = 3 + column * 2;
                    Set(frameRow, modelColumn, material?.Model ?? "");
                    Set(frameRow, modelColumn + 1, material?.Quantity ?? "");
                    if (material == null) continue;
                    IClientAnchor anchor = helper.CreateClientAnchor();
                    anchor.Col1 = modelColumn;
                    anchor.Col2 = modelColumn + 4;
                    anchor.Row1 = index + 1;
                    anchor.Row2 = index + 11;
                    IComment note = frameNotes.CreateCellComment(anchor);
                    note.Author = "UNCAD";
                    note.String = helper.CreateRichTextString(material.DetailText);
                    frameRow.GetCell(modelColumn).CellComment = note;
                }
                frameRow.HeightInPoints = 32;
            }
            int[] identityWidths = { 18, 24, 14 };
            for (int index = 0; index < frameColumns.Length; index++)
                frames.SetColumnWidth(index, (index < 3 ? identityWidths[index]
                    : (index - 3) % 2 == 0 ? 38 : 18) * 256);
            FormatTable(frames, 0, report.Frames.Count, frameColumns.Length, bodyStyle);
            frames.CreateFreezePane(3, 1);

            ISheet issues = workbook.CreateSheet("异常图框");
            IRow issueHeader = issues.CreateRow(0);
            Set(issueHeader, 0, "图框ID", workbook, true);
            Set(issueHeader, 1, "机台 ID", workbook, true);
            Set(issueHeader, 2, "回路名称", workbook, true);
            Set(issueHeader, 3, "问题", workbook, true);
            for (int index = 0; index < report.Issues.Count; index++)
            {
                XstsFrameIssue issue = report.Issues[index];
                IRow issueRow = issues.CreateRow(index + 1);
                Set(issueRow, 0, issue.FrameIdText);
                Set(issueRow, 1, issue.MachineId);
                Set(issueRow, 2, issue.CircuitName);
                Set(issueRow, 3, issue.Description);
            }
            for (int i = 0; i < 7; i++)
                sheet.SetColumnWidth(i, (i >= 5 ? 38 : i == 0 ? 18 : 14) * 256);
            for (int i = 0; i < 4; i++) issues.SetColumnWidth(i, (i == 3 ? 42 : 18) * 256);
            FormatTable(sheet, 4, 4 + report.Machines.Count, 7, bodyStyle);
            FormatTable(issues, 0, issues.LastRowNum, 4, bodyStyle);
            return workbook;
        }

        private static void FormatTable(ISheet sheet, int headerRow, int lastDataRow,
            int columns, ICellStyle bodyStyle)
        {
            sheet.CreateFreezePane(0, headerRow + 1);
            if (lastDataRow > headerRow)
                sheet.SetAutoFilter(new CellRangeAddress(headerRow, lastDataRow, 0, columns - 1));
            for (int row = headerRow + 1; row <= lastDataRow; row++)
                foreach (ICell cell in sheet.GetRow(row).Cells)
                    cell.CellStyle = bodyStyle;
        }

        private static string ExpectedStatusText(XstsReport report)
        {
            switch (report.ExpectedDataStatus)
            {
                case XstsExpectedDataStatus.Available:
                    return report.HasUnknownMachineBaseline
                        ? "已读取，但部分机台不存在于基准"
                        : "已读取";
                case XstsExpectedDataStatus.NotConfigured: return "未配置，无法判断缺少回路";
                case XstsExpectedDataStatus.FileNotFound: return "文件不存在，无法判断缺少回路";
                default: return "读取失败，无法判断缺少回路";
            }
        }

        private static void Set(IRow row, int index, string value, IWorkbook workbook = null,
            bool bold = false)
        {
            ICell cell = row.CreateCell(index);
            cell.SetCellValue(value ?? "");
            ApplyBold(cell, workbook, bold);
        }

        private static void Set(IRow row, int index, int value, IWorkbook workbook = null,
            bool bold = false)
        {
            ICell cell = row.CreateCell(index);
            cell.SetCellValue(value);
            ApplyBold(cell, workbook, bold);
        }

        private static void ApplyBold(ICell cell, IWorkbook workbook, bool bold)
        {
            if (!bold || workbook == null) return;
            // NPOI caps a workbook at 64k cell styles; reuse one shared bold
            // style per workbook instead of creating one for every cell.
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
