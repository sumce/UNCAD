using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Submission
{
    /// <summary>将已绘制清单写入固定 BOQ 模板，每个项目编码只更新工程量列。</summary>
    public static class BoqWorkbookWriter
    {
        public const string SheetName = "Sheet1";
        public const string OutputPrefix = "[BOQ]";
        private const int FirstDeviceColumn = 11;
        private const int DeviceHeaderGroupRow = 3;
        private const int DeviceHeaderNamesRow = 4;
        public const string TemplateFileName = "BOQ模板.xlsx";
        private static readonly Regex ItemCodePattern = new Regex(
            @"^\d+\.\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string ResolveTemplatePath()
        {
            string assemblyPath = typeof(BoqWorkbookWriter).Assembly.Location;
            return ResolveTemplatePath(Path.GetDirectoryName(assemblyPath));
        }

        public static string ResolveTemplatePath(string pluginDirectory)
        {
            if (string.IsNullOrWhiteSpace(pluginDirectory))
                throw new DirectoryNotFoundException("无法确定 UNCAD 插件目录，不能定位 BOQ 模板。");
            string directory = Path.GetFullPath(pluginDirectory);
            string[] candidates =
            {
                Path.Combine(directory, "BOQ_Template.xlsx"),
                Path.Combine(directory, TemplateFileName)
            };
            string path = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(path))
                throw new FileNotFoundException(
                    "找不到固定 BOQ 模板。已搜索插件目录：" + directory
                    + "。请确认其中存在 BOQ_Template.xlsx 或 BOQ模板.xlsx。",
                    candidates[0]);
            return Path.GetFullPath(path);
        }

        public static string BuildFileName(string machineId)
        {
            string value = (machineId ?? "").Trim();
            if (value.Length == 0) throw new ArgumentException("机台ID不能为空。", nameof(machineId));
            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || value == "." || value == ".." || value.EndsWith(".", StringComparison.Ordinal))
                throw new InvalidDataException("机台ID不能作为文件夹或文件名：" + value);
            return OutputPrefix + value + ".xlsx";
        }

        public static string BuildTargetPath(string outputRoot, string machineId)
        {
            if (string.IsNullOrWhiteSpace(outputRoot))
                throw new ArgumentException("BOQ 输出文件夹不能为空。", nameof(outputRoot));
            string fileName = BuildFileName(machineId);
            string machineFolder = Path.Combine(Path.GetFullPath(outputRoot), (machineId ?? "").Trim());
            Directory.CreateDirectory(machineFolder);
            return Path.Combine(machineFolder, fileName);
        }

        public static void ValidateTargetForUpdate(string targetPath, string templatePath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("BOQ 输出文件路径为空。", nameof(targetPath));
            if (!File.Exists(targetPath) && (string.IsNullOrWhiteSpace(templatePath)
                || !File.Exists(templatePath)))
                throw new FileNotFoundException("BOQ 模板不存在。", templatePath);
            string folder = Path.GetDirectoryName(Path.GetFullPath(targetPath));
            if (string.IsNullOrWhiteSpace(folder))
                throw new DirectoryNotFoundException("BOQ 输出文件夹不存在。");
            Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, ".boq-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); } catch { }
            }
            IWorkbook workbook = Load(targetPath, templatePath);
            workbook.Close();
        }

        public static void Write(string targetPath, string templatePath,
            IEnumerable<SubmissionRecord> records)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("BOQ 输出文件路径为空。", nameof(targetPath));
            List<SubmissionRecord> sourceRecords = (records ?? Enumerable.Empty<SubmissionRecord>())
                .Where(record => record != null).ToList();
            string[] machineIds = sourceRecords.Select(record => (record.MachineId ?? "").Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (machineIds.Length > 1)
                throw new InvalidDataException("One BOQ workbook cannot mix multiple machine IDs.");
            if (sourceRecords.Any(record => string.IsNullOrWhiteSpace(record.MachineId)))
                throw new InvalidDataException("BOQ machine ID is required for quantity attribution.");
            if (sourceRecords.Any(record => string.IsNullOrWhiteSpace(record.DeviceName)))
                throw new InvalidDataException("BOQ device name is required for quantity attribution.");
            if (!sourceRecords.Any(record => record.Materials != null && record.Materials.Count > 0))
                throw new InvalidDataException("没有可写入 BOQ 的清单材料。");
            string fullPath = Path.GetFullPath(targetPath);
            string folder = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(folder);
            string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            IWorkbook workbook = null;
            FileStream updateLock = null;
            try
            {
                updateLock = AcquireUpdateLock(fullPath);
                workbook = Load(fullPath, templatePath);
                ISheet sheet = workbook.GetSheet(SheetName) ?? workbook.GetSheetAt(0);
                Dictionary<string, int> rows = FindItemRows(sheet);
                Dictionary<string, int> deviceColumns = FindDeviceColumns(sheet);
                foreach (IGrouping<string, SubmissionRecord> deviceGroup in sourceRecords
                    .Where(record => record != null)
                    .GroupBy(record => (record.DeviceName ?? "").Trim(),
                        StringComparer.OrdinalIgnoreCase))
                {
                    if (deviceGroup.Key.Length == 0)
                        throw new InvalidDataException("BOQ 设备名称不能为空，不能创建设备列。");
                    int deviceColumn = GetOrCreateDeviceColumn(sheet, deviceColumns, deviceGroup.Key);
                    Dictionary<string, decimal> deviceQuantities = ReadQuantities(
                        deviceGroup, rows);
                    // 只清空当前设备列；同一机台其他设备列必须完整保留。
                    foreach (KeyValuePair<string, int> item in rows)
                        SetNumber(GetOrCreateCell(sheet.GetRow(item.Value), deviceColumn), 0m);
                    foreach (KeyValuePair<string, decimal> item in deviceQuantities)
                        SetNumber(GetOrCreateCell(sheet.GetRow(rows[item.Key]), deviceColumn), item.Value);
                }
                int lastDeviceColumn = deviceColumns.Values.Max();
                string firstColumn = ColumnName(FirstDeviceColumn);
                string lastColumn = ColumnName(lastDeviceColumn);
                foreach (KeyValuePair<string, int> item in rows)
                {
                    IRow row = GetOrCreateRow(sheet, item.Value);
                    ICell totalCell = GetOrCreateCell(row, 4);
                    totalCell.SetCellFormula(
                        "SUM(" + firstColumn + (item.Value + 1) + ":" + lastColumn
                        + (item.Value + 1) + ")");
                    // NPOI only writes the formula text.  SetCellValue on a formula cell
                    // stores its pre-calculated result, so viewers that do not recalculate
                    // (including WPS preview/data_only readers) still see the real total.
                    totalCell.SetCellValue((double)ReadDeviceTotal(row, deviceColumns.Values));
                }
                ((XSSFWorkbook)workbook).SetForceFormulaRecalculation(true);

                using (var stream = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                    workbook.Write(stream);
                workbook.Close();
                workbook = null;
                Replace(fullPath, temporary);
                temporary = null;
            }
            finally
            {
                workbook?.Close();
                if (updateLock != null)
                {
                    updateLock.Dispose();
                    TryDelete(fullPath + ".boq.lock");
                }
                TryDelete(temporary);
            }
        }

        private static IWorkbook Load(string targetPath, string templatePath)
        {
            string source = File.Exists(targetPath) ? targetPath : templatePath;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                throw new FileNotFoundException("BOQ 模板不存在。", source);
            using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                return new XSSFWorkbook(stream);
        }

        private static Dictionary<string, int> FindDeviceColumns(ISheet sheet)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            IRow header = GetOrCreateRow(sheet, DeviceHeaderNamesRow);
            int lastCell = Math.Max(FirstDeviceColumn, (int)header.LastCellNum);
            for (int column = FirstDeviceColumn; column < lastCell; column++)
            {
                string device = CellText(header.GetCell(column));
                if (device.Length == 0) continue;
                if (result.ContainsKey(device))
                    throw new InvalidDataException("BOQ 模板存在重复设备列：" + device);
                result[device] = column;
            }
            return result;
        }

        private static int GetOrCreateDeviceColumn(ISheet sheet,
            Dictionary<string, int> deviceColumns, string deviceName)
        {
            if (deviceColumns.TryGetValue(deviceName, out int existing)) return existing;
            int column = FirstDeviceColumn;
            while (deviceColumns.Values.Contains(column)) column++;
            IRow groupHeader = GetOrCreateRow(sheet, DeviceHeaderGroupRow);
            IRow namesHeader = GetOrCreateRow(sheet, DeviceHeaderNamesRow);
            ICell groupCell = groupHeader.GetCell(column) ?? groupHeader.CreateCell(column);
            ICell nameCell = namesHeader.GetCell(column) ?? namesHeader.CreateCell(column);
            ICell groupStyle = groupHeader.GetCell(10);
            ICell nameStyle = namesHeader.GetCell(10);
            if (groupStyle != null) groupCell.CellStyle = groupStyle.CellStyle;
            if (nameStyle != null) nameCell.CellStyle = nameStyle.CellStyle;
            nameCell.SetCellValue(deviceName);
            groupCell.SetCellValue("各设备工程量");
            sheet.SetColumnWidth(column, Math.Max((int)sheet.GetColumnWidth(10), 18 * 256));
            deviceColumns[deviceName] = column;
            int lastColumn = deviceColumns.Values.Max();
            for (int mergeIndex = sheet.NumMergedRegions - 1; mergeIndex >= 0; mergeIndex--)
            {
                NPOI.SS.Util.CellRangeAddress range = sheet.GetMergedRegion(mergeIndex);
                if (range.FirstRow == DeviceHeaderGroupRow
                    && range.LastRow == DeviceHeaderGroupRow
                    && range.FirstColumn >= FirstDeviceColumn)
                    sheet.RemoveMergedRegion(mergeIndex);
            }
            if (lastColumn > FirstDeviceColumn)
                sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(
                    DeviceHeaderGroupRow, DeviceHeaderGroupRow,
                    FirstDeviceColumn, lastColumn));
            return column;
        }

        private static Dictionary<string, decimal> ReadQuantities(
            IEnumerable<SubmissionRecord> records, Dictionary<string, int> rows)
        {
            var quantities = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (SubmissionRecord record in records)
            {
                foreach (SubmissionMaterial material in record.Materials
                    ?? new List<SubmissionMaterial>())
                {
                    // Legacy CAD tables sometimes store the fixed code in NO. and leave
                    // 项次编码 empty. Prefer the explicit code, but fall back on a
                    // non-empty number rather than rejecting an otherwise valid row.
                    string code = (material.Code ?? "").Trim();
                    if (code.Length == 0) code = (material.Number ?? "").Trim();
                    if (code.Length == 0)
                        throw new InvalidDataException("BOQ 材料缺少项目编码，不能猜测固定清单项目。");
                    if (!rows.ContainsKey(code))
                        throw new InvalidDataException("固定 BOQ 模板不存在项目编码：" + code);
                    if (!TryParseQuantity(material.Quantity, out decimal quantity))
                        throw new InvalidDataException("BOQ 项目 " + code + " 的工程量不是有效数字："
                            + (material.Quantity ?? ""));
                    if (quantity < 0m)
                        throw new InvalidDataException("BOQ material quantity cannot be negative: "
                            + code + " = " + material.Quantity);
                    quantities[code] = quantities.TryGetValue(code, out decimal old)
                        ? old + quantity : quantity;
                }
            }
            if (quantities.Count == 0)
                throw new InvalidDataException("设备没有可写入 BOQ 的清单材料。");
            return quantities;
        }

        private static IRow GetOrCreateRow(ISheet sheet, int rowIndex)
            => sheet.GetRow(rowIndex) ?? sheet.CreateRow(rowIndex);

        private static ICell GetOrCreateCell(IRow row, int column)
            => row.GetCell(column) ?? row.CreateCell(column);

        private static decimal ReadDeviceTotal(IRow row, IEnumerable<int> columns)
        {
            decimal total = 0m;
            foreach (int column in columns ?? Enumerable.Empty<int>())
            {
                ICell cell = row?.GetCell(column);
                if (cell == null) continue;
                if (cell.CellType == CellType.Numeric)
                {
                    total += (decimal)cell.NumericCellValue;
                    continue;
                }
                // Device columns are normally numeric, but retain cached numeric values if
                // a workbook was edited by another spreadsheet application.
                if (cell.CellType == CellType.Formula
                    && cell.CachedFormulaResultType == CellType.Numeric)
                    total += (decimal)cell.NumericCellValue;
            }
            return total;
        }

        private static string ColumnName(int column)
        {
            string result = "";
            int value = column + 1;
            while (value > 0)
            {
                int remainder = (value - 1) % 26;
                result = (char)('A' + remainder) + result;
                value = (value - 1) / 26;
            }
            return result;
        }

        private static Dictionary<string, int> FindItemRows(ISheet sheet)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int rowIndex = 0; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                string code = CellText(row?.GetCell(0));
                if (!ItemCodePattern.IsMatch(code)) continue;
                if (result.ContainsKey(code))
                    throw new InvalidDataException("BOQ 模板存在重复项目编码：" + code);
                result[code] = rowIndex;
            }
            if (result.Count == 0)
                throw new InvalidDataException("BOQ 模板没有找到项目编码明细行。");
            return result;
        }

        private static bool TryParseQuantity(string value, out decimal quantity)
        {
            string text = (value ?? "").Trim();
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity)
                || decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out quantity);
        }

        private static void SetNumber(ICell cell, decimal value)
        {
            if (cell == null) return;
            cell.SetCellValue((double)value);
        }

        private static string CellText(ICell cell)
            => cell == null ? "" : (cell.ToString() ?? "").Trim();

        private static void Replace(string target, string temporary)
        {
            if (!File.Exists(target))
            {
                File.Move(temporary, target);
                return;
            }

            string backup = target + ".bak";
            try
            {
                File.Replace(temporary, target, backup, true);
                TryDelete(backup);
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (NotSupportedException) { }
            catch (IOException) { }

            bool movedOriginal = false;
            try
            {
                File.Move(target, backup);
                movedOriginal = true;
                File.Move(temporary, target);
                TryDelete(backup);
            }
            catch
            {
                if (movedOriginal && !File.Exists(target) && File.Exists(backup))
                    File.Move(backup, target);
                throw;
            }
        }

        private static FileStream AcquireUpdateLock(string workbookPath)
        {
            string lockPath = workbookPath + ".boq.lock";
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    return new FileStream(lockPath, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    if (attempt < 19) Thread.Sleep(150);
                }
            }
            throw new IOException("BOQ 正在被另一个 UNCAD 用户更新，请稍后重试。");
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
