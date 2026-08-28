using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Submission
{
    public static class SubmissionWorkbookWriter
    {
        public const string DefaultFileName = "UNCAD_Submissions.xlsx";
        public const string SheetName = "提交记录";
        public const string DetailSheetName = "清单明细";
        public static readonly string[] Headers =
        {
            "机台ID", "设备名称", "盘柜类型",
            "电缆型号", "设备原电缆型号", "清单电缆型号", "电缆米数", "FR", "配电详情",
            "软管直径", "软管米数",
            "桥架信息", "桥架米数", "线管信息", "线管米数",
            "下游轴位", "上游轴位", "提交时间", "更新时间"
        };
        public static readonly string[] DetailHeaders =
        {
            "机台ID", "设备名称", "序号", "材料名称", "特征描述",
            "单位", "数量", "项目编码", "提交时间", "更新时间"
        };

        /// <summary>
        /// Acquires the same process lock used by the writer, verifies an existing workbook can be
        /// parsed, and proves the target directory accepts a temporary file before CAD is changed.
        /// </summary>
        public static void ValidateTargetForUpdate(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("自动记录文件路径为空。", nameof(filePath));
            string fullPath = Path.GetFullPath(filePath);
            string folder = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                throw new DirectoryNotFoundException("自动记录文件夹不存在: " + folder);

            using (FileStream updateLock = AcquireUpdateLock(fullPath))
            {
                if (File.Exists(fullPath))
                {
                    IWorkbook workbook = null;
                    try { workbook = LoadOrCreate(fullPath); }
                    finally { workbook?.Close(); }
                }
                string probe = Path.Combine(folder, "." + Path.GetFileName(fullPath)
                    + "." + Guid.NewGuid().ToString("N") + ".probe");
                try
                {
                    using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                }
                finally { TryDelete(probe); }
            }
        }

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

            // 先取得进程间锁，再读取基线，避免等待锁期间的合法更新被误判为冲突。
            FileStream updateLock = AcquireUpdateLock(fullPath);
            IWorkbook workbook = null;
            bool existedBeforeRead = false;
            byte[] originalFingerprint = null;
            string temporary = Path.Combine(folder, "." + Path.GetFileName(fullPath)
                + "." + Guid.NewGuid().ToString("N") + ".tmp");
            string backup = temporary + ".bak";
            try
            {
                existedBeforeRead = File.Exists(fullPath);
                originalFingerprint = existedBeforeRead
                    ? ComputeFingerprint(fullPath) : null;
                workbook = LoadOrCreate(fullPath);
                ISheet sheet = workbook.GetSheet(SheetName) ?? workbook.CreateSheet(SheetName);
                Dictionary<string, int> columns = EnsureHeader(workbook, sheet, Headers);
                MigrateLegacyCableColumns(sheet, columns);
                ISheet detailSheet = workbook.GetSheet(DetailSheetName)
                    ?? workbook.CreateSheet(DetailSheetName);
                Dictionary<string, int> detailColumns = EnsureHeader(
                    workbook, detailSheet, DetailHeaders);
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
                for (int rowIndex = detailSheet.LastRowNum; rowIndex >= 1; rowIndex--)
                    if (SameKey(detailSheet.GetRow(rowIndex), detailColumns, record))
                        RemoveRow(detailSheet, rowIndex);

                string now = submittedNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                string submitted = originalSubmitted.Length > 0 ? originalSubmitted : now;
                IRow target = sheet.CreateRow(Math.Max(1, sheet.LastRowNum + 1));
                Set(target, columns, "机台ID", record.MachineId);
                Set(target, columns, "设备名称", record.DeviceName);
                Set(target, columns, "盘柜类型", record.PanelType);
                // Keep the legacy column current so existing workbook formulas still work.
                Set(target, columns, "电缆型号", record.Cable);
                Set(target, columns, "设备原电缆型号", record.OriginalCable);
                Set(target, columns, "清单电缆型号", record.Cable);
                Set(target, columns, "电缆米数", record.CableMeters);
                Set(target, columns, "FR", record.Fr);
                Set(target, columns, "配电详情", record.Detail);
                Set(target, columns, "软管直径", record.Diameter);
                Set(target, columns, "软管米数", record.FlexibleConduitMeters);
                Set(target, columns, "桥架信息", record.BridgeInfo);
                Set(target, columns, "桥架米数", record.BridgeMeters);
                Set(target, columns, "线管信息", record.ConduitInfo);
                Set(target, columns, "线管米数", record.ConduitMeters);
                Set(target, columns, "下游轴位", record.DownstreamAxis);
                Set(target, columns, "上游轴位", record.UpstreamAxis);
                Set(target, columns, "提交时间", submitted);
                Set(target, columns, "更新时间", now);

                foreach (SubmissionMaterial material in record.Materials
                    ?? new List<SubmissionMaterial>())
                {
                    IRow detail = detailSheet.CreateRow(Math.Max(1, detailSheet.LastRowNum + 1));
                    Set(detail, detailColumns, "机台ID", record.MachineId);
                    Set(detail, detailColumns, "设备名称", record.DeviceName);
                    Set(detail, detailColumns, "序号", material.Number);
                    Set(detail, detailColumns, "材料名称", material.Name);
                    Set(detail, detailColumns, "特征描述", material.Description);
                    Set(detail, detailColumns, "单位", material.Unit);
                    Set(detail, detailColumns, "数量", material.Quantity);
                    Set(detail, detailColumns, "项目编码", material.Code);
                    Set(detail, detailColumns, "提交时间", submitted);
                    Set(detail, detailColumns, "更新时间", now);
                }
                ApplyWidths(sheet, columns);
                ApplyWidths(detailSheet, detailColumns);

                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    workbook.Write(stream);
                workbook.Close();
                workbook = null;
                if (existedBeforeRead)
                {
                    if (!File.Exists(fullPath))
                        throw new IOException("提交表在读取期间被删除，未覆盖原文件。");
                    if (!SameFingerprint(originalFingerprint, ComputeFingerprint(fullPath)))
                        throw new IOException("提交表在读取期间发生外部修改，未覆盖最新内容。");
                    ReplaceExisting(temporary, fullPath, backup, originalFingerprint);
                    backup = "";
                }
                else
                {
                    if (File.Exists(fullPath))
                        throw new IOException("提交表在写入期间被其他程序创建，未覆盖该文件。");
                    File.Move(temporary, fullPath);
                }

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

        /// <summary>
        /// Updates several frame records through one target lock, one workbook load and one final
        /// replacement. All input is validated before the target workbook can be modified.
        /// </summary>
        public static SubmissionBatchWriteResult UpsertMany(string filePath,
            IEnumerable<SubmissionRecord> sourceRecords, DateTimeOffset submittedNow)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("提交文件路径为空。", nameof(filePath));
            List<SubmissionRecord> records = (sourceRecords ?? Enumerable.Empty<SubmissionRecord>())
                .ToList();
            if (records.Count == 0) throw new ArgumentException("没有可提交的图框记录。", nameof(sourceRecords));

            var inputKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SubmissionRecord record in records)
            {
                if (record == null) throw new ArgumentException("提交记录不能为空。", nameof(sourceRecords));
                if (string.IsNullOrWhiteSpace(record.MachineId)
                    || string.IsNullOrWhiteSpace(record.DeviceName))
                    throw new InvalidDataException("机台ID和设备名称不能为空。");
                string key = record.MachineId.Trim() + "" + record.DeviceName.Trim();
                if (!inputKeys.Add(key))
                    throw new InvalidDataException("批量提交包含重复机台/设备："
                        + record.MachineId.Trim() + " / " + record.DeviceName.Trim());
            }

            string fullPath = Path.GetFullPath(filePath);
            string folder = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                throw new DirectoryNotFoundException("提交文件夹不存在: " + folder);

            FileStream updateLock = AcquireUpdateLock(fullPath);
            IWorkbook workbook = null;
            bool existedBeforeRead = false;
            byte[] originalFingerprint = null;
            string temporary = Path.Combine(folder, "." + Path.GetFileName(fullPath)
                + "." + Guid.NewGuid().ToString("N") + ".tmp");
            string backup = temporary + ".bak";
            try
            {
                existedBeforeRead = File.Exists(fullPath);
                originalFingerprint = existedBeforeRead ? ComputeFingerprint(fullPath) : null;
                workbook = LoadOrCreate(fullPath);
                ISheet sheet = workbook.GetSheet(SheetName) ?? workbook.CreateSheet(SheetName);
                Dictionary<string, int> columns = EnsureHeader(workbook, sheet, Headers);
                MigrateLegacyCableColumns(sheet, columns);
                ISheet detailSheet = workbook.GetSheet(DetailSheetName)
                    ?? workbook.CreateSheet(DetailSheetName);
                Dictionary<string, int> detailColumns = EnsureHeader(
                    workbook, detailSheet, DetailHeaders);
                string now = submittedNow.ToLocalTime().ToString(
                    "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var batch = new SubmissionBatchWriteResult
                {
                    FilePath = fullPath,
                    UpdatedAt = now
                };

                foreach (SubmissionRecord record in records)
                {
                    var duplicateRows = new List<int>();
                    string originalSubmitted = "";
                    for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
                    {
                        IRow row = sheet.GetRow(rowIndex);
                        if (!SameKey(row, columns, record)) continue;
                        duplicateRows.Add(rowIndex);
                        string value = CellText(row.GetCell(columns["提交时间"]));
                        if (originalSubmitted.Length == 0 && value.Length > 0)
                            originalSubmitted = value;
                    }
                    for (int i = duplicateRows.Count - 1; i >= 0; i--)
                        RemoveRow(sheet, duplicateRows[i]);
                    for (int rowIndex = detailSheet.LastRowNum; rowIndex >= 1; rowIndex--)
                        if (SameKey(detailSheet.GetRow(rowIndex), detailColumns, record))
                            RemoveRow(detailSheet, rowIndex);

                    string submitted = originalSubmitted.Length > 0 ? originalSubmitted : now;
                    IRow target = sheet.CreateRow(Math.Max(1, sheet.LastRowNum + 1));
                    Set(target, columns, "机台ID", record.MachineId);
                    Set(target, columns, "设备名称", record.DeviceName);
                    Set(target, columns, "盘柜类型", record.PanelType);
                    Set(target, columns, "电缆型号", record.Cable);
                    Set(target, columns, "设备原电缆型号", record.OriginalCable);
                    Set(target, columns, "清单电缆型号", record.Cable);
                    Set(target, columns, "电缆米数", record.CableMeters);
                    Set(target, columns, "FR", record.Fr);
                    Set(target, columns, "配电详情", record.Detail);
                    Set(target, columns, "软管直径", record.Diameter);
                    Set(target, columns, "软管米数", record.FlexibleConduitMeters);
                    Set(target, columns, "桥架信息", record.BridgeInfo);
                    Set(target, columns, "桥架米数", record.BridgeMeters);
                    Set(target, columns, "线管信息", record.ConduitInfo);
                    Set(target, columns, "线管米数", record.ConduitMeters);
                    Set(target, columns, "下游轴位", record.DownstreamAxis);
                    Set(target, columns, "上游轴位", record.UpstreamAxis);
                    Set(target, columns, "提交时间", submitted);
                    Set(target, columns, "更新时间", now);

                    foreach (SubmissionMaterial material in record.Materials
                        ?? new List<SubmissionMaterial>())
                    {
                        IRow detail = detailSheet.CreateRow(Math.Max(1, detailSheet.LastRowNum + 1));
                        Set(detail, detailColumns, "机台ID", record.MachineId);
                        Set(detail, detailColumns, "设备名称", record.DeviceName);
                        Set(detail, detailColumns, "序号", material.Number);
                        Set(detail, detailColumns, "材料名称", material.Name);
                        Set(detail, detailColumns, "特征描述", material.Description);
                        Set(detail, detailColumns, "单位", material.Unit);
                        Set(detail, detailColumns, "数量", material.Quantity);
                        Set(detail, detailColumns, "项目编码", material.Code);
                        Set(detail, detailColumns, "提交时间", submitted);
                        Set(detail, detailColumns, "更新时间", now);
                    }

                    var item = new SubmissionWriteResult
                    {
                        ReplacedExisting = duplicateRows.Count > 0,
                        RemovedDuplicates = duplicateRows.Count,
                        FilePath = fullPath,
                        SubmittedAt = submitted,
                        UpdatedAt = now
                    };
                    batch.Records.Add(item);
                    if (item.ReplacedExisting) batch.ReplacedCount++;
                    else batch.AddedCount++;
                    batch.RemovedDuplicates += item.RemovedDuplicates;
                }

                ApplyWidths(sheet, columns);
                ApplyWidths(detailSheet, detailColumns);
                using (var stream = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None)) workbook.Write(stream);
                workbook.Close();
                workbook = null;

                if (existedBeforeRead)
                {
                    if (!File.Exists(fullPath))
                        throw new IOException("提交表在读取期间被删除，未覆盖原文件。");
                    if (!SameFingerprint(originalFingerprint, ComputeFingerprint(fullPath)))
                        throw new IOException("提交表在读取期间发生外部修改，未覆盖最新内容。");
                    ReplaceExisting(temporary, fullPath, backup, originalFingerprint);
                }
                else
                {
                    if (File.Exists(fullPath))
                        throw new IOException("提交表在写入期间被其他程序创建，未覆盖该文件。");
                    File.Move(temporary, fullPath);
                }
                return batch;
            }
            finally
            {
                workbook?.Close();
                updateLock.Dispose();
                TryDelete(temporary);
            }
        }

        // 计算完整文件指纹，避免只比较大小和时间导致外部修改漏检。
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
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        // 某些网络或重定向文件系统不支持 File.Replace，降级为带备份的移动替换。
        private static void ReplaceExisting(string temporary, string target, string backup,
            byte[] expectedFingerprint)
        {
            try
            {
                File.Replace(temporary, target, backup, true);
                TryDelete(backup);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                // 继续使用可回滚的移动替换路径。
            }
            catch (NotSupportedException)
            {
                // 继续使用可回滚的移动替换路径。
            }
            catch (IOException)
            {
                // 网络文件系统可能以 IOException 表示不支持原子替换。
            }

            if (!SameFingerprint(expectedFingerprint, ComputeFingerprint(target)))
                throw new IOException("提交表在替换期间发生外部修改，未覆盖最新内容。");

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
                // 替换失败时尽力恢复原文件；恢复失败由调用方的最终异常明确暴露。
                if (movedOriginal && !File.Exists(target) && File.Exists(backup))
                    File.Move(backup, target);
                throw;
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
            // 允许其他读取者，但在解析期间拒绝外部写入和删除，避免读取撕裂。
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read)) return new XSSFWorkbook(stream);
        }

        private static Dictionary<string, int> EnsureHeader(IWorkbook workbook, ISheet sheet,
            string[] expectedHeaders)
        {
            IRow header = sheet.GetRow(0);
            if (header == null || header.LastCellNum <= 0)
            {
                header = sheet.CreateRow(0);
                ICellStyle style = HeaderStyle(workbook);
                for (int i = 0; i < expectedHeaders.Length; i++)
                {
                    ICell cell = header.CreateCell(i);
                    cell.SetCellValue(expectedHeaders[i]);
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
            string[] missing = expectedHeaders.Where(h => !result.ContainsKey(h)).ToArray();
            if (missing.Length > 0)
            {
                ICellStyle style = HeaderStyle(workbook);
                int column = Math.Max(0, (int)header.LastCellNum);
                foreach (string name in missing)
                {
                    ICell cell = header.CreateCell(column);
                    cell.SetCellValue(name);
                    cell.CellStyle = style;
                    result[name] = column++;
                }
            }
            return result;
        }

        private static void MigrateLegacyCableColumns(ISheet sheet,
            Dictionary<string, int> columns)
        {
            // v1.7 has one cable field. Seed both v1.8 lineage fields without
            // overwriting rows that were already exported by the new schema.
            int legacyColumn = columns["电缆型号"];
            int originalColumn = columns["设备原电缆型号"];
            int boqColumn = columns["清单电缆型号"];
            for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                if (row == null) continue;
                string legacy = CellText(row.GetCell(legacyColumn));
                if (legacy.Length == 0) continue;
                if (CellText(row.GetCell(originalColumn)).Length == 0)
                    row.GetCell(originalColumn, MissingCellPolicy.CREATE_NULL_AS_BLANK)
                        .SetCellValue(legacy);
                if (CellText(row.GetCell(boqColumn)).Length == 0)
                    row.GetCell(boqColumn, MissingCellPolicy.CREATE_NULL_AS_BLANK)
                        .SetCellValue(legacy);
            }
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
            => row != null
                && string.Equals(CellText(row.GetCell(columns["机台ID"])), record.MachineId.Trim(), StringComparison.OrdinalIgnoreCase)
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
                int width = column.Key == "FR" || column.Key == "配电详情"
                    || column.Key.EndsWith("电缆型号", StringComparison.Ordinal)
                    || column.Key == "特征描述"
                    || column.Key.EndsWith("信息", StringComparison.Ordinal)
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
