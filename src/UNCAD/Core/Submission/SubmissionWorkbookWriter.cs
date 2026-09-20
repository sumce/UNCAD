using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UNCAD.Core.IO;
using UNCAD.Core.Text;
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

            using (IDisposable updateLock = FileUpdateLock.Acquire(fullPath,
                "提交表正在被另一个 UNCAD 用户更新，请稍后重试。"))
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
                finally { FileCleanup.TryDelete(probe); }
            }
        }

        public static SubmissionWriteResult Upsert(string filePath, SubmissionRecord record,
            DateTimeOffset submittedNow)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            SubmissionBatchWriteResult batch = UpsertMany(filePath,
                new[] { record }, submittedNow);
            return batch.Records.Single();
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

            var inputKeys = new HashSet<string>(IdentityTextNormalizer.Comparer);
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

            IDisposable updateLock = FileUpdateLock.Acquire(fullPath,
                "提交表正在被另一个 UNCAD 用户更新，请稍后重试。");
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
                CompactLegacyDetailRows(detailSheet, detailColumns);
                string now = submittedNow.ToLocalTime().ToString(
                    "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var batch = new SubmissionBatchWriteResult
                {
                    FilePath = fullPath,
                    UpdatedAt = now
                };

                foreach (SubmissionRecord record in records)
                {
                    // 汇总表追加每次提交历史；明细表仅保留该机台/设备的最新材料版本。
                    bool hadExistingSubmission = false;
                    for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
                    {
                        if (SameKey(sheet.GetRow(rowIndex), columns, record))
                        {
                            hadExistingSubmission = true;
                            break;
                        }
                    }
                    int removedDetailRows = 0;
                    for (int rowIndex = detailSheet.LastRowNum; rowIndex >= 1; rowIndex--)
                    {
                        if (!SameKey(detailSheet.GetRow(rowIndex), detailColumns, record)) continue;
                        RemoveRow(detailSheet, rowIndex);
                        removedDetailRows++;
                    }

                    string submitted = now;
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

                    WriteCompactDetailRow(detailSheet, detailColumns, record, submitted, now);

                    var item = new SubmissionWriteResult
                    {
                        ReplacedExisting = hadExistingSubmission,
                        RemovedDetailRows = removedDetailRows,
                        FilePath = fullPath,
                        SubmittedAt = submitted,
                        UpdatedAt = now
                    };
                    batch.Records.Add(item);
                    // Every execution adds one immutable history row; replacement counts describe
                    // identities whose latest-detail view was refreshed.
                    batch.AddedCount++;
                    if (item.ReplacedExisting) batch.ReplacedCount++;
                    batch.RemovedDetailRows += item.RemovedDetailRows;
                }

                GroupRowsByMachineId(sheet, columns);
                GroupRowsByMachineId(detailSheet, detailColumns);
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
                FileCleanup.TryDelete(temporary);
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
                FileCleanup.TryDelete(backup);
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
                FileCleanup.TryDelete(backup);
            }
            catch
            {
                // 替换失败时尽力恢复原文件；恢复失败由调用方的最终异常明确暴露。
                if (movedOriginal && !File.Exists(target) && File.Exists(backup))
                    File.Move(backup, target);
                throw;
            }
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

        /// <summary>Writes all materials into aligned multiline cells so one device occupies one row.</summary>
        private static void WriteCompactDetailRow(ISheet sheet, Dictionary<string, int> columns,
            SubmissionRecord record, string submitted, string updated)
        {
            List<SubmissionMaterial> materials = record.Materials ?? new List<SubmissionMaterial>();
            IRow row = sheet.CreateRow(Math.Max(1, sheet.LastRowNum + 1));
            Set(row, columns, "机台ID", record.MachineId);
            Set(row, columns, "设备名称", record.DeviceName);
            Set(row, columns, "序号", JoinMaterials(materials, item => item.Number));
            Set(row, columns, "材料名称", JoinMaterials(materials, item => item.Name));
            Set(row, columns, "特征描述", JoinMaterials(materials, item => item.Description));
            Set(row, columns, "单位", JoinMaterials(materials, item => item.Unit));
            Set(row, columns, "数量", JoinMaterials(materials, item => item.Quantity));
            Set(row, columns, "项目编码", JoinMaterials(materials, item => item.Code));
            Set(row, columns, "提交时间", submitted);
            Set(row, columns, "更新时间", updated);
            foreach (string header in new[] { "序号", "材料名称", "特征描述", "单位", "数量", "项目编码" })
                EnableWrapText(row.GetCell(columns[header]));
            // Keep the compact row readable without allowing very large BOQs to create extreme heights.
            row.Height = (short)Math.Min(short.MaxValue, Math.Max(1, materials.Count) * 300);
        }

        private static string JoinMaterials(IEnumerable<SubmissionMaterial> materials,
            Func<SubmissionMaterial, string> selector)
            => string.Join("\n", (materials ?? Enumerable.Empty<SubmissionMaterial>())
                .Select(item => (selector(item) ?? "").Trim()));

        /// <summary>
        /// Migrates the old one-material-per-row layout. The first row retains custom cells and
        /// formatting; known material columns are combined in their original order.
        /// </summary>
        private static void CompactLegacyDetailRows(ISheet sheet, Dictionary<string, int> columns)
        {
            var groups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                if (row == null) continue;
                string machine = CellText(row.GetCell(columns["机台ID"]));
                string device = CellText(row.GetCell(columns["设备名称"]));
                if (machine.Length == 0 || device.Length == 0) continue;
                string key = machine + "\u001f" + device;
                if (!groups.TryGetValue(key, out List<int> indexes))
                    groups[key] = indexes = new List<int>();
                indexes.Add(rowIndex);
            }

            foreach (List<int> indexes in groups.Values.Where(group => group.Count > 1)
                .OrderByDescending(group => group[0]))
            {
                List<IRow> sourceRows = indexes.Select(sheet.GetRow).Where(row => row != null).ToList();
                if (sourceRows.Count < 2) continue;
                bool legacyMaterialRows = LooksLikeLegacyMaterialRows(sourceRows, columns);
                IRow retained = legacyMaterialRows ? sourceRows[0]
                    : SelectLatestDetailRow(sourceRows, columns);
                var snapshot = RowSnapshot.Capture(retained,
                    CellText(retained.GetCell(columns["机台ID"])), retained.RowNum);
                var combined = new Dictionary<string, string>();
                if (legacyMaterialRows)
                {
                    foreach (string header in MaterialDetailHeaders)
                        combined[header] = string.Join("\n", sourceRows
                            .Select(row => CellText(row.GetCell(columns[header]))));
                }

                for (int index = indexes.Count - 1; index >= 0; index--)
                    RemoveRow(sheet, indexes[index]);
                IRow target = sheet.CreateRow(Math.Max(1, sheet.LastRowNum + 1));
                snapshot.WriteTo(target);
                if (legacyMaterialRows)
                {
                    foreach (KeyValuePair<string, string> value in combined)
                    {
                        Set(target, columns, value.Key, value.Value);
                        EnableWrapText(target.GetCell(columns[value.Key]));
                    }
                    target.Height = (short)Math.Min(short.MaxValue, sourceRows.Count * 300);
                }
            }
        }

        private static readonly string[] MaterialDetailHeaders =
        {
            "序号", "材料名称", "特征描述", "单位", "数量", "项目编码"
        };

        private static bool LooksLikeLegacyMaterialRows(List<IRow> rows,
            Dictionary<string, int> columns)
        {
            // Old layout has one scalar, uniquely numbered material per row and one shared timestamp.
            // Any multiline value or differing timestamp means these are competing compact versions.
            if (rows.Any(row => MaterialDetailHeaders.Any(header =>
                CellText(row.GetCell(columns[header])).IndexOfAny(new[] { '\r', '\n' }) >= 0)))
                return false;
            List<string> numbers = rows.Select(row =>
                CellText(row.GetCell(columns["序号"]))).ToList();
            if (numbers.Any(value => value.Length == 0)
                || numbers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
                return false;
            List<string> times = rows.Select(row => LatestDetailTime(row, columns))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return times.Count <= 1;
        }

        private static IRow SelectLatestDetailRow(List<IRow> rows,
            Dictionary<string, int> columns)
            => rows.OrderBy(row => LatestDetailTime(row, columns), StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.RowNum).Last();

        private static string LatestDetailTime(IRow row, Dictionary<string, int> columns)
        {
            string updated = CellText(row.GetCell(columns["更新时间"]));
            return updated.Length > 0 ? updated
                : CellText(row.GetCell(columns["提交时间"]));
        }

        private static void EnableWrapText(ICell cell)
        {
            if (cell == null) return;
            ICellStyle style = cell.Sheet.Workbook.CreateCellStyle();
            if (cell.CellStyle != null) style.CloneStyleFrom(cell.CellStyle);
            style.WrapText = true;
            style.VerticalAlignment = VerticalAlignment.Top;
            cell.CellStyle = style;
        }

        private static bool SameKey(IRow row, Dictionary<string, int> columns, SubmissionRecord record)
            => row != null
                && IdentityTextNormalizer.Equals(
                    CellText(row.GetCell(columns["机台ID"])), record.MachineId)
                && IdentityTextNormalizer.Equals(
                    CellText(row.GetCell(columns["设备名称"])), record.DeviceName);

        /// <summary>
        /// Rewrites data rows in a stable machine-ID order. Rows for one machine become contiguous,
        /// while submission chronology and material order inside that machine remain unchanged.
        /// </summary>
        private static void GroupRowsByMachineId(ISheet sheet, Dictionary<string, int> columns)
        {
            var rows = new List<RowSnapshot>();
            for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                if (row == null) continue;
                rows.Add(RowSnapshot.Capture(row, CellText(row.GetCell(columns["机台ID"])),
                    rowIndex));
            }
            List<RowSnapshot> ordered = rows
                .OrderBy(row => row.MachineId.Length == 0 ? 1 : 0)
                .ThenBy(row => row.MachineId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.OriginalIndex)
                .ToList();
            if (rows.Select(row => row.OriginalIndex).SequenceEqual(
                ordered.Select(row => row.OriginalIndex))) return;

            // Snapshot first so formulas, numeric values, styles and user-added columns survive grouping.
            for (int rowIndex = sheet.LastRowNum; rowIndex >= 1; rowIndex--)
            {
                IRow row = sheet.GetRow(rowIndex);
                if (row != null) sheet.RemoveRow(row);
            }
            for (int index = 0; index < ordered.Count; index++)
                ordered[index].WriteTo(sheet.CreateRow(index + 1));
        }

        private sealed class RowSnapshot
        {
            public string MachineId { get; private set; }
            public int OriginalIndex { get; private set; }
            private short Height { get; set; }
            private bool ZeroHeight { get; set; }
            private List<CellSnapshot> Cells { get; } = new List<CellSnapshot>();

            public static RowSnapshot Capture(IRow row, string machineId, int originalIndex)
            {
                var snapshot = new RowSnapshot
                {
                    MachineId = machineId ?? "",
                    OriginalIndex = originalIndex,
                    Height = row.Height,
                    ZeroHeight = row.ZeroHeight
                };
                for (int column = 0; column < row.LastCellNum; column++)
                {
                    ICell cell = row.GetCell(column);
                    if (cell != null) snapshot.Cells.Add(CellSnapshot.Capture(cell));
                }
                return snapshot;
            }

            public void WriteTo(IRow row)
            {
                row.Height = Height;
                row.ZeroHeight = ZeroHeight;
                foreach (CellSnapshot cell in Cells) cell.WriteTo(row);
            }
        }

        private sealed class CellSnapshot
        {
            private int Column { get; set; }
            private CellType Type { get; set; }
            private object Value { get; set; }
            private ICellStyle Style { get; set; }

            public static CellSnapshot Capture(ICell cell)
            {
                object value;
                switch (cell.CellType)
                {
                    case CellType.Boolean: value = cell.BooleanCellValue; break;
                    case CellType.Numeric: value = cell.NumericCellValue; break;
                    case CellType.Formula: value = cell.CellFormula; break;
                    case CellType.Error: value = cell.ErrorCellValue; break;
                    case CellType.Blank: value = null; break;
                    default: value = cell.StringCellValue ?? ""; break;
                }
                return new CellSnapshot
                {
                    Column = cell.ColumnIndex,
                    Type = cell.CellType,
                    Value = value,
                    Style = cell.CellStyle
                };
            }

            public void WriteTo(IRow row)
            {
                ICell cell = row.CreateCell(Column, Type);
                if (Style != null) cell.CellStyle = Style;
                switch (Type)
                {
                    case CellType.Boolean: cell.SetCellValue((bool)Value); break;
                    case CellType.Numeric: cell.SetCellValue((double)Value); break;
                    case CellType.Formula: cell.SetCellFormula((string)Value); break;
                    case CellType.Error: cell.SetCellErrorValue((byte)Value); break;
                    case CellType.Blank: break;
                    default: cell.SetCellValue((string)Value ?? ""); break;
                }
            }
        }

        private static void RemoveRow(ISheet sheet, int index)
        {
            IRow row = sheet.GetRow(index);
            if (row != null) sheet.RemoveRow(row);
            if (index < sheet.LastRowNum) sheet.ShiftRows(index + 1, sheet.LastRowNum, -1);
        }

        private static void Set(IRow row, Dictionary<string, int> columns, string header, string value)
            => row.GetCell(columns[header], MissingCellPolicy.CREATE_NULL_AS_BLANK)
                .SetCellValue((value ?? "").Trim());

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

    }
}
