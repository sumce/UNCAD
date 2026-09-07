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
            public Core.Fill.FrameInfoJsonRecord PreviousRecord { get; set; }
            public List<TableFillRow> ExistingRows { get; set; }
            public string LegacyLastUpdated { get; set; } = "";
            public string DeviceState { get; set; }
            public bool DeviceOutletStateKnown { get; set; }
            public string CableRequestKey { get; set; }
            public string BusPlugBoxRequestKey { get; set; }
            public bool HoseWasMissingBeforeCableChoice { get; set; }
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
            ctx.Write("\n[U1U] 正在批量预检 " + regions.Count + " 个图框，请稍候...");
            using (Transaction readTransaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(readTransaction);
                foreach (FrameRegionGroup region in regions)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    Preflight(ctx, readTransaction, region, options, workbook, plans,
                        errors, catalogRequests, definitions, statisticsSettings);
                    clock.Stop();
                    Log.Info("U1U 批量预检 图框 " + region.Handle + ": "
                        + clock.ElapsedMilliseconds + " ms");
                }
                readTransaction.Commit();
            }
            ctx.Write("\n[U1U] 批量预检完成，开始生成更新计划...");

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
                using (var picker = new BatchCatalogSelectionForm(catalogRequests))
                {
                    if (AcApplication.ShowModalDialog(picker) != DialogResult.OK) return;
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
                int capacity = FillFeature.ResolveTableWriteCapacity(ctx,
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

            string automaticExcelPath;
            IReadOnlyList<BoqWorkbookRevision> automaticExcelRevisions;
            try
            {
                AutomaticSubmissionService.ValidateIdentityKeys(plans.Select(plan =>
                    new KeyValuePair<string, string>(plan.Machine.MachineId,
                        plan.Machine.CircuitName)));
                automaticExcelPath = AutomaticSubmissionService.PrepareTargetPath(
                    plans.Select(plan => plan.Machine.MachineId));
                automaticExcelRevisions = AutomaticSubmissionService.CaptureTargetRevisions(
                    automaticExcelPath, plans.Select(plan => plan.Machine.MachineId));
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[U1U] BOQ 输出路径不可用，图纸未修改: "
                    + ex.Message);
                return;
            }

            // U1U 强化:批量确认前展示每框的上次更新时间/用户与清单并排对比。
            // 没有任何更新信息的图框不进入对比窗;全部无记录时直接跳过弹窗。
            var compareItems = plans
                .Select(plan => FillFeature.BuildCompareItem(
                    plan.Machine, plan.Rows, plan.ExistingRows, plan.PreviousRecord,
                    plan.Region.Handle, plan.LegacyLastUpdated))
                .Where(item => item.HasHistory)
                .ToList();
            if (compareItems.Count == 0)
                ctx.Write("\n[U1U] 所选图框均无上次更新记录，跳过对比。");
            else
            using (var compareForm = new FillUpdateCompareForm(compareItems))
            {
                if (AcApplication.ShowModalDialog(compareForm) != DialogResult.OK)
                {
                    ctx.Write("\n[U1U] 已取消批量更新，图纸未修改。");
                    return;
                }
            }
            var confirmationRows = plans.Select(plan => new BatchFillConfirmationRow(
                plan.Region.Handle, plan.Machine.MachineId, plan.Machine.CircuitName,
                plan.DeviceState, plan.Rows.Count)).ToList();
            using (var confirmation = new BatchFillConfirmationForm(confirmationRows))
            {
                if (AcApplication.ShowModalDialog(confirmation) != DialogResult.OK) return;
            }

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

            AutomaticSubmissionWriteResult automaticExcel;
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
                    plans.Select(plan => plan.Machine.MachineId))))
            try
            {
                // Close the gap between the post-confirmation check and batch snapshot.
                // BeginWrite performs the final check after the CAD work is prepared.
                AutomaticSubmissionService.ValidateTargetRevisions(
                    automaticExcelRevisions);
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
                    foreach (Plan plan in plans)
                    {
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
                            plan.Statistics.BridgeState == MeasurementState.ConfirmedEmpty
                                ? "" : options.BridgeInfo,
                            plan.Statistics,
                            plan.Statistics.CableState == MeasurementState.Unknown,
                            plan.Statistics.BridgeState == MeasurementState.Unknown,
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
                            plan.Review, CommandIds.FillUpdate, metadataIndex);
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
                    }
                    // Read the modified entities through the same transaction. If BOQ output
                    // fails, disposing this transaction rolls back the whole CAD batch.
                    automaticExcel = AutomaticSubmissionService.Write(ctx, transaction,
                        automaticExcelPath, plans.Select(plan => plan.Selection.SourceIds),
                        outputBatch);
                    transaction.Commit();
                    outputBatch.Complete();
                }
            }
            catch (System.Exception ex)
            {
                System.Exception reported = ex;
                string rollbackStatus = "所有图框和 BOQ 修改均已回滚。";
                try { outputBatch.Rollback(); }
                catch (System.Exception rollbackError)
                {
                    reported = new AggregateException(
                        "CAD 图框已回滚，但部分 BOQ 文件无法安全恢复。",
                        ex, rollbackError);
                    rollbackStatus = "CAD 图框已回滚，但部分 BOQ 文件无法安全恢复；"
                        + "已保留当前文件，请先检查错误再继续更新。";
                }
                Log.Error("U1U batch write failed", reported);
                ctx.Write("\n[U1U] 批量写入失败；" + rollbackStatus + " "
                    + reported.Message);
                MessageBox.Show(Owner(), "批量写入失败；" + rollbackStatus
                    + "\r\n\r\n" + reported.Message,
                    "U1U", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SelectionService.ClearPickFirst(ctx);
            if (xframeMigration.Frames > 0 || xframeMigration.BoqTables > 0
                || xframeMigration.DrawingInfoTables > 0)
                ctx.Write("\n[U1U] 图框已升级：xframe " + xframeMigration.Frames
                    + " 个，BOQ 表替换 " + xframeMigration.BoqTables
                    + " 个，制图信息表替换 " + xframeMigration.DrawingInfoTables + " 个。");
            if (deviceColorBlocks + upstreamColorBlocks > 0)
                ctx.Write("\n[U1U] 颜色已校正：设备端 " + deviceColorBlocks
                    + " 个块，上游端 " + upstreamColorBlocks + " 个块。");
            ctx.Write("\n[U1U] 批量完成：图框 " + plans.Count
                + " 个，表格写入 " + tableRows + " 行，块 " + frameBlocks
                + " 个共更新 " + attributeValues + " 项属性。"
                + (migratedBridgeLabels > 0 ? " 旧桥架标注转换 "
                    + migratedBridgeLabels + " 个（"
                    + UNCAD.Core.Text.TextFormatter.FormatNum(options.MmPerGrid)
                    + " mm/格）。" : "")
                + " BOQ 文件处理 " + automaticExcel.AddedCount + " 条，覆盖 "
                + automaticExcel.ReplacedCount + " 个文件；路径 " + automaticExcel.FilePath);

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

            SummationOutput summation = FillStatisticsModule.Execute(ctx, readTransaction,
                selection.TextIds, options.MmPerGrid, selection.StatisticsScopeComplete,
                statisticsSettings);
            CableStatResult statistics = summation.Statistics;
            if (statistics.CableState == MeasurementState.Unknown
                || statistics.BridgeState == MeasurementState.Unknown
                || statistics.ConduitState == MeasurementState.Unknown)
                ctx.Write("\n[U1U] " + prefix
                    + "存在未启用或不完整的统计类别，仅这些类别保留旧值。");

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
                workbook.Catalog, statistics, options.Planning);
            FillFeature.WriteCablePlanNote(ctx, machine, tablePlan);
            List<TableFillRow> plannedRows = FillUpdateRowMerger.Merge(
                readTransaction, selection, tablePlan.CopyDefaultRows(), statistics);
            bool deviceHasOutlet;
            bool deviceOutletStateKnown;
            string deviceState;
            try
            {
                CadDynamicBlockStateService.TryReadDeviceHasOutlet(readTransaction,
                    selection.DeviceBlockIds, out deviceHasOutlet,
                    out deviceOutletStateKnown, out deviceState);
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
            tablePlan = new TableGenerationOutput(socketRows,
                tablePlan.DefaultCableMeters);
            FillReviewData review = tablePlan.CreateReview(machine, options.Planning,
                originalCableModel);
            FrameInfoJsonRecord previousRecord = FrameInfoJsonBlockWriter.Read(readTransaction,
                selection.FrameInfoJsonBlockIds);
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

            plans.Add(new Plan
            {
                Region = region,
                Selection = selection,
                Machine = review.Machine,
                Summation = summation,
                Statistics = statistics,
                Review = review,
                Rows = review.SelectedRows(),
                CableRequestKey = cableRequestKey,
                BusPlugBoxRequestKey = busPlugBoxRequestKey,
                HoseWasMissingBeforeCableChoice = hoseWasMissingBeforeCableChoice,
                DeviceState = deviceState,
                DeviceOutletStateKnown = deviceOutletStateKnown,
                PreviousRecord = previousRecord,
                ExistingRows = FillRowDiffBuilder.ReadRows(
                    CadSubmissionReader.Read(readTransaction, selection.TableIds)),
                LegacyLastUpdated = FrameLegacyInfoReader.ReadLastUpdatedText(
                    readTransaction, selection)
            });
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
