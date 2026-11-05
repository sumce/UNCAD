using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Submission;
using UNCAD.Features.Submit;
using UNCAD.Infra;
using UNCAD.UI;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// Preflights and writes several already-filled frame regions without reopening the machine
    /// picker or one review dialog per frame. New generation remains in FillFeature's single path.
    /// </summary>
    internal static class BatchFillUpdateCoordinator
    {
        private sealed class Plan
        {
            public FrameRegionGroup Region { get; set; }
            public FillSelection Selection { get; set; }
            public MachineRow Machine { get; set; }
            public SummationOutput Summation { get; set; }
            public CableStatResult Statistics { get; set; }
            public List<TableFillRow> Rows { get; set; }
            public FillReviewData Review { get; set; }
            public CableReserveInfo Reserve { get; set; }
            public Core.Fill.FrameInfoJsonRecord PreviousRecord { get; set; }
            public List<TableFillRow> ExistingRows { get; set; }
            public string LegacyLastUpdated { get; set; } = "";
            public string DeviceState { get; set; }
            public bool DeviceOutletStateKnown { get; set; }
            public string CableRequestKey { get; set; }
            public string BusPlugBoxRequestKey { get; set; }
            public bool HoseWasMissingBeforeCableChoice { get; set; }
            /// <summary>预检阶段（在预检读事务里）算好的表格可写行数；-1 表示未计算。</summary>
            public int Capacity { get; set; } = -1;
            /// <summary>该图框的提示（统计类别不完整/Downstream 状态等），进批量结果窗。</summary>
            public List<string> Notes { get; set; } = new List<string>();
            /// <summary>D-043：已吸收的老版设备预留标注实体（写事务里删除）。</summary>
            public List<ObjectId> DeviceReserveEraseIds { get; set; } = new List<ObjectId>();
        }

        public static void Execute(CadContext ctx, IReadOnlyList<FrameRegionGroup> regions)
        {
            if (regions == null || regions.Count < 2) return;
            FillRuntimeOptions options = FillSettings.Current();
            StatisticsSettingsSnapshot statisticsSettings = StatisticsSettings.Current();
            string path = FillFeature.ResolveMachineWorkbookPath(ctx, options.MachineWorkbookPath);
            if (path == null) return;

            FillWorkbookSnapshot workbook;
            try
            {
                workbook = FillWorkbookSnapshot.Load(path);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[U1U] 读取机台 SQLite 快照失败: " + ex.Message);
                Log.Error("U1U batch read machine SQLite snapshot failed", ex);
                return;
            }
            if (workbook.MachineIds.Count == 0 || workbook.ListItems.Count == 0)
            {
                ctx.Write("\n[U1U] 机台 Excel 或内嵌固定清单没有可用数据。");
                return;
            }

            var plans = new List<Plan>();
            var errors = new List<string>();
            var catalogRequests = new List<BatchCatalogRequest>();
            // 分阶段 PERF：大批量“无响应”排查靠这行数据（对齐 XLAYOUT/XSTS 的口径）。
            var perfTotal = System.Diagnostics.Stopwatch.StartNew();
            long perfPreflight = 0, perfWrite = 0, perfBoq = 0;
            ctx.Write("\n[U1U] 正在批量预检 " + regions.Count + " 个图框，请稍候...");
            var perfPhase = System.Diagnostics.Stopwatch.StartNew();
            // 千/万级图框：进度窗让用户看到进展，不再“无响应”。
            var preflightProgress = new ProgressWindow("U1U 批量预检");
            preflightProgress.Show();
            int preflightStep = Math.Max(1, regions.Count / 100);
            try
            {
            using (Transaction readTransaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(readTransaction);
                int preflightDone = 0;
                foreach (FrameRegionGroup region in regions)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    Preflight(ctx, readTransaction, region, options, workbook, plans,
                        errors, catalogRequests, definitions, statisticsSettings);
                    clock.Stop();
                    preflightDone++;
                    Log.Info("U1U 批量预检 图框 " + region.Handle + ": "
                        + clock.ElapsedMilliseconds + " ms");
                    // 上千个图框时没有进度输出会被误认为卡死。
                    if (preflightDone % preflightStep == 0 || preflightDone == regions.Count)
                    {
                        preflightProgress.Update("批量预检图框（机台/回路/统计）",
                            preflightDone, regions.Count);
                        FlushProgress(ctx);
                    }
                }
                readTransaction.Commit();
            }
            }
            finally { preflightProgress.Close(); }
            perfPreflight = perfPhase.ElapsedMilliseconds;
            ctx.Write("\n[U1U] 批量预检完成，开始生成更新计划...");

            // 首次使用的图框：在 GUI 里逐框填写设备内预留（不同设备预留通常
            // 不同，不能整批共用一个值）；已有 frameinfo_json 记录的图框保持
            // 原值，不再询问。命令行不出现任何输入提示。
            List<Plan> reserveMissing = plans.Where(plan => plan.Reserve != null
                && (plan.PreviousRecord == null
                    || string.IsNullOrWhiteSpace(plan.PreviousRecord.DeviceReserveMm)))
                .ToList();
            if (reserveMissing.Count > 0)
            {
                var reserveRows = reserveMissing.ConvertAll(plan =>
                    new DeviceReserveRow(plan.Machine?.MachineId ?? "",
                        plan.Machine?.CircuitName ?? ""));
                // 3.0 WPF 窗体（D-038）：确认与否用 Confirmed 表达。
                var reserveWindow = new DeviceReserveWindow(reserveRows);
                AcApplication.ShowModalWindow(reserveWindow);
                if (!reserveWindow.Confirmed)
                {
                    ctx.Write("\n[U1U] 已取消批量更新，图纸未修改。");
                    return;
                }
                for (int i = 0; i < reserveMissing.Count; i++)
                {
                    Plan plan = reserveMissing[i];
                    double reserveMillimeters = reserveWindow.ReserveMillimeters(i);
                    FillFeature.ApplyDeviceReserve(plan.Statistics, plan.Reserve,
                        reserveMillimeters);
                    plan.Review.DeviceReserveMm =
                        UNCAD.Core.Text.TextFormatter.FormatNum(reserveMillimeters);
                    plan.Review.SyncCableReserveMeters();
                }
            }

            if (errors.Count > 0)
            {
                string message = "批量更新预检失败，图纸未修改：\r\n\r\n"
                    + string.Join("\r\n", errors.Take(20));
                if (errors.Count > 20) message += "\r\n其余 " + (errors.Count - 20) + " 项请查看日志。";
                MessageBox.Show(Owner(), message, "U1U 批量预检",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                foreach (string error in errors) ctx.Write("\n[U1U] " + error);
                return;
            }

            if (catalogRequests.Count > 0)
            {
                // 3.0 WPF 窗体（D-038）：结果用 Confirmed 表达。
                var picker = new BatchCatalogSelectionWindow(catalogRequests);
                AcApplication.ShowModalWindow(picker);
                if (!picker.Confirmed) return;
                foreach (Plan plan in plans)
                {
                    if (!string.IsNullOrWhiteSpace(plan.CableRequestKey))
                    {
                        plan.Review.ReplaceWithCatalogItem(plan.Review.CableItem(),
                            picker.Selections[plan.CableRequestKey], workbook.Catalog);
                        if (plan.HoseWasMissingBeforeCableChoice
                            && plan.Review.FlexibleConduitItem() != null
                            && !FillFeature.ApplyRuanguanLength(ctx, plan.Selection,
                                plan.Review, workbook.Catalog, options.Planning, true))
                            return;
                    }
                    if (!string.IsNullOrWhiteSpace(plan.BusPlugBoxRequestKey))
                        plan.Review.ReplaceWithCatalogItem(plan.Review.BusPlugBoxItem(),
                            picker.Selections[plan.BusPlugBoxRequestKey], workbook.Catalog);
                }
            }

            // A merged/deleted material row has no fixed-catalog identity. Do not write it:
            // a CAD row without a BOQ code would make the two sources disagree.
            List<Tuple<Plan, FillReviewItem>> unresolvedDefaults = plans
                .SelectMany(plan => (plan.Review?.Items ?? new List<FillReviewItem>())
                    .Where(item => item.RequiresCatalogConfirmation)
                    .Select(item => Tuple.Create(plan, item)))
                .ToList();
            if (unresolvedDefaults.Count > 0)
            {
                List<string> details = unresolvedDefaults.Select((entry, index) =>
                    (index + 1) + ". 机台：" + PromptValue(entry.Item1.Machine.MachineId)
                    + "；回路：" + PromptValue(entry.Item1.Machine.CircuitName)
                    + "；图框：" + PromptValue(entry.Item1.Region.Handle)
                    + "\r\n   项目：" + PromptValue(entry.Item2.Name)
                    + "；配电信息：" + PromptValue(entry.Item1.Machine.Detail))
                    .ToList();
                foreach (string detail in details)
                    ctx.Write("\n[U1U] 未匹配固定清单："
                        + detail.Replace("\r\n   ", "；"));
                string dialogDetails = string.Join("\r\n", details.Take(12));
                if (details.Count > 12)
                    dialogDetails += "\r\n其余 " + (details.Count - 12)
                        + " 项已输出到 CAD 命令行。";
                MessageBox.Show(Owner(),
                    "批量更新中有 " + unresolvedDefaults.Count
                        + " 个清单项目未匹配固定清单（可能已在现有表格中合并）。\r\n"
                        + "\r\n未匹配明细：\r\n" + dialogDetails + "\r\n\r\n"
                        + "。批量更新已取消。请删除这些行，或在单图 U1U 中选择固定清单替代项后重试。",
                    "U1U 未匹配固定清单", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            foreach (Plan plan in plans)
            {
                List<FillReviewItem> unresolved = plan.Review?.Items.Where(item =>
                    item.RequiresCatalogConfirmation).ToList()
                    ?? new List<FillReviewItem>();
                if (unresolved.Count > 0)
                {
                    errors.Add("机台 " + plan.Machine.MachineId + "；设备 "
                        + plan.Machine.CircuitName + "；图框 " + plan.Region.Handle
                        + "：固定清单仍有未匹配项目。" + string.Join("、",
                            unresolved.Select(item => item.Name)));
                    continue;
                }
                plan.Rows = plan.Review.SelectedRows();
                int capacity = plan.Capacity >= 0
                    ? plan.Capacity
                    : FillFeature.ResolveTableWriteCapacity(ctx,
                        plan.Selection.TableIds, options.StartRow, options.ClearRows);
                if (plan.Rows.Count > capacity)
                    errors.Add("机台 " + plan.Machine.MachineId + "；设备 "
                        + plan.Machine.CircuitName + "；图框 " + plan.Region.Handle
                        + "：清单 " + plan.Rows.Count + " 项超出表格容量 " + capacity + " 行。");
            }
            if (errors.Count > 0)
            {
                foreach (string error in errors) ctx.Write("\n[U1U] " + error);
                MessageBox.Show(Owner(), "批量更新确认失败，图纸未修改：\r\n\r\n"
                    + string.Join("\r\n", errors.Take(20)), "U1U 批量确认",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            foreach (Plan plan in plans)
                ctx.Operation?.Machine(plan.Machine.MachineId, plan.Machine.CircuitName);

            // 2.10.0 整机回路校验（U1SET 可关闭）：缺回路的机台只更新图纸、
            // 跳过该机 BOQ（部分回路的 BOQ 会丢掉未选回路的工程量），不再拦
            // 整批更新；补全回路后由用户执行 U1S 补清单。
            // D-048（2026-11-04）：缺回路机台先弹强制生成选择窗——默认全部不勾
            // 选 = 照旧跳过；用户勾选并确认的机台按已画回路强制生成 BOQ，
            // 结果窗备注列标注。勾选与否都不拦图纸更新。
            List<CircuitCoverageResult> coverageFailed = FillCoverageGate
                .EvaluateCircuitCoverage(ctx, workbook, CommandIds.FillUpdate,
                    plans.Select(plan => new KeyValuePair<string, string>(
                        plan.Machine.MachineId, plan.Machine.CircuitName)));
            var coverageSet = new HashSet<string>(
                coverageFailed.Select(result => result.MachineId),
                UNCAD.Core.Text.IdentityTextNormalizer.Comparer);
            HashSet<string> forcedSet = FillCoverageGate.OfferForceBoq(
                ctx, CommandIds.FillUpdate, coverageFailed);
            List<Plan> boqPlans = plans.Where(plan => !coverageSet.Contains(
                plan.Machine.MachineId)
                || forcedSet.Contains(plan.Machine.MachineId)).ToList();
            foreach (Plan plan in plans)
            {
                if (coverageSet.Contains(plan.Machine.MachineId)
                    && forcedSet.Contains(plan.Machine.MachineId))
                    plan.Notes.Add("BOQ 已强制生成（回路不全，仅含已画回路）；"
                        + "补画后可执行 " + CommandIds.Submit + " 重出完整清单。");
            }
            if (coverageFailed.Count > 0)
            {
                int skippedCount = coverageFailed.Count - forcedSet.Count;
                ctx.Write("\n[U1U] 有 " + coverageFailed.Count
                    + " 台机台回路不全：跳过 " + skippedCount + " 台 BOQ"
                    + (forcedSet.Count > 0
                        ? "，强制生成 " + forcedSet.Count + " 台（仅含已画回路，"
                            + "见结果窗备注）" : "")
                    + "；全部回路画完后执行 " + CommandIds.Submit
                    + " 生成清单（明细见结果窗）。");
            }

            string automaticExcelPath;
            IReadOnlyList<BoqWorkbookRevision> automaticExcelRevisions;
            try
            {
                AutomaticSubmissionService.ValidateIdentityKeys(boqPlans.Select(plan =>
                    new KeyValuePair<string, string>(plan.Machine.MachineId,
                        plan.Machine.CircuitName)));
                automaticExcelPath = AutomaticSubmissionService.PrepareTargetPath(
                    boqPlans.Select(plan => plan.Machine.MachineId));
                automaticExcelRevisions = AutomaticSubmissionService.CaptureTargetRevisions(
                    automaticExcelPath, boqPlans.Select(plan => plan.Machine.MachineId));
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[U1U] BOQ 输出路径不可用，图纸未修改: "
                    + ex.Message);
                return;
            }

            // U1U 强化:批量确认前展示每框的上次更新时间/用户与清单并排对比。
            // 2026-11-04:门控改 HasChanges——内容没有变化的图框不进对比窗
            // (不再按 HasHistory 打扰);电缆长度卡片数据取 plan.Reserve。
            var compareItems = plans
                .Select(plan => FillFeature.BuildCompareItem(
                    plan.Machine, plan.Rows, plan.ExistingRows, plan.PreviousRecord,
                    plan.Region.Handle, plan.LegacyLastUpdated, plan.Reserve))
                .Where(item => item.HasChanges)
                .ToList();
            if (compareItems.Count == 0)
                ctx.Write("\n[U1U] 所选图框内容均无变化，跳过对比，直接更新。");
            else
            {
                // 3.0 WPF 窗体（D-038）：结果用 Confirmed 表达。
                var compareWindow = new FillUpdateCompareWindow(compareItems);
                AcApplication.ShowModalWindow(compareWindow);
                if (!compareWindow.Confirmed)
                {
                    ctx.Write("\n[U1U] 已取消批量更新，图纸未修改。");
                    return;
                }
            }
            var confirmationRows = plans.Select(plan => new BatchFillConfirmationRow(
                plan.Region.Handle, plan.Machine.MachineId, plan.Machine.CircuitName,
                plan.DeviceState, plan.Rows.Count)).ToList();
            // 3.0 WPF 窗体（D-038）：结果用 Confirmed 表达，取消/关闭/Esc 一律 false。
            var confirmationWindow = new BatchFillConfirmationWindow(confirmationRows);
            AcApplication.ShowModalWindow(confirmationWindow);
            if (!confirmationWindow.Confirmed) return;

            try
            {
                AutomaticSubmissionService.ValidateTargetRevisions(
                    automaticExcelRevisions);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[U1U] BOQ 文件在确认期间发生变化，图纸未修改: "
                    + ex.Message);
                return;
            }

            AutomaticSubmissionWriteResult automaticExcel = null;
            var machineStats = new Dictionary<string, BatchUpdateMachineRow>(
                UNCAD.Core.Text.IdentityTextNormalizer.Comparer);
            int tableRows = 0;
            int frameBlocks = 0;
            int attributeValues = 0;
            int migratedBridgeLabels = 0;
            XFrameMigrationResult xframeMigration = new XFrameMigrationResult(0, 0, 0);
            int deviceColorBlocks = 0;
            int upstreamColorBlocks = 0;
            short deviceColorIndex = FillColorSettings.DeviceColorIndex();
            short upstreamColorIndex = FillColorSettings.UpstreamColorIndex();
            using (var outputBatch = new FileBatchRollback(
                AutomaticSubmissionService.TargetPaths(automaticExcelPath,
                    boqPlans.Select(plan => plan.Machine.MachineId))))
            {
            // Close the gap between the post-confirmation check and batch snapshot.
            // BeginWrite performs the final check after the CAD work is prepared.
            AutomaticSubmissionService.ValidateTargetRevisions(
                automaticExcelRevisions);
            try
            {
                perfPhase.Restart();
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    xframeMigration = XFrameMigrationService.Migrate(ctx, transaction,
                        plans.Select(plan => plan.Selection));
                    foreach (Plan plan in plans)
                    {
                        int migratedCapacity = FillFeature.ResolveTableWriteCapacity(
                            transaction, plan.Selection.TableIds, options.StartRow,
                            options.ClearRows);
                        if (plan.Rows.Count > migratedCapacity)
                            throw new InvalidOperationException("图框 "
                                + plan.Region.Handle + " 迁移后的新版清单表容量只有 "
                                + migratedCapacity + " 行，当前清单需要 "
                                + plan.Rows.Count + " 行；本次已回滚。");
                    }
                    // Shared once-per-batch state: scanning frameinfo_json inserts and
                    // normalizing shared block definitions per frame used to repeat
                    // whole-space/whole-definition walks inside the write transaction.
                    FrameInfoJsonBlockWriter.MetadataBlockIndex metadataIndex =
                        FrameInfoJsonBlockWriter.MetadataBlockIndex.Scan(ctx, transaction);
                    var normalizedDefinitions = new HashSet<ObjectId>();
                    int writeDone = 0;
                    var writeProgress = new ProgressWindow("U1U 写入图纸");
                    writeProgress.Show();
                    int writeStep = Math.Max(1, plans.Count / 100);
                    try
                    {
                    foreach (Plan plan in plans)
                    {
                        writeDone++;
                        if (writeDone % writeStep == 0 || writeDone == plans.Count)
                        {
                            writeProgress.Update("写入图框（表格/块/JSON）",
                                writeDone, plans.Count);
                            FlushProgress(ctx);
                        }
                        // D-043：删除已吸收的老版设备预留横排标注。
                        DeviceReserveMarkerReader.EraseAbsorbed(transaction,
                            plan.DeviceReserveEraseIds.ToArray(), CommandIds.FillUpdate);
                        migratedBridgeLabels += BridgeLabelMigrationWriter.Migrate(
                            transaction, plan.Selection.TextIds, options.MmPerGrid);
                        int filled = FillTableModule.Write(ctx, transaction,
                            plan.Selection.TableIds, options.StartRow, options.ClearRows,
                            plan.Rows, options.TextHeight);
                        if (filled < 0)
                            throw new InvalidOperationException("图框 " + plan.Region.Handle
                                + " 的清单表写入失败。");
                        tableRows += filled;
                        CadDrawingInfoTableWriter.Write(transaction,
                            plan.Selection.DrawingInfoTableIds, plan.Machine, DateTime.Now);

                        FillWriteResult frame = CadBlockAttributeWriter.FillFrame(ctx, transaction,
                            plan.Selection.FrameBlockIds, plan.Machine,
                            plan.Statistics,
                            plan.Statistics.CableState == MeasurementState.Unknown,
                            plan.Statistics.ConduitState == MeasurementState.Unknown);
                        FillWriteResult device = CadBlockAttributeWriter.FillDeviceName(ctx,
                            transaction, plan.Selection.DeviceBlockIds,
                            DeviceBlockFiller.BuildValue(plan.Machine));
                        string hoseDiameter = plan.Review?.Machine?.Dia
                            ?? plan.Machine.Dia;
                        FillWriteResult ruanguan = RuanguanBlockWriter.FillModelAndLength(ctx,
                            transaction, plan.Selection.RuanguanBlockIds, hoseDiameter);
                        FillWriteResult frameInfo = FrameInfoJsonBlockWriter.FillOrMigrate(ctx,
                            transaction, plan.Selection.FrameBlockIds,
                            plan.Selection.FrameInfoJsonBlockIds, plan.Machine,
                            plan.Review, CommandIds.FillUpdate, metadataIndex,
                            plan.Reserve, plan.PreviousRecord);
                        FillWriteResult upstreamInfo = CadBlockAttributeWriter.FillTagged(ctx,
                            transaction, plan.Selection.UpstreamInfoBlockIds,
                            ConnectionBlockFiller.TagUpstreamInfo,
                            ConnectionBlockFiller.UpstreamInfo(plan.Machine), true);
                        FillWriteResult upstreamState = CadDynamicBlockStateService.FillUpstreamState(
                            ctx, transaction, plan.Selection.UpstreamStateBlockIds,
                            plan.Machine.Next);
                        FillWriteResult upstreamAxis = CadBlockAttributeWriter.FillTagged(ctx,
                            transaction, plan.Selection.UpstreamAxisBlockIds,
                            ConnectionBlockFiller.TagUpstreamAxis,
                            ConnectionBlockFiller.UpstreamAxis(plan.Machine), false);
                        FillWriteResult downstreamAxis = CadBlockAttributeWriter.FillTagged(ctx,
                            transaction, plan.Selection.DownstreamAxisBlockIds,
                            ConnectionBlockFiller.TagDownstreamAxis,
                            ConnectionBlockFiller.DownstreamAxis(plan.Machine), false);
                        deviceColorBlocks += CadBlockColorWriter.Apply(transaction,
                            plan.Selection.DeviceBlockIds.Concat(
                                plan.Selection.DownstreamAxisBlockIds)
                                .Concat(plan.Selection.DeviceColorBlockIds),
                            deviceColorIndex, normalizedDefinitions);
                        upstreamColorBlocks += CadBlockColorWriter.Apply(transaction,
                            plan.Selection.UpstreamStateBlockIds
                                .Concat(plan.Selection.UpstreamInfoBlockIds)
                                .Concat(plan.Selection.UpstreamAxisBlockIds)
                                .Concat(plan.Selection.UpstreamColorBlockIds),
                            upstreamColorIndex, normalizedDefinitions);
                        frameBlocks += frame.Blocks + device.Blocks + ruanguan.Blocks
                            + frameInfo.Blocks
                            + upstreamInfo.Blocks
                            + upstreamState.Blocks + upstreamAxis.Blocks + downstreamAxis.Blocks;
                        attributeValues += frame.Values + device.Values + ruanguan.Values
                            + frameInfo.Values
                            + upstreamInfo.Values
                            + upstreamState.Values + upstreamAxis.Values + downstreamAxis.Values;
                        // D-042 批量结果窗：按机台聚合写入量与提示，命令行不再展开。
                        string machineKey = plan.Machine.MachineId ?? "";
                        if (!machineStats.TryGetValue(machineKey,
                                out BatchUpdateMachineRow machineRow))
                            machineStats[machineKey] = machineRow =
                                new BatchUpdateMachineRow(machineKey);
                        machineRow.Frames++;
                        machineRow.TableRows += filled;
                        machineRow.AttributeValues += frame.Values + device.Values
                            + ruanguan.Values + frameInfo.Values + upstreamInfo.Values
                            + upstreamState.Values + upstreamAxis.Values
                            + downstreamAxis.Values;
                        foreach (string note in plan.Notes) machineRow.AddNote(note);
                    }
                    }
                    finally { writeProgress.Close(); }
                    // 提交前只做 CAD：读表会让 AutoCAD 懒重算，提交前先按固定行高回写一次。
                    foreach (Plan plan in plans)
                        CadTableFillWriter.FixGeneratedRowHeights(transaction,
                            plan.Selection.TableIds, options.StartRow, options.ClearRows);
                    transaction.Commit();
                }
            }
            catch (System.Exception ex)
            {
                // 事务没有提交 → 全部图框自动回滚；BOQ 文件此时还没有 BeginWrite。
                string rollbackStatus = "所有图框和 BOQ 修改均已回滚。";
                try { outputBatch.Rollback(); }
                catch (System.Exception rollbackError)
                {
                    rollbackStatus = "所有图框修改均已回滚；BOQ 文件未开始写入。";
                    Log.Error("U1U batch rollback failed (CAD already rolled back)",
                        rollbackError);
                }
                Log.Error("U1U batch write failed", ex);
                ctx.Write("\n[U1U] 批量写入失败；" + rollbackStatus + " " + ex.Message);
                MessageBox.Show(Owner(), "批量写入失败；" + rollbackStatus
                    + "\r\n\r\n" + ex.Message,
                    "U1U", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            perfWrite = perfPhase.ElapsedMilliseconds;

            // CAD 已提交：按 docs/DEVELOPMENT.md 的顺序，现在才读真实 CAD 结果并写 BOQ。
            // 这一段失败不再回滚图纸，只回滚本批 BOQ 文件，并明确要求重跑 U1S。
            perfPhase.Restart();
            try
            {
                AutomaticSubmissionService.ValidateTargetRevisions(
                    automaticExcelRevisions);
                using (Transaction readTransaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    automaticExcel = AutomaticSubmissionService.Write(ctx, readTransaction,
                        automaticExcelPath, boqPlans.Select(plan => plan.Selection.SourceIds),
                        outputBatch);
                    readTransaction.Commit();
                }
                // 读清单表会触发 AutoCAD 懒重算，提交后必须再按固定行高回写一次。
                using (Transaction fixTransaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    foreach (Plan plan in plans)
                        CadTableFillWriter.FixGeneratedRowHeights(fixTransaction,
                            plan.Selection.TableIds, options.StartRow, options.ClearRows);
                    fixTransaction.Commit();
                }
                outputBatch.Complete();
                FillFeature.RecordOutputs(ctx, "BOQ", automaticExcel.FilePath);
            }
            catch (System.Exception ex)
            {
                string rollbackStatus = "本批 BOQ 文件已恢复为修改前的版本。";
                try { outputBatch.Rollback(); }
                catch (System.Exception rollbackError)
                {
                    rollbackStatus = "部分 BOQ 文件无法安全恢复，已保留当前文件。";
                    Log.Error("图纸已提交，但部分 BOQ 文件回滚不完整。", new AggregateException(
                        "图纸已提交，但部分 BOQ 文件回滚不完整。", ex, rollbackError));
                }
                Log.Error("U1U BOQ 输出失败（CAD 已提交）", ex);
                ctx.Write("\n[U1U] 图纸已更新，但 BOQ 输出失败；" + rollbackStatus
                    + " 请重跑 " + CommandIds.Submit + " 重新导出清单：" + ex.Message);
                MessageBox.Show(Owner(), "图纸已更新，但 BOQ 输出失败；" + rollbackStatus
                    + "\r\n\r\n请重跑 " + CommandIds.Submit + " 重新导出清单。\r\n\r\n"
                    + ex.Message, "U1U", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            }

            SelectionService.ClearPickFirst(ctx);
            perfBoq = perfPhase.ElapsedMilliseconds;
            perfTotal.Stop();
            Log.Info("[PERF] U1U total=" + perfTotal.ElapsedMilliseconds + "ms"
                + " preflight=" + perfPreflight + "ms"
                + " gui+plan=" + (perfTotal.ElapsedMilliseconds - perfPreflight
                    - perfWrite - perfBoq) + "ms"
                + " write=" + perfWrite + "ms"
                + " boq=" + perfBoq + "ms"
                + " frames=" + plans.Count);
            if (xframeMigration.Frames > 0 || xframeMigration.BoqTables > 0
                || xframeMigration.DrawingInfoTables > 0)
                ctx.Write("\n[U1U] 图框已升级：xframe_v2 " + xframeMigration.Frames
                    + " 个，BOQ 表替换 " + xframeMigration.BoqTables
                    + " 个，制图信息表替换 " + xframeMigration.DrawingInfoTables + " 个。");
            if (deviceColorBlocks + upstreamColorBlocks > 0)
                ctx.Write("\n[U1U] 颜色已校正：设备端 " + deviceColorBlocks
                    + " 个块，上游端 " + upstreamColorBlocks + " 个块。");
            // D-042 交互（2026-11-03）：批量“做了什么/结果如何/文件在哪”在结果窗里看，
            // 命令行只留一行摘要——不再把逐个 BOQ 路径刷进终端。
            List<BatchUpdateMachineRow> resultRows = machineStats.Values
                .OrderBy(row => row.MachineId, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (BatchUpdateMachineRow row in resultRows)
            {
                if (coverageSet.Contains(row.MachineId)
                    && forcedSet.Contains(row.MachineId))
                {
                    // D-048：用户确认强制生成的机台照常出了 BOQ，标注来源。
                    row.BoqStatus = "已强制生成（缺回路）";
                    row.BoqPath = UNCAD.Core.Submission.BoqWorkbookWriter.ComposeTargetPath(
                        automaticExcelPath, row.MachineId);
                    continue;
                }
                if (coverageSet.Contains(row.MachineId))
                {
                    row.BoqStatus = "跳过（回路不全）";
                    row.AddNote("回路不全：本次只更新图纸、不生成该机 BOQ；全部回路画完后执行 "
                        + CommandIds.Submit + " 生成清单。");
                    continue;
                }
                row.BoqStatus = "已生成";
                row.BoqPath = UNCAD.Core.Submission.BoqWorkbookWriter.ComposeTargetPath(
                    automaticExcelPath, row.MachineId);
            }
            ctx.Write("\n[U1U] 批量完成：图框 " + plans.Count + " 个 / 机台 "
                + resultRows.Count + " 台，表格 " + tableRows + " 行、块 " + frameBlocks
                + " 个共更新 " + attributeValues + " 项属性"
                + (migratedBridgeLabels > 0 ? "；旧桥架标注升级/角度修复 "
                    + migratedBridgeLabels + " 个" : "")
                + "；明细见结果窗。"
                + (coverageFailed.Count > 0
                    ? " 有 " + coverageFailed.Count + " 台回路不全（其中 "
                        + forcedSet.Count + " 台按确认强制生成了 BOQ）。" : ""));
            var resultWindow = new BatchUpdateResultWindow(resultRows, plans.Count,
                tableRows, frameBlocks, attributeValues,
                automaticExcel?.AddedCount ?? 0, automaticExcel?.ReplacedCount ?? 0,
                automaticExcelPath);
            AcApplication.ShowModalWindow(resultWindow);
        }

        private static void Preflight(CadContext ctx, Transaction readTransaction,
            FrameRegionGroup region,
            FillRuntimeOptions options, FillWorkbookSnapshot workbook,
            List<Plan> plans, List<string> errors,
            List<BatchCatalogRequest> catalogRequests,
            CadBlockDefinitionReader definitions, StatisticsSettingsSnapshot statisticsSettings)
        {
            string prefix = "图框 " + region.Handle + "：";
            FillSelection selection = FillSelectionCollector.Split(readTransaction,
                region.EntityIds.ToArray(), true, definitions);
            // 复用分区结果：拿到已算好的图框区域，避免每个图框再做一次全空间扫描。
            selection.Region = region;
            if (selection.FrameBlockIds.Length != 1)
            {
                errors.Add(prefix + "没有读取到唯一图框块。");
                return;
            }

            // Resolve identity before reporting table shape so each structural error carries the
            // machine and device when available. Any failed preflight still returns before planning.
            bool validTableShape = selection.TableIds.Length == 1;
            bool validIdentity = FillSelectionCollector.TryReadExistingIdentity(readTransaction,
                selection,
                out ExistingFillIdentity identity, out string identityError);
            MachineRow machine = null;
            string matchError = "";
            if (!validIdentity)
            {
                errors.Add(prefix + identityError);
            }
            else
            {
                machine = ExistingFillIdentityResolver.MatchMachine(identity,
                    workbook.FindRows(identity.MachineId), out matchError);
                if (machine == null) errors.Add(prefix + matchError);
            }
            if (machine != null)
            {
                prefix = "机台 " + machine.MachineId + "；设备 " + machine.CircuitName
                    + "；图框 " + region.Handle + "：";
            }
            if (!validTableShape)
            {
                errors.Add(prefix + "需要且只能包含一个清单表，实际 "
                    + selection.TableIds.Length + " 个。");
            }
            if (!validIdentity || machine == null || !validTableShape)
                return;
            string originalCableModel = (machine.Cable ?? "").Trim();
            // 该图框的提示（进批量结果窗的机台备注列；同一机台多框会自动去重）。
            var notes = new List<string>();
            // D-043：设备预留标记吸收结果（备注文本 + 待删除实体）。
            string markerNote = null;
            List<ObjectId> plan_DeviceReserveEraseIds = new List<ObjectId>();

            // 首次 U1U 时读 frameinfo_json 决定设备内预留；没有记录才询问一次并写入 JSON。
            // 必须同时读图框块上的旧内嵌 FRAMEINFO_JSON：迁移会移除该属性，晚读就丢了。
            // 2026-11-04：带句柄校验——复制图框的源框 JSON（预留/替代型号/历史）
            // 不继承，备注进结果窗告知用户。
            // 2026-11-05：json 读取与 D-043 标记决策提前到统计之前——吸收的横排
            // 毫米标（如 "3000mm"）同时命中电缆长度判据（^数字0mm$），不排除会被
            // 米数统计双算（统计一次、设备内预留又一次）。吸收的文本从统计排除。
            FrameInfoJsonRecord previousRecord = FrameInfoJsonBlockWriter.Read(readTransaction,
                FrameInfoJsonBlockWriter.ReadSources(selection),
                FrameInfoJsonBlockWriter.OwnFrameHandle(readTransaction,
                    selection.FrameBlockIds),
                out string foreignJsonNote);
            if (foreignJsonNote.Length > 0)
                notes.Add("检测到复制图框遗留的其他图框 JSON，已忽略："
                    + "不继承其设备内预留/电缆替代型号/更新历史。");
            // 现场需求（2026-11-02）：JSON 没有设备内预留记录时，默认采用图框
            // CABLE_INFO 文本里已有的「数值（设备内）」——不询问用户；恢复值
            // 写回记录，后续不重复恢复并随 JSON 持久化。
            if (previousRecord == null
                || string.IsNullOrWhiteSpace(previousRecord.DeviceReserveMm))
            {
                string cableInfo = CadBlockAttributeWriter.ReadFirstTagged(readTransaction,
                    selection.FrameBlockIds, UNCAD.Core.Fill.FrameBlockFiller.TagCable);
                if (UNCAD.Core.Fill.CableReserveReview.TryParseDeviceReserveMillimeters(
                        cableInfo, out double recovered))
                {
                    previousRecord = previousRecord ?? new FrameInfoJsonRecord();
                    previousRecord.DeviceReserveMm =
                        UNCAD.Core.Text.TextFormatter.FormatNum(recovered);
                }
            }
            // D-043：老版横排毫米标自动识别（U1SET 可关）。恰好一处→吸收并删除
            // 标注；多处→不乱取，备注进结果窗交人工确认。
            DeviceReserveMarkerReader.Result reserveMarkers =
                DeviceReserveMarkerReader.Collect(readTransaction,
                    selection.TextIds);
            var markerExcludedIds = new List<ObjectId>();
            if (reserveMarkers.Hits.Count == 1)
            {
                double? decided = DeviceReserveMarker.Decide(
                    options.DeviceReserveAutoDetect, reserveMarkers.Hits,
                    FillFeature.ParseJsonReserve(previousRecord), out List<int> absorbed);
                if (absorbed.Count > 0)
                {
                    previousRecord = previousRecord ?? new FrameInfoJsonRecord();
                    previousRecord.DeviceReserveMm =
                        UNCAD.Core.Text.TextFormatter.FormatNum(decided.Value);
                    plan_DeviceReserveEraseIds = reserveMarkers.EntityIds;
                    markerExcludedIds.AddRange(reserveMarkers.EntityIds);
                    markerNote = "设备内预留自动识别 " + reserveMarkers.Hits[0].Text
                        + " mm（已删除图上标注，不计入米数统计）";
                }
            }
            else if (reserveMarkers.Hits.Count > 1)
            {
                markerNote = "发现 " + reserveMarkers.Hits.Count + " 处横排毫米标（"
                    + string.Join("、", reserveMarkers.Hits.ConvertAll(hit => hit.Text))
                    + "），未自动吸收；请人工确认";
            }

            SummationOutput summation = FillStatisticsModule.Execute(ctx, readTransaction,
                selection.TextIds, options.MmPerGrid, selection.StatisticsScopeComplete,
                statisticsSettings, markerExcludedIds);
            CableStatResult statistics = summation.Statistics;
            if (statistics.CableState == MeasurementState.Unknown
                || statistics.BridgeState == MeasurementState.Unknown
                || statistics.ConduitState == MeasurementState.Unknown)
            {
                // 批量结果窗按机台展示；命令行只在日志里保留逐帧明细。
                notes.Add("存在未启用或不完整的统计类别，仅这些类别保留旧值。");
                Log.Info("[U1U] " + prefix + "统计类别不完整，保留旧值。");
            }
            CableReserveInfo reserve = FillFeature.ResolveCableReserve(previousRecord,
                machine, statistics, options.CableOwnerReserveMillimeters,
                options.CableReserve);

            try
            {
                // Batch U1U must use the current CAD table as the cable fallback before planning.
                FillFeature.ResolveUpdateCableFromExistingTable(ctx, selection, machine,
                    workbook.Catalog, readTransaction, definitions);
            }
            catch (Exception ex)
            {
                errors.Add(prefix + ex.Message);
                return;
            }

            FlexibleConduitCableMap.ApplyTo(machine);
            TableGenerationOutput tablePlan = FillTableModule.Plan(machine,
                workbook.Catalog, statistics, options.Planning, options.AutoFill);
            FillFeature.WriteCablePlanNote(ctx, machine, tablePlan);
            List<TableFillRow> plannedRows = FillUpdateRowMerger.Merge(
                readTransaction, selection, tablePlan.CopyDefaultRows(), statistics);
            bool deviceHasOutlet;
            bool deviceOutletStateKnown;
            string deviceState;
            DownstreamState downstreamEquipment;
            string downstreamState;
            int downstreamBlocks;
            try
            {
                CadDynamicBlockStateService.TryReadDeviceHasOutlet(readTransaction,
                    selection.DeviceBlockIds, out deviceHasOutlet,
                    out deviceOutletStateKnown, out deviceState);
                CadDynamicBlockStateService.TryReadDownstreamState(readTransaction,
                    selection.DeviceBlockIds, out downstreamEquipment,
                    out _, out downstreamState, out downstreamBlocks);
            }
            catch (Exception ex)
            {
                errors.Add(prefix + ex.Message);
                return;
            }
            TableFillRow deviceOutlet = TableFillPlanner.BuildOutletRow(machine.Detail,
                workbook.Catalog);
            // Batch U1U follows the same socket contract as the single-frame path:
            // preserve every existing outlet row (especially its quantity), add one
            // only when the device is a socket and the table has none, and remove
            // stale 8.x rows when the device is back to equipment.
            List<TableFillRow> existingOutlets = CadExistingOutletReader.Read(readTransaction,
                selection.TableIds, options.StartRow, options.ClearRows);
            List<TableFillRow> socketRows = deviceOutletStateKnown
                ? DeviceOutletPolicy.ApplyForUpdate(plannedRows, deviceHasOutlet,
                    deviceOutlet, existingOutlets)
                : UpdateOutletPolicy.PreserveExisting(plannedRows, existingOutlets);
            socketRows = DownstreamStatePolicy.Apply(socketRows, workbook.Catalog,
                options.DownstreamSocketEnabled, options.DownstreamPanelEnabled,
                downstreamEquipment, options.DownstreamSocketAmps,
                options.DownstreamPanelAmps);
            if (downstreamBlocks > 0)
            {
                string downstreamNote = "Downstream 状态: " + downstreamState
                    + "；附加清单: " + FillFeature.DescribeDownstreamExtra(
                        downstreamEquipment, options);
                notes.Add(downstreamNote);
                Log.Info("[U1U] " + prefix + downstreamNote);
            }
            tablePlan = new TableGenerationOutput(socketRows,
                tablePlan.DefaultCableMeters, tablePlan.AutoFill);
            FillReviewData review = tablePlan.CreateReview(machine, options.Planning,
                originalCableModel);
            if (reserve != null) FillFeature.InitializeReserveReview(review, reserve);
            FillFeature.SyncCableReserveTotal(reserve, review.CableMeters);
            review.RestoreBusPlugBoxChoice(previousRecord, workbook.Catalog);
            try
            {
                if (!FillFeature.ApplyRuanguanLength(ctx, selection, review,
                        workbook.Catalog, options.Planning, true, readTransaction))
                {
                    errors.Add(prefix + "Ruanguan 软管长度无法确认。");
                    return;
                }
            }
            catch (Exception ex)
            {
                errors.Add(prefix + ex.Message);
                return;
            }
            bool hoseWasMissingBeforeCableChoice = review.FlexibleConduitItem() == null;
            List<FillReviewItem> unresolved = review.Items.Where(item =>
                item.RequiresCatalogConfirmation).ToList();
            FillReviewItem unresolvedCable = unresolved.FirstOrDefault(item =>
                item.Category == TableFillCategory.Cable);
            string cableRequestKey = "";
            if (unresolvedCable != null)
            {
                if (workbook.Catalog.Cables.Count == 0)
                {
                    errors.Add(prefix + "固定清单没有可选择的电缆型号。");
                    return;
                }
                cableRequestKey = region.Handle + ":cable";
                catalogRequests.Add(new BatchCatalogRequest(cableRequestKey,
                    machine.MachineId, machine.CircuitName, "电缆", review.BoqCableModel,
                    workbook.Catalog.Cables));
            }

            string busPlugBoxRequestKey = "";
            if (unresolved.Any(item => item.Category == TableFillCategory.BusPlugBox))
            {
                busPlugBoxRequestKey = region.Handle + ":busPlugBox";
                var request = new BatchCatalogRequest(busPlugBoxRequestKey,
                    machine.MachineId, machine.CircuitName, "母线插接箱", machine.Detail,
                    workbook.Catalog.BusPlugBoxes);
                if (request.Candidates.Count == 0)
                {
                    errors.Add(prefix + "固定清单没有可选择的母线插接箱规格。");
                    return;
                }
                catalogRequests.Add(request);
            }

            if (!string.IsNullOrEmpty(markerNote))
            {
                notes.Add(markerNote);
                Log.Info("[U1U] " + prefix + markerNote + "。");
            }
            plans.Add(new Plan
            {
                Region = region,
                Selection = selection,
                Machine = review.Machine,
                Summation = summation,
                Statistics = statistics,
                Review = review,
                Reserve = reserve,
                Rows = review.SelectedRows(),
                CableRequestKey = cableRequestKey,
                BusPlugBoxRequestKey = busPlugBoxRequestKey,
                HoseWasMissingBeforeCableChoice = hoseWasMissingBeforeCableChoice,
                DeviceState = deviceState,
                DeviceOutletStateKnown = deviceOutletStateKnown,
                Notes = notes,
                DeviceReserveEraseIds = plan_DeviceReserveEraseIds,
                PreviousRecord = previousRecord,
                ExistingRows = FillRowDiffBuilder.ReadRows(
                    CadSubmissionReader.Read(readTransaction, selection.TableIds)),
                LegacyLastUpdated = FrameLegacyInfoReader.ReadLastUpdatedText(
                    readTransaction, selection),
                // 容量在预检读事务里算一次；以前每个图框在此后还各开一个写事务重算。
                Capacity = FillFeature.ResolveTableWriteCapacity(readTransaction,
                    selection.TableIds, options.StartRow, options.ClearRows)
            });
        }

        /// <summary>批量写入时的界面刷新：事务内的改动必须先进图形队列再刷屏，
        /// 否则 UpdateScreen 看不到未提交的修改——大批量时表现为“屏幕死了、
        /// 最后一次性蹦完”。进度文字由 ProgressWindow 展示，命令行不再逐步输出。</summary>
        private static void FlushProgress(CadContext ctx)
        {
            try
            {
                ctx.Db.TransactionManager.QueueForGraphicsFlush();
                ctx.Ed.UpdateScreen();
            }
            catch (System.Exception ex)
            {
                // 进度刷新绝不影响真正的写入。
                Log.Warn("U1U 进度刷新失败: " + ex.Message);
            }
        }

        private static WindowWrapper Owner()
        {
            // AcCoreConsole has no main window; full AutoCAD still receives a modal owner.
            IntPtr handle = AcApplication.MainWindow == null
                ? IntPtr.Zero : AcApplication.MainWindow.Handle;
            return new WindowWrapper(handle);
        }

        private static string PromptValue(string value)
            => string.IsNullOrWhiteSpace(value) ? "（空）" : value.Trim();
    }
}
