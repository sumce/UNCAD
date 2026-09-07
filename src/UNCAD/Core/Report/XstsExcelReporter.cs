using System;
using System.Globalization;
using System.IO;
using NPOI.SS.UserModel;
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
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write,
                    FileShare.Read)) workbook.Write(stream);
            }
            finally { workbook.Close(); }
        }

        public static IWorkbook CreateWorkbook(XstsReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("回路统计");
            int row = 0;
            Set(sheet.CreateRow(row++), 0, "UNCAD XSTS 机台回路统计", workbook, true);
            Set(sheet.CreateRow(row++), 0, "生成时间: " + DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Set(sheet.CreateRow(row++), 0, "框选机台数量");
            Set(sheet.GetRow(row - 1), 1, report.FrameCount);
            Set(sheet.CreateRow(row++), 0, "回路基准状态");
            Set(sheet.GetRow(row - 1), 1, ExpectedStatusText(report));
            Set(sheet.GetRow(row - 1), 2, report.ExpectedDataDetail);
            Set(sheet.CreateRow(row++), 0, "机台 ID", workbook, true);
            Set(sheet.GetRow(row - 1), 1, "已选回路", workbook, true);
            Set(sheet.GetRow(row - 1), 2, "应有回路", workbook, true);
            Set(sheet.GetRow(row - 1), 3, "缺少回路", workbook, true);
            Set(sheet.GetRow(row - 1), 4, "多出回路", workbook, true);
            foreach (XstsMachineSummary item in report.Machines)
            {
                IRow current = sheet.CreateRow(row++);
                Set(current, 0, item.MachineId);
                Set(current, 1, item.SelectedCircuitCount);
                if (item.ExpectedDataAvailable) Set(current, 2, item.ExpectedCircuitCount);
                else Set(current, 2, item.ExpectedCircuitText);
                Set(current, 3, item.MissingText);
                Set(current, 4, item.UnexpectedText);
            }
            Set(sheet.CreateRow(row++), 0, "合计", workbook, true);
            Set(sheet.GetRow(row - 1), 1, report.SelectedCircuitCount);
            if (report.ExpectedDataAvailable && !report.HasUnknownMachineBaseline)
                Set(sheet.GetRow(row - 1), 2, report.ExpectedCircuitCount);
            else Set(sheet.GetRow(row - 1), 2, report.ExpectedCircuitText);
            Set(sheet.GetRow(row - 1), 3, report.MissingCircuitText);
            Set(sheet.GetRow(row - 1), 4, report.UnexpectedCircuitText);

            ISheet issues = workbook.CreateSheet("异常图框");
            IRow issueHeader = issues.CreateRow(0);
            Set(issueHeader, 0, "图框序号", workbook, true);
            Set(issueHeader, 1, "机台 ID", workbook, true);
            Set(issueHeader, 2, "回路名称", workbook, true);
            Set(issueHeader, 3, "问题", workbook, true);
            for (int index = 0; index < report.Issues.Count; index++)
            {
                XstsFrameIssue issue = report.Issues[index];
                IRow issueRow = issues.CreateRow(index + 1);
                Set(issueRow, 0, issue.FrameNumber == 0 ? "" : issue.FrameNumber.ToString(
                    CultureInfo.InvariantCulture));
                Set(issueRow, 1, issue.MachineId);
                Set(issueRow, 2, issue.CircuitName);
                Set(issueRow, 3, issue.Description);
            }
            for (int i = 0; i < 5; i++) sheet.SetColumnWidth(i, (i >= 3 ? 38 : 18) * 256);
            for (int i = 0; i < 4; i++) issues.SetColumnWidth(i, (i == 3 ? 42 : 18) * 256);
            return workbook;
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
