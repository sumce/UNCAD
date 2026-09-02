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
            public string DeviceState { get; set; }
            public string CableRequestKey { get; set; }
            public bool HoseWasMissingBeforeCableChoice { get; set; }
            public bool AllowUnmatchedDefaults { get; set; }
        }

        public static void Execute(CadContext ctx, IReadOnlyList<FrameRegionGroup> regions)
        {
            if (regions == null || regions.Count < 2) return;
            FillRuntimeOptions options = FillSettings.Current();
            string path = FillFeature.ResolveMachineWorkbookPath(ctx, options.MachineWorkbookPath);
            if (path == null) return;

            FillWorkbookSnapshot workbook;
            try
            {
                workbook = FillWorkbookSnapshot.Load(path);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[U1U] 读取机台 Excel 失败: " + ex.Message);
                Log.Error("U1U batch read machine excel failed", ex);
                return;
            }
            if (workbook.MachineIds.Count == 0 || workbook.ListItems.Count == 0)
            {
                ctx.Write("\n[U1U] 机台 Excel 或内嵌固定清单没有可用数据。");
                return;
            }

            var plans = new List<Plan>();
            var errors = new List<string>();
            var cableRequests = new List<BatchCableCatalogRequest>();
            foreach (FrameRegionGroup region in regions)
                Preflight(ctx, region, options, workbook, plans, errors, cableRequests);

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

            if (cableRequests.Count > 0)
            {
                using (var picker = new BatchCableCatalogSelectionForm(cableRequests))
                {
                    if (AcApplication.ShowModalDialog(picker) != DialogResult.OK) return;
                    foreach (Plan plan in plans)
                    {
                        if (string.IsNullOrWhiteSpace(plan.CableRequestKey)
                            || !picker.Selections.TryGetValue(plan.CableRequestKey,
                                out ListItem selected)) continue;
                        FillReviewItem cable = plan.Review?.CableItem();
                        if (cable != null)
                            plan.Review.ReplaceWithCatalogItem(cable, selected,
                                workbook.Catalog);
                        if (plan.HoseWasMissingBeforeCableChoice
                            && plan.Review?.FlexibleConduitItem() != null)
                            FillFeature.ApplyRuanguanLength(ctx, plan.Selection,
                                plan.Review, workbook.Catalog, options.Planning);
                    }
                }
            }

            // A merged/deleted material row can legitimately have no fixed-catalog match.
            // Ask once for the whole batch and keep the decision explicit in the plan; do
            // not turn this recoverable situation into a preflight warning/error.
            List<Tuple<Plan, FillReviewItem>> unresolvedDefaults = plans
                .SelectMany(plan => (plan.Review?.Items ?? new List<FillReviewItem>())
                    .Where(item => item.RequiresCatalogConfirmation)
                    .Select(item => Tuple.Create(plan, item)))
                .ToList();
            if (unresolvedDefaults.Count > 0)
            {
                string details = string.Join("、", unresolvedDefaults.Select(entry =>
                    entry.Item2.Name).Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(12));
                if (unresolvedDefaults.Count > 12) details += " 等";
                DialogResult useDefaults = MessageBox.Show(Owner(),
                    "批量更新中有 " + unresolvedDefaults.Count
                        + " 个清单项目未匹配固定清单（可能已在现有表格中合并）。\r\n"
                        + (details.Length > 0 ? "项目：" + details + "\r\n\r\n" : "\r\n")
                        + "是否采用当前默认数据继续更新表格？\r\n"
                        + "选择“否”将取消整批，CAD 和 BOQ 均不修改。",
                    "U1U 批量默认数据", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                if (useDefaults != DialogResult.Yes) return;
                foreach (Plan plan in plans)
                {
                    if (plan.Review == null) continue;
                    plan.AllowUnmatchedDefaults = plan.Review.AcceptUnmatchedDefaults() > 0;
                }
                ctx.Write("\n[U1U] 用户确认采用默认数据更新未匹配项目。未编码默认项不会写入自动 BOQ 材料数量。");
            }

            foreach (Plan plan in plans)
            {
                List<FillReviewItem> unresolved = plan.Review?.Items.Where(item =>
                    item.RequiresCatalogConfirmation).ToList()
                    ?? new List<FillReviewItem>();
                if (unresolved.Count > 0 && !plan.AllowUnmatchedDefaults)
                {
                    errors.Add("机台 " + plan.Machine.MachineId + "；设备 "
                        + plan.Machine.CircuitName + "；图框 " + plan.Region.Handle
                        + "：固定清单仍有未匹配项目。" + string.Join("、",
                            unresolved.Select(item => item.Name)));
                    continue;
                }
                plan.Rows = plan.Review.SelectedRows(plan.AllowUnmatchedDefaults);
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
            try
            {
                AutomaticSubmissionService.ValidateIdentityKeys(plans.Select(plan =>
                    new KeyValuePair<string, string>(plan.Machine.MachineId,
                        plan.Machine.CircuitName)));
                automaticExcelPath = AutomaticSubmissionService.PrepareTargetPath(
                    plans.Select(plan => plan.Machine.MachineId));
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[U1U] BOQ 输出路径不可用，图纸未修改: "
                    + ex.Message);
                return;
            }

            var confirmationRows = plans.Select(plan => new BatchFillConfirmationRow(
                plan.Region.Handle, plan.Machine.MachineId, plan.Machine.CircuitName,
                plan.DeviceState, plan.Rows.Count)).ToList();
            using (var confirmation = new BatchFillConfirmationForm(confirmationRows))
            {
                if (AcApplication.ShowModalDialog(confirmation) != DialogResult.OK) return;
            }

            AutomaticSubmissionWriteResult automaticExcel;
            int tableRows = 0;
            int frameBlocks = 0;
            int attributeValues = 0;
            int migratedBridgeLabels = 0;
            int upstreamConnectionLines = 0;
            bool upstreamConnectionEnabled = Settings.GetBool(
                ConfigKeys.FillUpstreamConnectionEnabled, false);
            using (var outputBatch = new FileBatchRollback(
                AutomaticSubmissionService.TargetPaths(automaticExcelPath,
                    plans.Select(plan => plan.Machine.MachineId))))
            try
            {
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
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

                        FillWriteResult frame = CadBlockAttributeWriter.FillFrame(ctx, transaction,
                            plan.Selection.FrameBlockIds, plan.Machine,
                            options.BridgeInfo, plan.Statistics,
                            plan.Statistics.CableSum <= 0,
                            plan.Statistics.Bridges.Count == 0,
                            plan.Statistics.Conduits.Count == 0);
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
                            plan.Review, CommandIds.FillUpdate);
                        FillWriteResult upstreamInfo = CadBlockAttributeWriter.FillTagged(ctx,
                            transaction, plan.Selection.UpstreamInfoBlockIds,
                            ConnectionBlockFiller.TagUpstreamInfo,
                            ConnectionBlockFiller.UpstreamInfo(plan.Machine), true);
                        FillWriteResult upstreamState = CadDynamicBlockStateService.FillUpstreamState(
                            ctx, transaction, plan.Selection.UpstreamStateBlockIds,
                            plan.Machine.Next);
                        FillWriteResult upstreamConnection = upstreamConnectionEnabled
                            ? UpstreamConnectionLineWriter.Ensure(ctx, transaction,
                                plan.Selection.UpstreamInfoBlockIds,
                                plan.Selection.UpstreamStateBlockIds)
                            : FillWriteResult.Empty;
                        upstreamConnectionLines += upstreamConnection.Blocks;
                        FillWriteResult upstreamAxis = CadBlockAttributeWriter.FillTagged(ctx,
                            transaction, plan.Selection.UpstreamAxisBlockIds,
                            ConnectionBlockFiller.TagUpstreamAxis,
                            ConnectionBlockFiller.UpstreamAxis(plan.Machine), false);
                        FillWriteResult downstreamAxis = CadBlockAttributeWriter.FillTagged(ctx,
                            transaction, plan.Selection.DownstreamAxisBlockIds,
                            ConnectionBlockFiller.TagDownstreamAxis,
                            ConnectionBlockFiller.DownstreamAxis(plan.Machine), false);
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
                    automaticExcel = plans.Any(plan => plan.AllowUnmatchedDefaults)
                        ? AutomaticSubmissionService.WriteBatchWithDefaults(ctx, transaction,
                            automaticExcelPath,
                            regions.Select(region => region.EntityIds.ToArray()), outputBatch)
                        : AutomaticSubmissionService.Write(ctx, transaction,
                            automaticExcelPath,
                            regions.Select(region => region.EntityIds.ToArray()), outputBatch);
                    transaction.Commit();
                    outputBatch.Complete();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("U1U batch write failed", ex);
                ctx.Write("\n[U1U] 批量写入失败，整批已回滚: " + ex.Message);
                MessageBox.Show(Owner(), "批量写入失败，所有图框修改均已回滚。\r\n\r\n" + ex.Message,
                    "U1U", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (upstreamConnectionLines > 0)
                ctx.Write("\n[U1U] upstream_info 与 upstream 连接线已创建或更新 "
                    + upstreamConnectionLines + " 段。");
            SelectionService.ClearPickFirst(ctx);
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

        private static void Preflight(CadContext ctx, FrameRegionGroup region,
            FillRuntimeOptions options, FillWorkbookSnapshot workbook,
            List<Plan> plans, List<string> errors,
            List<BatchCableCatalogRequest> cableRequests)
        {
            string prefix = "图框 " + region.Handle + "：";
            FillSelection selection = FillSelectionCollector.Split(ctx, region.EntityIds.ToArray());
            if (selection.FrameBlockIds.Length != 1)
            {
                errors.Add(prefix + "没有读取到唯一图框块。");
                return;
            }

            // Resolve identity before reporting table shape so each structural error carries the
            // machine and device when available. Any failed preflight still returns before planning.
            bool validTableShape = selection.TableIds.Length == 1;
            bool validIdentity = FillSelectionCollector.TryReadExistingIdentity(ctx, selection,
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

            SummationOutput summation = FillStatisticsModule.Execute(ctx,
                selection.TextIds, options.MmPerGrid);
            CableStatResult statistics = summation.Statistics;
            if (statistics.CableSum <= 0 && statistics.Bridges.Count == 0
                && statistics.Conduits.Count == 0)
                ctx.Write("\n[U1U] " + prefix + "未读取到新的长度文字，将保留现有数量。");

            try
            {
                // Batch U1U must use the current CAD table as the cable fallback before planning.
                FillFeature.ResolveUpdateCableFromExistingTable(ctx, selection, machine, workbook.Catalog);
            }
            catch (Exception ex)
            {
                errors.Add(prefix + ex.Message);
                return;
            }

            FlexibleConduitCableMap.ApplyTo(machine);
            TableGenerationOutput tablePlan = FillTableModule.Plan(machine,
                workbook.Catalog, statistics, options.Planning);
            List<TableFillRow> plannedRows = FillFeature.MergeExistingRowsForBatchUpdate(
                ctx, selection, tablePlan.CopyDefaultRows(), statistics);
            bool deviceHasOutlet;
            string deviceState;
            try
            {
                deviceHasOutlet = CadDynamicBlockStateService.ReadDeviceHasOutlet(ctx,
                    selection.DeviceBlockIds, out deviceState);
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
            List<TableFillRow> existingOutlets = CadExistingOutletReader.Read(ctx,
                selection.TableIds, options.StartRow, options.ClearRows);
            tablePlan = new TableGenerationOutput(DeviceOutletPolicy.ApplyForUpdate(
                plannedRows, deviceHasOutlet, deviceOutlet, existingOutlets),
                tablePlan.DefaultCableMeters);
            FillReviewData review = tablePlan.CreateReview(machine, options.Planning);
            FillFeature.ApplyRuanguanLength(ctx, selection, review, workbook.Catalog,
                options.Planning);
            bool hoseWasMissingBeforeCableChoice = review.FlexibleConduitItem() == null;
            List<FillReviewItem> unresolved = review.Items.Where(item =>
                item.RequiresCatalogConfirmation).ToList();
            FillReviewItem unresolvedCable = unresolved.FirstOrDefault(item =>
                item.Category == TableFillCategory.Cable);
            if (unresolvedCable != null)
            {
                if (workbook.Catalog.Cables.Count == 0)
                {
                    errors.Add(prefix + "固定清单没有可选择的电缆型号。");
                    return;
                }
                string requestKey = region.Handle + ":cable";
                cableRequests.Add(new BatchCableCatalogRequest(requestKey,
                    machine.MachineId, machine.CircuitName, review.BoqCableModel,
                    workbook.Catalog.Cables));
                plans.Add(new Plan
                {
                    Region = region,
                    Selection = selection,
                    Machine = machine,
                    Summation = summation,
                    Statistics = statistics,
                    Review = review,
                    CableRequestKey = requestKey,
                    HoseWasMissingBeforeCableChoice = hoseWasMissingBeforeCableChoice,
                    DeviceState = deviceState
                });
                return;
            }

            plans.Add(new Plan
            {
                Region = region,
                Selection = selection,
                Machine = machine,
                Summation = summation,
                Statistics = statistics,
                Review = review,
                Rows = review.SelectedRows(),
                HoseWasMissingBeforeCableChoice = hoseWasMissingBeforeCableChoice,
                DeviceState = deviceState
            });
        }

        private static WindowWrapper Owner()
        {
            // AcCoreConsole has no main window; full AutoCAD still receives a modal owner.
            IntPtr handle = AcApplication.MainWindow == null
                ? IntPtr.Zero : AcApplication.MainWindow.Handle;
            return new WindowWrapper(handle);
        }
    }
}
