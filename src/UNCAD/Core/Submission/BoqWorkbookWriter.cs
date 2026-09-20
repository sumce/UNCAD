using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UNCAD.Core.IO;
using UNCAD.Core.Text;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Submission
{
    internal sealed class BoqExternalModificationException : IOException
    {
        public BoqExternalModificationException(string message) : base(message) { }
        public BoqExternalModificationException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    internal sealed class BoqWorkbookRevision
    {
        public BoqWorkbookRevision(string fullPath, bool exists, byte[] fingerprint)
        {
            FullPath = fullPath;
            Exists = exists;
            Fingerprint = fingerprint;
        }

        public string FullPath { get; }
        public bool Exists { get; }
        public byte[] Fingerprint { get; }
    }

    internal enum BoqWorkbookWriteState
    {
        Untouched,
        Replaced,
        Restored
    }

    /// <summary>Reports whether a failed BOQ write still needs batch-level recovery.</summary>
    internal sealed class BoqWorkbookWriteProgress
    {
        public BoqWorkbookWriteState State { get; private set; }
        public byte[] WrittenFingerprint { get; private set; }

        internal void MarkReplaced(byte[] fingerprint)
        {
            State = BoqWorkbookWriteState.Replaced;
            WrittenFingerprint = fingerprint;
        }

        internal void MarkRestored()
        {
            State = BoqWorkbookWriteState.Restored;
            WrittenFingerprint = null;
        }
    }

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

        internal static BoqWorkbookRevision CaptureRevision(string targetPath)
        {
            string fullPath = Path.GetFullPath(targetPath);
            bool exists = File.Exists(fullPath);
            return new BoqWorkbookRevision(fullPath, exists,
                exists ? ComputeFingerprint(fullPath) : null);
        }

        internal static void ValidateRevision(BoqWorkbookRevision revision,
            string phase = "确认期间")
        {
            if (revision == null) throw new ArgumentNullException(nameof(revision));
            bool exists = File.Exists(revision.FullPath);
            if (revision.Exists && !exists)
                throw new BoqExternalModificationException(
                    "BOQ 文件在" + phase + "被删除，图纸和清单均未修改。");
            if (!revision.Exists && exists)
                throw new BoqExternalModificationException(
                    "BOQ 文件在" + phase + "被其他程序创建，未覆盖该文件。");
            if (revision.Exists
                && !SameFingerprint(revision.Fingerprint,
                    ComputeFingerprint(revision.FullPath)))
                throw new BoqExternalModificationException(
                    "BOQ 文件在" + phase + "发生外部修改，未覆盖最新内容。");
        }

        public static void Write(string targetPath, string templatePath,
            IEnumerable<SubmissionRecord> records)
            => Write(targetPath, templatePath, records, false);

        /// <summary>
        /// Writes one machine's quantities. U1F/U1U pass allowEmptyMaterials=true so
        /// clearing a CAD table also clears the existing BOQ device column.
        /// </summary>
        public static void Write(string targetPath, string templatePath,
            IEnumerable<SubmissionRecord> records, bool allowEmptyMaterials)
            => WriteCore(targetPath, templatePath, records, allowEmptyMaterials,
                null, null, null);

        // Keeps the read/replace race deterministic in unit tests without changing public callers.
        internal static void Write(string targetPath, string templatePath,
            IEnumerable<SubmissionRecord> records, bool allowEmptyMaterials,
            Action beforeReplace)
            => WriteCore(targetPath, templatePath, records, allowEmptyMaterials,
                beforeReplace, null, null);

        internal static void Write(string targetPath, string templatePath,
            IEnumerable<SubmissionRecord> records, bool allowEmptyMaterials,
            Action beforeReplace, Action afterReplace)
            => WriteCore(targetPath, templatePath, records, allowEmptyMaterials,
                beforeReplace, afterReplace, null);

        internal static void WriteTracked(string targetPath, string templatePath,
            IEnumerable<SubmissionRecord> records, bool allowEmptyMaterials,
            BoqWorkbookWriteProgress progress, Action beforeReplace = null,
            Action afterReplace = null)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            WriteCore(targetPath, templatePath, records, allowEmptyMaterials,
                beforeReplace, afterReplace, progress);
        }

        private static void WriteCore(string targetPath, string templatePath,
            IEnumerable<SubmissionRecord> records, bool allowEmptyMaterials,
            Action beforeReplace, Action afterReplace, BoqWorkbookWriteProgress progress)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("BOQ 输出文件路径为空。", nameof(targetPath));
            List<SubmissionRecord> sourceRecords = (records ?? Enumerable.Empty<SubmissionRecord>())
                .Where(record => record != null).ToList();
            string[] machineIds = sourceRecords.Select(record => (record.MachineId ?? "").Trim())
                .Where(value => value.Length > 0)
                .Distinct(IdentityTextNormalizer.Comparer).ToArray();
            if (machineIds.Length > 1)
                throw new InvalidDataException("One BOQ workbook cannot mix multiple machine IDs.");
            if (sourceRecords.Any(record => string.IsNullOrWhiteSpace(record.MachineId)))
                throw new InvalidDataException("BOQ machine ID is required for quantity attribution.");
            if (sourceRecords.Any(record => string.IsNullOrWhiteSpace(record.DeviceName)))
                throw new InvalidDataException("BOQ device name is required for quantity attribution.");
            if (!allowEmptyMaterials
                && !sourceRecords.Any(record => record.Materials != null
                    && record.Materials.Count > 0))
                throw new InvalidDataException("没有可写入 BOQ 的清单材料。");
            string fullPath = Path.GetFullPath(targetPath);
            string folder = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(folder);
            string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool existedBeforeWrite = false;
            byte[] originalFingerprint = null;
            string verificationBackup = null;
            IWorkbook workbook = null;
            IDisposable updateLock = null;
            bool replacementApplied = false;
            byte[] writtenFingerprint = null;
            try
            {
                updateLock = FileUpdateLock.Acquire(fullPath,
                    "BOQ 正在被另一个 UNCAD 用户更新，请稍后重试。");
                // Keep an independent rollback copy until the post-write readback has
                // succeeded. Replace() removes its own transient backup immediately.
                existedBeforeWrite = File.Exists(fullPath);
                originalFingerprint = existedBeforeWrite ? ComputeFingerprint(fullPath) : null;
                verificationBackup = existedBeforeWrite
                    ? fullPath + ".uncad-verify-" + Guid.NewGuid().ToString("N") + ".bak"
                    : null;
                if (existedBeforeWrite)
                    File.Copy(fullPath, verificationBackup, false);
                workbook = Load(fullPath, templatePath);
                ISheet sheet = workbook.GetSheet(SheetName) ?? workbook.GetSheetAt(0);
                Dictionary<string, int> rows = FindItemRows(sheet);
                Dictionary<string, int> deviceColumns = FindDeviceColumns(sheet);
                var expected = new Dictionary<string, Dictionary<string, decimal>>(
                    IdentityTextNormalizer.Comparer);
                foreach (IGrouping<string, SubmissionRecord> deviceGroup in sourceRecords
                    .Where(record => record != null)
                    .GroupBy(record => (record.DeviceName ?? "").Trim(),
                        IdentityTextNormalizer.Comparer))
                {
                    if (deviceGroup.Key.Length == 0)
                        throw new InvalidDataException("BOQ 设备名称不能为空，不能创建设备列。");
                    int deviceColumn = GetOrCreateDeviceColumn(sheet, deviceColumns, deviceGroup.Key);
                    Dictionary<string, decimal> deviceQuantities = ReadQuantities(
                        deviceGroup, rows, allowEmptyMaterials);
                    expected[deviceGroup.Key] = deviceQuantities;
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
                beforeReplace?.Invoke();
                if (existedBeforeWrite)
                {
                    if (!File.Exists(fullPath))
                        throw new BoqExternalModificationException(
                            "BOQ 文件在读取期间被删除，未覆盖原文件。");
                    if (!SameFingerprint(originalFingerprint, ComputeFingerprint(fullPath)))
                        throw new BoqExternalModificationException(
                            "BOQ 文件在读取期间发生外部修改，未覆盖最新内容。");
                }
                else if (File.Exists(fullPath))
                {
                    throw new BoqExternalModificationException(
                        "BOQ 文件在写入期间被其他程序创建，未覆盖该文件。");
                }
                Replace(fullPath, temporary, originalFingerprint);
                temporary = null;
                replacementApplied = true;
                writtenFingerprint = ComputeFingerprint(fullPath);
                progress?.MarkReplaced(writtenFingerprint);
                afterReplace?.Invoke();
                // 写后回读硬对账:逐设备列核对非零工程量与内存聚合一致,
                // 不一致(写入失败/外部改动)立即报错并回滚整个批次。
                VerifyWrittenWorkbook(fullPath, templatePath, expected);
                if (!File.Exists(fullPath)
                    || !SameFingerprint(writtenFingerprint, ComputeFingerprint(fullPath)))
                    throw new BoqExternalModificationException(
                        "BOQ 文件在写后校验期间发生外部修改，未覆盖最新内容。");
            }
            catch (Exception error)
            {
                if (replacementApplied)
                {
                    try
                    {
                        if (!File.Exists(fullPath)
                            || !SameFingerprint(writtenFingerprint,
                                ComputeFingerprint(fullPath)))
                            throw new BoqExternalModificationException(
                                "BOQ 文件在写后校验期间发生外部修改，未使用旧备份覆盖最新版。");
                        if (existedBeforeWrite)
                        {
                            if (!File.Exists(verificationBackup))
                                throw new FileNotFoundException(
                                    "BOQ 写后校验失败且恢复备份已丢失。", verificationBackup);
                            File.Copy(verificationBackup, fullPath, true);
                        }
                        else if (!existedBeforeWrite && File.Exists(fullPath))
                            File.Delete(fullPath);
                        progress?.MarkRestored();
                    }
                    catch (BoqExternalModificationException conflict)
                    {
                        throw new BoqExternalModificationException(
                            "BOQ 写后校验失败，且文件已被外部修改；已保留外部最新版。",
                            new AggregateException(error, conflict));
                    }
                    catch (Exception restoreError)
                    {
                        throw new AggregateException(
                            "BOQ 写入校验失败且原文件恢复失败。", error, restoreError);
                    }
                }
                throw;
            }
            finally
            {
                workbook?.Close();
                if (updateLock != null)
                {
                    updateLock.Dispose();
                }
                FileCleanup.TryDelete(temporary);
                FileCleanup.TryDelete(verificationBackup);
            }
        }

        /// <summary>写后回读:重新打开目标文件,逐设备列核对每个项目编码的工程量。</summary>
        private static void VerifyWrittenWorkbook(string fullPath, string templatePath,
            Dictionary<string, Dictionary<string, decimal>> expected)
        {
            IWorkbook workbook = Load(fullPath, templatePath);
            try
            {
                ISheet sheet = workbook.GetSheet(SheetName) ?? workbook.GetSheetAt(0);
                Dictionary<string, int> rows = FindItemRows(sheet);
                Dictionary<string, int> deviceColumns = FindDeviceColumns(sheet);
                foreach (KeyValuePair<string, Dictionary<string, decimal>> device in expected)
                {
                    if (!deviceColumns.TryGetValue(device.Key, out int column))
                        throw new InvalidDataException(
                            "BOQ 写后校验失败：设备列不存在 " + device.Key);
                    foreach (KeyValuePair<string, int> item in rows)
                    {
                        decimal expectedValue = device.Value.TryGetValue(item.Key,
                            out decimal quantity) ? quantity : 0m;
                        decimal actualValue = ReadNumeric(sheet.GetRow(item.Value)
                            ?.GetCell(column));
                        if (Math.Abs(actualValue - expectedValue) > 0.000001m)
                            throw new InvalidDataException(
                                "BOQ 写后校验失败：文件与图框内容不一致。设备 "
                                + device.Key + "，项目编码 " + item.Key
                                + "，期望 " + expectedValue + "，实际 " + actualValue
                                + "。导出已回滚。");
                    }
                }
            }
            finally
            {
                workbook.Close();
            }
        }

        private static decimal ReadNumeric(ICell cell)
        {
            if (cell == null) return 0m;
            if (cell.CellType == CellType.Numeric) return (decimal)cell.NumericCellValue;
            if (cell.CellType == CellType.String
                && TryParseQuantity(cell.StringCellValue, out decimal parsed)) return parsed;
            return 0m;
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
            var result = new Dictionary<string, int>(IdentityTextNormalizer.Comparer);
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
            IEnumerable<SubmissionRecord> records, Dictionary<string, int> rows,
            bool allowEmptyMaterials)
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
            if (quantities.Count == 0 && !allowEmptyMaterials)
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

        private static byte[] ComputeFingerprint(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (SHA256 sha256 = SHA256.Create())
                return sha256.ComputeHash(stream);
        }

        private static bool SameFingerprint(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++)
                if (left[index] != right[index]) return false;
            return true;
        }

        private static void Replace(string target, string temporary,
            byte[] expectedFingerprint)
        {
            if (!File.Exists(target))
            {
                if (expectedFingerprint != null)
                    throw new BoqExternalModificationException(
                        "BOQ 文件在替换期间被删除，未覆盖原文件。");
                File.Move(temporary, target);
                return;
            }
            if (expectedFingerprint == null)
                throw new BoqExternalModificationException(
                    "BOQ 文件在替换期间被其他程序创建，未覆盖该文件。");
            if (!SameFingerprint(expectedFingerprint, ComputeFingerprint(target)))
                throw new BoqExternalModificationException(
                    "BOQ 文件在替换期间发生外部修改，未覆盖最新内容。");

            string backup = target + ".bak";
            try
            {
                File.Replace(temporary, target, backup, true);
                FileCleanup.TryDelete(backup);
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (NotSupportedException) { }
            catch (IOException) { }

            if (!File.Exists(target)
                || !SameFingerprint(expectedFingerprint, ComputeFingerprint(target)))
                throw new BoqExternalModificationException(
                    "BOQ 文件在替换期间发生外部修改，未覆盖最新内容。");

            bool movedOriginal = false;
            try
            {
                File.Move(target, backup);
                movedOriginal = true;
                File.Move(temporary, target);
                FileCleanup.TryDelete(backup);
            }
            catch
            {
                if (movedOriginal && !File.Exists(target) && File.Exists(backup))
                    File.Move(backup, target);
                throw;
            }
        }

    }
}
