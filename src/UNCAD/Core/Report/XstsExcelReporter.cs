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
            Set(sheet.GetRow(row - 1), 1, report.FrameCount.ToString(CultureInfo.InvariantCulture));
            Set(sheet.CreateRow(row++), 0, "机台 ID", workbook, true);
            Set(sheet.GetRow(row - 1), 1, "已选回路", workbook, true);
            Set(sheet.GetRow(row - 1), 2, "应有回路", workbook, true);
            Set(sheet.GetRow(row - 1), 3, "缺少回路", workbook, true);
            foreach (XstsMachineSummary item in report.Machines)
            {
                IRow current = sheet.CreateRow(row++);
                Set(current, 0, item.MachineId);
                Set(current, 1, item.SelectedCircuitCount.ToString(CultureInfo.InvariantCulture));
                Set(current, 2, item.ExpectedCircuitCount.ToString(CultureInfo.InvariantCulture));
                Set(current, 3, item.MissingText);
            }
            Set(sheet.CreateRow(row++), 0, "合计", workbook, true);
            Set(sheet.GetRow(row - 1), 1, report.SelectedCircuitCount.ToString(CultureInfo.InvariantCulture));
            Set(sheet.GetRow(row - 1), 2, report.ExpectedCircuitCount.ToString(CultureInfo.InvariantCulture));
            Set(sheet.GetRow(row - 1), 3, report.MissingCircuitCount.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < 4; i++) sheet.SetColumnWidth(i, (i == 3 ? 45 : 18) * 256);
            return workbook;
        }

        private static void Set(IRow row, int index, string value, IWorkbook workbook = null,
            bool bold = false)
        {
            ICell cell = row.CreateCell(index);
            cell.SetCellValue(value ?? "");
            if (!bold || workbook == null) return;
            IFont font = workbook.CreateFont();
            font.IsBold = true;
            ICellStyle style = workbook.CreateCellStyle();
            style.SetFont(font);
            cell.CellStyle = style;
        }
    }
}
