using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Submission
{
    public static class SubmissionWorkbookWriter
    {
        public const string DefaultFileName = "UNCAD_Submissions.xlsx";
        public const string SheetName = "提交记录";
        public static readonly string[] Headers =
        {
            "机台ID", "设备名称", "盘柜类型", "电缆型号", "FR", "配电详情",
            "软管直径", "下游轴位", "上游轴位", "提交时间", "更新时间"
        };

        public static SubmissionWriteResult Upsert(string filePath, SubmissionRecord record,
            DateTimeOffset submittedNow)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("提交文件路径为空。", nameof(filePath));
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.MachineId) || string.IsNullOrWhiteSpace(record.DeviceName))
                throw new InvalidDataException("机台ID和设备名称不能为空。");

            string fullPath = Path.GetFullPath(filePath);
            string folder = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                throw new DirectoryNotFoundException("提交文件夹不存在: " + folder);

            IWorkbook workbook = null;
            FileStream updateLock = AcquireUpdateLock(fullPath);
            string temporary = Path.Combine(folder, "." + Path.GetFileName(fullPath)
                + "." + Guid.NewGuid().ToString("N") + ".tmp");
            string backup = temporary + ".bak";
            try
            {
                workbook = LoadOrCreate(fullPath);
                ISheet sheet = workbook.GetSheet(SheetName) ?? workbook.CreateSheet(SheetName);
                Dictionary<string, int> columns = EnsureHeader(workbook, sheet);
                var duplicateRows = new List<int>();
                string originalSubmitted = "";
                for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
                {
                    IRow row = sheet.GetRow(rowIndex);
                    if (row == null || !SameKey(row, columns, record)) continue;
                    duplicateRows.Add(rowIndex);
                    string value = CellText(row.GetCell(columns["提交时间"]));
                    if (originalSubmitted.Length == 0 && value.Length > 0) originalSubmitted = value;
                }
                for (int i = duplicateRows.Count - 1; i >= 0; i--) RemoveRow(sheet, duplicateRows[i]);

                string now = submittedNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                string submitted = originalSubmitted.Length > 0 ? originalSubmitted : now;
                IRow target = sheet.CreateRow(Math.Max(1, sheet.LastRowNum + 1));
                Set(target, columns, "机台ID", record.MachineId);
                Set(target, columns, "设备名称", record.DeviceName);
                Set(target, columns, "盘柜类型", record.PanelType);
                Set(target, columns, "电缆型号", record.Cable);
                Set(target, columns, "FR", record.Fr);
                Set(target, columns, "配电详情", record.Detail);
                Set(target, columns, "软管直径", record.Diameter);
                Set(target, columns, "下游轴位", record.DownstreamAxis);
                Set(target, columns, "上游轴位", record.UpstreamAxis);
                Set(target, columns, "提交时间", submitted);
                Set(target, columns, "更新时间", now);
                ApplyWidths(sheet, columns);

                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    workbook.Write(stream);
                workbook.Close();
                workbook = null;
                if (File.Exists(fullPath))
                {
                    File.Replace(temporary, fullPath, backup, true);
                    TryDelete(backup);
                    backup = "";
                }
                else File.Move(temporary, fullPath);

                return new SubmissionWriteResult
                {
                    ReplacedExisting = duplicateRows.Count > 0,
                    RemovedDuplicates = duplicateRows.Count,
                    FilePath = fullPath,
                    SubmittedAt = submitted,
                    UpdatedAt = now
                };
            }
            finally
            {
                workbook?.Close();
                updateLock.Dispose();
                TryDelete(temporary);
            }
        }

        private static FileStream AcquireUpdateLock(string workbookPath)
        {
            string lockPath = workbookPath + ".uncad.lock";
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    try { File.SetAttributes(lockPath, File.GetAttributes(lockPath) | FileAttributes.Hidden); }
                    catch { }
                    return stream;
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(150);
                }
            }
            throw new IOException("提交表正在被另一个 UNCAD 用户更新，请稍后重试。");
        }

        private static IWorkbook LoadOrCreate(string path)
        {
            if (!File.Exists(path)) return new XSSFWorkbook();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete)) return new XSSFWorkbook(stream);
        }

        private static Dictionary<string, int> EnsureHeader(IWorkbook workbook, ISheet sheet)
        {
            IRow header = sheet.GetRow(0);
            if (header == null || header.LastCellNum <= 0)
            {
                header = sheet.CreateRow(0);
                ICellStyle style = HeaderStyle(workbook);
                for (int i = 0; i < Headers.Length; i++)
                {
                    ICell cell = header.CreateCell(i);
                    cell.SetCellValue(Headers[i]);
                    cell.CellStyle = style;
                }
            }
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int column = 0; column < header.LastCellNum; column++)
            {
                string value = CellText(header.GetCell(column));
                if (value.Length == 0) continue;
                if (result.ContainsKey(value))
                    throw new InvalidDataException("提交表存在重复表头: " + value);
                result[value] = column;
            }
            string[] missing = Headers.Where(h => !result.ContainsKey(h)).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException("提交表缺少字段: " + string.Join("、", missing));
            return result;
        }

        private static ICellStyle HeaderStyle(IWorkbook workbook)
        {
            IFont font = workbook.CreateFont();
            font.IsBold = true;
            ICellStyle style = workbook.CreateCellStyle();
            style.SetFont(font);
            style.FillForegroundColor = IndexedColors.Grey25Percent.Index;
            style.FillPattern = FillPattern.SolidForeground;
            style.BorderBottom = BorderStyle.Thin;
            return style;
        }

        private static bool SameKey(IRow row, Dictionary<string, int> columns, SubmissionRecord record)
            => string.Equals(CellText(row.GetCell(columns["机台ID"])), record.MachineId.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(CellText(row.GetCell(columns["设备名称"])), record.DeviceName.Trim(), StringComparison.OrdinalIgnoreCase);

        private static void RemoveRow(ISheet sheet, int index)
        {
            IRow row = sheet.GetRow(index);
            if (row != null) sheet.RemoveRow(row);
            if (index < sheet.LastRowNum) sheet.ShiftRows(index + 1, sheet.LastRowNum, -1);
        }

        private static void Set(IRow row, Dictionary<string, int> columns, string header, string value)
            => row.CreateCell(columns[header]).SetCellValue((value ?? "").Trim());

        private static string CellText(ICell cell)
            => (cell?.ToString() ?? "").Trim();

        private static void ApplyWidths(ISheet sheet, Dictionary<string, int> columns)
        {
            foreach (KeyValuePair<string, int> column in columns)
            {
                int width = column.Key == "FR" || column.Key == "配电详情" || column.Key == "电缆型号"
                    ? 30 : column.Key.EndsWith("时间", StringComparison.Ordinal) ? 20 : 16;
                sheet.SetColumnWidth(column.Value, width * 256);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
