using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Submit
{
#pragma warning disable 618 // AutoCAD 2022 direct table reads avoid stale Cell wrappers.
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
        /// <summary>
        /// 预热并校验 BOQ 输出目录（在 CAD 事务/写盘之前调用）。
        ///
        /// 返回的是 BOQ 写盘根目录：默认 <c>&lt;输出根&gt;\&lt;图纸&gt;\BOQ</c>
        /// （见 <see cref="OutputLayout"/>）；调用方把它继续传给
        /// <see cref="TargetPaths"/> / <see cref="Write(CadContext, string, IEnumerable{ObjectId[]})"/>，
        /// 得到 <c>&lt;输出根&gt;\&lt;图纸&gt;\BOQ\&lt;机台&gt;\[BOQ]&lt;机台&gt;.xlsx</c>。
        ///
        /// 输出根为空时使用默认 <c>&lt;我的文档&gt;\UNCAD输出</c> 并写回
        /// <see cref="ConfigKeys.SubmitFolder"/>，保证下次 U1S/U1F/U1U 与 U1DWG/U1PDF 落到同一处。
        /// </summary>
        public static string PrepareTargetPath(IEnumerable<string> machineIds = null)
        {
            string configured = Settings.Get(ConfigKeys.SubmitFolder, "").Trim();
            string root = OutputLayout.NormalizeRoot(configured);
            try
            {
                Directory.CreateDirectory(root);
            }
            catch (System.Exception ex)
            {
                throw new DirectoryNotFoundException(
                    "无法创建输出文件夹：" + root + "（" + ex.Message + "）", ex);
            }
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("输出文件夹不存在：" + root);
            if (!string.Equals(configured, root, StringComparison.OrdinalIgnoreCase))
                Settings.Set(ConfigKeys.SubmitFolder, root);

            string template = null;
            var targets = new List<string>();
            foreach (string machineId in machineIds ?? Enumerable.Empty<string>())
            {
                string target = BoqWorkbookWriter.ComposeTargetPath(root, machineId);
                targets.Add(target);
                if (!File.Exists(target) && template == null)
                    template = BoqWorkbookWriter.ResolveTemplatePath();
                BoqWorkbookWriter.ValidateTargetForUpdate(target, template);
            }
            // 写占用预检：BOQ 文件可能正被 Excel 打开。必须在 CAD 事务之前中止并给中文提示。
            OutputLayout.ValidateNotLocked(targets);
            return root;
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

        /// <summary>D-056：带回路身份键的身份校验（U1S 用）。</summary>
        public static void ValidateIdentityKeys(
            IEnumerable<UNCAD.Core.Fill.CircuitSelection> identities)
        {
            foreach (UNCAD.Core.Fill.CircuitSelection identity in identities
                ?? Enumerable.Empty<UNCAD.Core.Fill.CircuitSelection>())
            {
                string machineId = (identity?.MachineId ?? "").Trim();
                string deviceName = (identity?.CircuitName ?? "").Trim();
                if (machineId.Length == 0 || deviceName.Length == 0)
                    throw new InvalidDataException("机台ID和设备名称不能为空。");
            }
        }

        public static AutomaticSubmissionWriteResult Write(CadContext ctx, string filePath,
            IEnumerable<ObjectId[]> sourceGroups)
            // U1S is the explicit migration path for drawings whose table predates
            // socket-panel rows, so it may infer the missing 4.x panel material.
            => WriteCore(ctx, null, filePath, sourceGroups, null, true, false);

        public static AutomaticSubmissionWriteResult Write(CadContext ctx, Transaction transaction,
            string filePath, IEnumerable<ObjectId[]> sourceGroups)
            // U1F/U1U have just applied the user's editable table; preserve deletions.
            => WriteCore(ctx, transaction, filePath, sourceGroups, null, false, true);

        public static AutomaticSubmissionWriteResult Write(CadContext ctx, Transaction transaction,
            string filePath, IEnumerable<ObjectId[]> sourceGroups, FileBatchRollback batch)
            // Batch U1U follows the same post-edit contract as single-frame U1U.
            => WriteCore(ctx, transaction, filePath, sourceGroups, batch, false, true);

        public static IReadOnlyList<string> TargetPaths(string outputRoot,
            IEnumerable<string> machineIds)
            => (machineIds ?? Enumerable.Empty<string>())
                .GroupBy(machineId => (machineId ?? "").Trim(),
                    IdentityTextNormalizer.Comparer)
                // 只拼路径：这是"预期文件集"（供批次备份与指纹快照），真正写盘时才建目录，
                // 否则机台号写法变化会在客户输出目录里留下一个空的机台文件夹。
                .Select(group => BoqWorkbookWriter.ComposeTargetPath(outputRoot,
                    group.First()))
                .ToList();

        public static IReadOnlyList<BoqWorkbookRevision> CaptureTargetRevisions(
            string outputRoot, IEnumerable<string> machineIds)
            => TargetPaths(outputRoot, machineIds)
                .Select(BoqWorkbookWriter.CaptureRevision).ToList();

        public static void ValidateTargetRevisions(
            IEnumerable<BoqWorkbookRevision> revisions)
        {
            foreach (BoqWorkbookRevision revision in revisions
                ?? Enumerable.Empty<BoqWorkbookRevision>())
                BoqWorkbookWriter.ValidateRevision(revision);
        }

        /// <summary>
        /// D-057：清单设备列区分同名回路要用的台账行（这些机台）。快照未配置/
        /// 未刷新/读取失败时返回 null——退回按回路名的旧口径，不拦清单输出。
        /// </summary>
        private static List<UNCAD.Core.Excel.MachineRow> LoadRowsForDeviceLabels(
            IEnumerable<string> machineIds)
        {
            try
            {
                string configured = Settings.Get(ConfigKeys.FillExcelPath, "").Trim();
                if (configured.Length == 0) return null;
                if (!UNCAD.Core.Excel.MachineWorkbookSource.TryGetSnapshot(configured,
                        out _)) return null;
                return UNCAD.Core.Excel.MachineWorkbookSnapshotStore.Default
                    .ReadRowsForMachines(configured, machineIds ?? new string[0])
                    .ToList();
            }
            catch (System.Exception ex)
            {
                Log.Warn("读取机台快照失败，清单设备列按回路名区分（同名回路可能并列）："
                    + ex.Message);
                return null;
            }
        }

        private static AutomaticSubmissionWriteResult WriteCore(CadContext ctx,
            Transaction transaction, string filePath, IEnumerable<ObjectId[]> sourceGroups,
            FileBatchRollback externalBatch, bool inferLegacySocketPanels,
            bool allowEmptyMaterials = false)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Excel 自动记录文件路径为空。", nameof(filePath));

            List<SubmissionRecord> records = ModuleRunner.Run(BoqOutputModule.Descriptor,
                "读取图框记录", () => ReadRecords(ctx, transaction, sourceGroups,
                    inferLegacySocketPanels));
            if (records.Count == 0)
                throw new InvalidDataException("没有可写入 Excel 的图框记录。");

            // D-057：同机台同名回路（上游编号不同）在清单里各占一列/DATA 各占一行。
            // 列名按台账判定是否重名，带“（上游 X）”后缀；快照不可用时退回旧口径。
            BoqDeviceLabels.Apply(records, LoadRowsForDeviceLabels(
                records.Select(record => record.MachineId)));

            // 硬对账:图框内存在无法匹配固定清单的材料行时,生成的 xlsx 必然
            // 与图框实际内容不一致。报错中止,列出每一行,让用户先在 CAD 表中解决。
            var dropped = records.Where(record =>
                record.DroppedRows != null && record.DroppedRows.Count > 0).ToList();
            if (dropped.Count > 0)
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

            // PrepareTargetPath returns the selected root directory, not a file path.
            string outputRoot = Path.GetFullPath(filePath);
            string template = null;
            var outputPaths = records
                .GroupBy(record => record.MachineId.Trim(),
                    IdentityTextNormalizer.Comparer)
                .Select(group => BoqWorkbookWriter.BuildTargetPath(outputRoot,
                    group.First().MachineId))
                .ToList();
            if (outputPaths.Any(path => !File.Exists(path)))
                template = BoqWorkbookWriter.ResolveTemplatePath();
            var result = new AutomaticSubmissionWriteResult
            {
                FilePath = string.Join("; ", outputPaths),
                RecordCount = records.Count,
                MaterialCount = records.Sum(record => record.Materials.Count)
            };
            // U1F/U1U must also synchronize an intentionally empty CAD table. Filtering
            // such records out leaves stale quantities in the per-machine BOQ column.
            // U1S keeps its historical strict behavior and still refuses an empty submit.
            List<SubmissionRecord> recordsToWrite = allowEmptyMaterials
                ? records.ToList()
                : records.Where(record => record.Materials != null
                    && record.Materials.Count > 0).ToList();
            if (recordsToWrite.Count == 0)
            {
                result.FilePath = "";
                return result;
            }
            Action<FileBatchRollback> writeRecords = batch =>
            {
                List<IGrouping<string, SubmissionRecord>> machineGroups = recordsToWrite
                    .GroupBy(record => record.MachineId.Trim(),
                        IdentityTextNormalizer.Comparer)
                    .ToList();
                // D-057：清单标题模板一次读取（U1SET「图框与排版」）。
                BoqTitleTemplates titles = FillSettings.BoqTitles();
                int machineDone = 0;
                foreach (IGrouping<string, SubmissionRecord> machineGroup in machineGroups)
                {
                    machineDone++;
                    // 上千个机台时逐个重写工作簿很慢，必须有进度输出。
                    if (machineGroups.Count > 50
                        && (machineDone % 50 == 0 || machineDone == machineGroups.Count))
                        ReportBoqProgress(ctx, machineDone, machineGroups.Count);
                    string target = BoqWorkbookWriter.BuildTargetPath(outputRoot, machineGroup.Key);
                    // 长事务之后重新预检一次：用户可能在 CAD 编辑期间用 Excel 打开了目标文件。
                    OutputLayout.ValidateNotLocked(new[] { target });
                    bool existed = File.Exists(target);
                    var progress = new BoqWorkbookWriteProgress();
                    batch?.BeginWrite(target);
                    try
                    {
                        BoqWorkbookWriter.WriteTracked(target, template, machineGroup,
                            allowEmptyMaterials, progress, null, null, titles);
                        batch?.MarkWritten(target, progress.WrittenFingerprint);
                    }
                    catch (BoqExternalModificationException)
                    {
                        batch?.CancelWrite(target);
                        throw;
                    }
                    catch
                    {
                        if (progress.State == BoqWorkbookWriteState.Replaced)
                            batch?.MarkWritten(target, progress.WrittenFingerprint);
                        else
                            batch?.CancelWrite(target);
                        throw;
                    }
                    result.AddedCount += machineGroup.Count();
                    if (existed) result.ReplacedCount++;
                }
            };
            if (externalBatch != null)
            {
                ModuleRunner.Run(BoqOutputModule.Descriptor, "写入 BOQ 工作簿",
                    () => writeRecords(externalBatch));
            }
            else
            {
                // Snapshot every destination before the first write. A later machine failure must
                // restore earlier workbooks instead of leaving a partial batch.
                using (var batch = new FileBatchRollback(outputPaths))
                {
                    try
                    {
                        ModuleRunner.Run(BoqOutputModule.Descriptor, "写入 BOQ 工作簿",
                            () => writeRecords(batch));
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

        /// <summary>BOQ 逐机台写入的进度提示（大批量 U1U 时避免误以为卡死）。</summary>
        private static void ReportBoqProgress(CadContext ctx, int done, int total)
        {
            try
            {
                ctx.Write("\n[BOQ] 清单输出 " + done + "/" + total + " 个机台…");
                ctx.Ed.UpdateScreen();
            }
            catch (System.Exception ex)
            {
                Log.Warn("BOQ 进度提示失败: " + ex.Message);
            }
        }

        private static List<SubmissionRecord> ReadRecords(CadContext ctx,
            Transaction transaction, IEnumerable<ObjectId[]> sourceGroups,
            bool inferLegacySocketPanels)
        {
            var records = new List<SubmissionRecord>();
            Action<Transaction> read = current =>
            {
                var definitions = new CadBlockDefinitionReader(current);
                int index = 0;
                foreach (ObjectId[] ids in sourceGroups ?? Enumerable.Empty<ObjectId[]>())
                {
                    index++;
                    if (ids == null || ids.Length == 0) continue;
                    SubmissionRecord record = FrameIdentityReader.Read(ctx, current, ids,
                        inferLegacySocketPanels, definitions);
                    if (string.IsNullOrWhiteSpace(record.MachineId)
                        || string.IsNullOrWhiteSpace(record.DeviceName))
                        throw new InvalidDataException("第 " + index
                            + " 个图框缺少机台编号或设备名称，Excel 未更新。");
                    records.Add(record);
                }
            };
            if (transaction != null)
            {
                read(transaction);
                return records;
            }

            using (Transaction readTransaction = ctx.Db.TransactionManager.StartTransaction())
            {
                read(readTransaction);
                readTransaction.Commit();
            }
            return records;
        }
    }

#pragma warning restore 618
}
