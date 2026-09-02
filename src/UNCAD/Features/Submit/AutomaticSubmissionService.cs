using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Submission;
using UNCAD.Infra;

namespace UNCAD.Features.Submit
{
    /// <summary>Result printed after U1F/U1U automatic sync or explicit U1S submission.</summary>
    internal sealed class AutomaticSubmissionWriteResult
    {
        public string FilePath { get; set; }
        public int RecordCount { get; set; }
        public int AddedCount { get; set; }
        public int ReplacedCount { get; set; }
        public int MaterialCount { get; set; }
    }

    /// <summary>
    /// Synchronizes current CAD frame state into one BOQ workbook per machine.
    /// U1F/U1U call this after updates; U1S calls it directly without changing CAD.
    /// </summary>
    internal static class AutomaticSubmissionService
    {
        public static string PrepareTargetPath(IEnumerable<string> machineIds = null)
        {
            string configured = Settings.Get(ConfigKeys.SubmitFolder, "").Trim();
            string folder = configured;
            if (folder.Length == 0)
                folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (folder.Length == 0)
                throw new DirectoryNotFoundException("无法确定 BOQ 输出文件夹，请先在 U1SET 中设置。");

            Directory.CreateDirectory(folder);
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("BOQ 输出文件夹不存在: " + folder);
            if (!string.Equals(configured, folder, StringComparison.OrdinalIgnoreCase))
                Settings.Set(ConfigKeys.SubmitFolder, folder);
            string template = null;
            foreach (string machineId in machineIds ?? Enumerable.Empty<string>())
            {
                string target = BoqWorkbookWriter.BuildTargetPath(folder, machineId);
                if (!File.Exists(target) && template == null)
                    template = BoqWorkbookWriter.ResolveTemplatePath();
                BoqWorkbookWriter.ValidateTargetForUpdate(target, template);
            }
            return folder;
        }

        public static void ValidateIdentityKeys(
            IEnumerable<KeyValuePair<string, string>> identities)
        {
            foreach (KeyValuePair<string, string> identity in identities
                ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                string machineId = (identity.Key ?? "").Trim();
                string deviceName = (identity.Value ?? "").Trim();
                if (machineId.Length == 0 || deviceName.Length == 0)
                    throw new InvalidDataException("机台ID和设备名称不能为空。");
            }
        }

        public static AutomaticSubmissionWriteResult Write(CadContext ctx, string filePath,
            IEnumerable<ObjectId[]> sourceGroups)
            // U1S is the explicit migration path for drawings whose table predates
            // socket-panel rows, so it may infer the missing 4.x panel material.
            => WriteCore(ctx, null, filePath, sourceGroups, null, true);

        public static AutomaticSubmissionWriteResult Write(CadContext ctx, Transaction transaction,
            string filePath, IEnumerable<ObjectId[]> sourceGroups)
            // U1F/U1U have just applied the user's editable table; preserve deletions.
            => WriteCore(ctx, transaction, filePath, sourceGroups, null, false);

        public static AutomaticSubmissionWriteResult Write(CadContext ctx, Transaction transaction,
            string filePath, IEnumerable<ObjectId[]> sourceGroups, FileBatchRollback batch)
            // Batch U1U follows the same post-edit contract as single-frame U1U.
            => WriteCore(ctx, transaction, filePath, sourceGroups, batch, false);

        public static AutomaticSubmissionWriteResult WriteBatchWithDefaults(
            CadContext ctx, Transaction transaction, string filePath,
            IEnumerable<ObjectId[]> sourceGroups, FileBatchRollback batch)
            // Batch U1U explicitly confirmed fallback rows. Keep CAD changes, submit only
            // fixed-catalog materials, and report rows that cannot be keyed in the BOQ.
            => WriteCore(ctx, transaction, filePath, sourceGroups, batch, false, true);

        public static IReadOnlyList<string> TargetPaths(string outputRoot,
            IEnumerable<string> machineIds)
            => (machineIds ?? Enumerable.Empty<string>())
                .Select(machineId => BoqWorkbookWriter.BuildTargetPath(outputRoot, machineId))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static AutomaticSubmissionWriteResult WriteCore(CadContext ctx,
            Transaction transaction, string filePath, IEnumerable<ObjectId[]> sourceGroups,
            FileBatchRollback externalBatch, bool inferLegacySocketPanels,
            bool allowUnmatchedDefaults = false)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Excel 自动记录文件路径为空。", nameof(filePath));

            var records = new List<SubmissionRecord>();
            int index = 0;
            foreach (ObjectId[] ids in sourceGroups ?? Enumerable.Empty<ObjectId[]>())
            {
                index++;
                if (ids == null || ids.Length == 0) continue;
                SubmissionSourceData source = transaction == null
                    ? CadSubmissionReader.Read(ctx, ids)
                    : CadSubmissionReader.Read(transaction, ids);
                SubmissionRecord record = SubmissionRecordExtractor.Extract(source,
                    inferLegacySocketPanels);
                if (string.IsNullOrWhiteSpace(record.MachineId)
                    || string.IsNullOrWhiteSpace(record.DeviceName))
                    throw new InvalidDataException("第 " + index
                        + " 个图框缺少机台编号或设备名称，Excel 未更新。");
                records.Add(record);
            }
            if (records.Count == 0)
                throw new InvalidDataException("没有可写入 Excel 的图框记录。");

            // 硬对账:图框内存在无法匹配固定清单的材料行时,生成的 xlsx 必然
            // 与图框实际内容不一致。报错中止,列出每一行,让用户先在 CAD 表中解决。
            var dropped = records.Where(record =>
                record.DroppedRows != null && record.DroppedRows.Count > 0).ToList();
            if (dropped.Count > 0 && !allowUnmatchedDefaults)
            {
                string detail = string.Join("\n", dropped.Select(record =>
                    "  机台 " + record.MachineId.Trim() + " / " + record.DeviceName.Trim()
                    + ":\n" + string.Join("\n", record.DroppedRows
                        .Select(row => "    · " + row))));
                throw new InvalidDataException(
                    "图框内存在无法匹配固定清单的手动材料行,BOQ 导出已中止"
                    + "（xlsx 将与图框内容不一致）:\n" + detail
                    + "\n请删除这些行或在 U1F/U1U 中替换为固定清单项目后重试。");
            }

            if (dropped.Count > 0 && allowUnmatchedDefaults)
            {
                foreach (SubmissionRecord record in dropped)
                    Log.Warn("U1U accepted unmatched BOQ rows for " + record.MachineId
                        + " / " + record.DeviceName + ": "
                        + string.Join(" || ", record.DroppedRows));
                ctx.Write("\n[U1U] 已按确认保留未匹配清单行；无固定项目编码的行未写入自动 BOQ。"
                    + "已匹配项目仍会正常同步。\n");
            }

            // PrepareTargetPath returns the selected root directory, not a file path.
            string outputRoot = Path.GetFullPath(filePath);
            string template = null;
            var outputPaths = records.Select(record =>
                BoqWorkbookWriter.BuildTargetPath(outputRoot, record.MachineId))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (outputPaths.Any(path => !File.Exists(path)))
                template = BoqWorkbookWriter.ResolveTemplatePath();
            var result = new AutomaticSubmissionWriteResult
            {
                FilePath = string.Join("; ", outputPaths),
                RecordCount = records.Count,
                MaterialCount = records.Sum(record => record.Materials.Count)
            };
            // A batch may contain only fallback CAD rows with no fixed-catalog code. Those
            // rows are valid for the drawing table after explicit confirmation, but there is
            // no safe material key to submit to the BOQ workbook. Report success and leave
            // the existing workbook untouched in that case.
            List<SubmissionRecord> recordsWithMaterials = records
                .Where(record => record.Materials != null && record.Materials.Count > 0)
                .ToList();
            if (recordsWithMaterials.Count == 0)
            {
                result.FilePath = "";
                return result;
            }
            Action writeRecords = () =>
            {
                foreach (IGrouping<string, SubmissionRecord> machineGroup in recordsWithMaterials
                    .GroupBy(record => record.MachineId.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    string target = BoqWorkbookWriter.BuildTargetPath(outputRoot, machineGroup.Key);
                    bool existed = File.Exists(target);
                    BoqWorkbookWriter.Write(target, template, machineGroup);
                    result.AddedCount += machineGroup.Count();
                    if (existed) result.ReplacedCount++;
                }
            };
            if (externalBatch != null)
            {
                writeRecords();
            }
            else
            {
                // Snapshot every destination before the first write. A later machine failure must
                // restore earlier workbooks instead of leaving a partial batch.
                using (var batch = new FileBatchRollback(outputPaths))
                {
                    try
                    {
                        writeRecords();
                        batch.Complete();
                    }
                    catch
                    {
                        batch.Rollback();
                        throw;
                    }
                }
            }
            return result;
        }
    }

    /// <summary>Reads blocks and BOQ tables through a read-only CAD transaction.</summary>
    internal static class CadSubmissionReader
    {
        public static SubmissionSourceData Read(CadContext ctx, ObjectId[] ids)
        {
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                SubmissionSourceData source = Read(transaction, ids);
                transaction.Commit();
                return source;
            }
        }

        public static SubmissionSourceData Read(Transaction transaction, ObjectId[] ids)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            var source = new SubmissionSourceData();
            foreach (ObjectId id in ids ?? Array.Empty<ObjectId>())
            {
                Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                // AutoCAD Table inherits BlockReference, so tables must be classified first.
                if (entity is Table table) ReadTable(table, source);
                else if (entity is BlockReference block) ReadBlock(transaction, block, source);
                else if (entity is DBText || entity is MText) source.TextEntityCount++;
            }
            return source;
        }

        private static void ReadBlock(Transaction transaction, BlockReference block,
            SubmissionSourceData source)
        {
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                var attribute = transaction.GetObject(attributeId, OpenMode.ForRead, true)
                    as AttributeReference;
                if (attribute == null) continue;
                string value = attribute.TextString ?? "";
                if (attribute.IsMTextAttribute)
                {
                    using (MText mtext = attribute.MTextAttribute)
                        if (mtext != null) value = mtext.Contents ?? value;
                }
                source.AddAttribute(attribute.Tag, value);
            }
            if (!block.IsDynamicBlock) return;
            foreach (DynamicBlockReferenceProperty property
                in block.DynamicBlockReferencePropertyCollection)
            {
                string value = Convert.ToString(property.Value) ?? "";
                if (value.Trim().Length == 0) continue;
                source.DynamicValues.Add(value);
                source.AddAttribute(property.PropertyName, value);
            }
        }

        private static void ReadTable(Table table, SubmissionSourceData source)
        {
            for (int row = 0; row < table.Rows.Count; row++)
            {
                var values = new string[table.Columns.Count];
                for (int column = 0; column < table.Columns.Count; column++)
                {
                    try { values[column] = table.Cells[row, column].TextString ?? ""; }
                    catch (System.Exception ex)
                    {
                        values[column] = "";
                        Log.Warn("automatic Excel read table cell failed: " + ex.Message);
                    }
                }
                source.AddTableRow(values);
            }
        }
    }
}
