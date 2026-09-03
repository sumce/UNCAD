using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Submission;
using UNCAD.Core.Text;
using UNCAD.Features.Submit;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.Fill
{
    /// <summary>Coordinates selection, Excel lookup, preview, and specialized CAD writers.</summary>
    [Feature("fill", "Excel 填充清单表",
        Commands = CommandIds.FillFeatureCommands,
        Description = "动态填充清单，并可读取已填充身份按当前统计重新生成")]
    public class FillFeature : CommandBase
    {
        [CommandMethod(CommandIds.Fill, CommandFlags.UsePickSet)]
        public void UncadFill() => Run();

        [CommandMethod(CommandIds.FillUpdate, CommandFlags.UsePickSet)]
        public void UncadFillUpdate() => Run(true);

        protected override void Execute(CadContext ctx) => ExecuteCore(ctx, false);

        protected override void Execute(CadContext ctx, object state)
            => ExecuteCore(ctx, state is bool updateMode && updateMode);

        private static void ExecuteCore(CadContext ctx, bool updateMode)
        {
            ProductMetadata.EnsureCommandAllowed(updateMode ? CommandIds.FillUpdate : CommandIds.Fill);
            // 阶段1：检测写入目标和统计文字。此阶段只读图纸，不产生任何修改。
            FillSelection selection = FillSelectionCollector.Collect(ctx);
            if (selection.IsEmpty)
            {
                ctx.Write("\n[U1F] 未找到清单表/图框块/设备块/上下游信息块/统计文字。");
                return;
            }
            if (updateMode)
            {
                FrameRegionCollection regions = FrameRegionCollector.Collect(ctx, selection.SourceIds);
                if (regions.SelectedFrameCount > 1)
                {
                    if (regions.Errors.Count > 0)
                    {
                        foreach (string error in regions.Errors)
                            ctx.Write("\n[U1U] 图框分区失败: " + error);
                        return;
                    }
                    // Only multi-frame update replaces the explicit selection with spatial groups.
                    // A single frame retains the established interactive update behavior.
                    BatchFillUpdateCoordinator.Execute(ctx, regions.Groups);
                    return;
                }
            }
            if (!selection.HasWriteTargets)
            {
                ctx.Write("\n[U1F] 已选到统计文字，但没有清单表或可写入块；本次未修改图纸。");
                return;
            }
            if (selection.TableIds.Length > 1 || selection.FrameBlockIds.Length > 1)
            {
                ctx.Write("\n[U1F] 一次只允许一个清单表和一个目标图框块，避免批量误写。");
                return;
            }

            // 阶段2：冻结配置并按实际标注求和，后续预览和写入复用同一结果。
            FillRuntimeOptions options = FillSettings.Current();
            SummationOutput summation = FillStatisticsModule.Execute(ctx,
                selection.TextIds, options.MmPerGrid, selection.StatisticsScopeComplete);
            CableStatResult statistics = summation.Statistics;
            if (updateMode) WriteMeasurementState(ctx, statistics);

            string path = ResolveMachineWorkbookPath(ctx, options.MachineWorkbookPath);
            if (path == null) return;

            // 阶段3：加载机台/盘柜数据（用户唯一的外部 Excel）和内嵌固定清单。
            // 清单随插件版本固化，不再检查外部清单文件；资源缺失属于程序集缺陷。
            FillWorkbookSnapshot workbook;
            try
            {
                workbook = FillWorkbookSnapshot.Load(path);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[U1F] 读取机台 Excel 失败: " + ex.Message);
                Log.Error("U1F read machine excel failed", ex);
                return;
            }
            ctx.Write("\n[U1F] 机台数据 "
                + (workbook.MachineCacheHit ? "已使用缓存" : "已重新加载")
                + "：" + workbook.MachineSourcePath
                + "；内嵌固定清单 " + workbook.CatalogItemCount + " 项。");

            List<string> machineIds = workbook.MachineIds;
            List<ListItem> listItems = workbook.ListItems;
            BoqCatalogIndex catalog = workbook.Catalog;
            if (machineIds.Count == 0)
            {
                ctx.Write("\n[U1F] Excel 中无机台ID数据。");
                return;
            }
            if (listItems.Count == 0)
            {
                // 固定清单为空代表插件资源或发布包损坏，不能让用户确认生成无编码项目。
                MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "插件内没有固定清单数据，已停止本次填充。请重新安装完整版本。",
                    "U1F 固定清单异常", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string bridgeInfo = options.BridgeInfo;
            int startRow = options.StartRow;
            int clearRowCount = options.ClearRows;
            double textHeight = options.TextHeight;

            MachineRow picked;
            if (updateMode)
            {
                if (!FillSelectionCollector.TryReadExistingIdentity(ctx, selection,
                    out ExistingFillIdentity identity, out string identityError))
                {
                    ctx.Write("\n[U1U] " + identityError);
                    return;
                }
                picked = ExistingFillIdentityResolver.MatchMachine(identity,
                    workbook.FindRows(identity.MachineId), out string matchError);
                if (picked == null)
                {
                    ctx.Write("\n[U1U] " + matchError);
                    return;
                }
                ResolveUpdateCableFromExistingTable(ctx, selection, picked, catalog);
                ctx.Write("\n[U1U] 已自动读取: "
                    + picked.MachineId + " " + picked.CircuitName);
            }
            else
            {
                Func<MachineRow, string> preview = selected => BuildPreview(ctx, selected, catalog,
                    statistics, selection, options);
                using (var form = new MachinePickerForm(machineIds, workbook.FindRows, preview))
                {
                    if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form)
                        != DialogResult.OK) return;
                    picked = form.Selected;
                }
                ctx.Write("\n[U1F] 已选择: "
                    + picked.MachineId + " " + picked.CircuitName);
            }

            // 阶段4：根据机台、盘柜和实际求和结果生成有序默认清单。
            FlexibleConduitCableMap.ApplyTo(picked);
            TableGenerationOutput tablePlan = FillTableModule.Plan(
                picked, catalog, statistics, options.Planning);
            List<TableFillRow> plannedRows = tablePlan.CopyDefaultRows();
            if (updateMode)
                plannedRows = FillUpdateRowMerger.Merge(ctx, selection, plannedRows, statistics);
            // U1U treats existing CAD outlet rows as authoritative for quantity and
            // material.  U1F starts from a clean generated list instead.
            List<TableFillRow> existingOutlets = updateMode
                ? CadExistingOutletReader.Read(ctx, selection.TableIds,
                    startRow, clearRowCount)
                : new List<TableFillRow>();
            CadDynamicBlockStateService.TryReadDeviceHasOutlet(ctx,
                selection.DeviceBlockIds, out bool deviceHasOutlet,
                out bool deviceOutletStateKnown, out string deviceState);
            TableFillRow deviceOutlet = TableFillPlanner.BuildOutletRow(picked.Detail, catalog);
            List<TableFillRow> socketAdjustedRows;
            if (updateMode && !deviceOutletStateKnown)
            {
                // Missing/legacy Device blocks do not prove that an outlet was removed.
                // Preserve the current CAD outlet rows and let the user decide in review.
                socketAdjustedRows = UpdateOutletPolicy.PreserveExisting(
                    plannedRows, existingOutlets);
            }
            else
            {
                socketAdjustedRows = updateMode
                    ? DeviceOutletPolicy.ApplyForUpdate(plannedRows,
                        deviceHasOutlet, deviceOutlet, existingOutlets)
                    : DeviceOutletPolicy.Apply(plannedRows,
                        deviceHasOutlet, deviceOutlet);
            }
            tablePlan = new TableGenerationOutput(socketAdjustedRows,
                tablePlan.DefaultCableMeters);
            ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                + "] Device 状态: " + deviceState + "；插座清单: "
                + (deviceHasOutlet ? "输出 " + (deviceOutlet.Code.Length > 0
                    ? deviceOutlet.Code : "未匹配") : "不输出"));
            string defaultCableMeters = tablePlan.DefaultCableMeters;
            FillReviewData review = tablePlan.CreateReview(picked, options.Planning);
            ResolveMissingCableCatalog(review, catalog);
            // Resolve a replacement before reading Ruanguan so an initially unknown
            // cable can still create the correctly mapped hose row.
            ApplyRuanguanLength(ctx, selection, review, catalog, options.Planning);
            bool hoseWasMissingBeforeReview = review.FlexibleConduitItem() == null;
            // 阶段5：用户修改、增加、删除或取消清单项；异常型号必须明确确认。
            string updateMachineId = picked.MachineId;
            string updateDeviceName = picked.CircuitName;
            using (var form = new FillReviewForm(review, catalog, options.Planning,
                updateMode))
            {
                if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form)
                    != DialogResult.OK) return;
                review = form.Data;
            }
            if (updateMode && (!string.Equals(updateMachineId,
                    review.Machine?.MachineId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(updateDeviceName, review.Machine?.CircuitName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "U1U 不允许在更新清单时修改机台 ID 或设备名称。请使用 U1F 为新身份建立清单。",
                    "U1U 身份锁定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (updateMode)
            {
                MachineRow validated = ExistingFillIdentityResolver.MatchMachine(
                    new ExistingFillIdentity
                    {
                        MachineId = review.Machine?.MachineId,
                        DeviceName = review.Machine?.CircuitName
                    }, workbook.FindRows(review.Machine?.MachineId), out string identityError);
                if (validated == null)
                {
                    ctx.Write("\n[U1U] " + identityError);
                    return;
                }
            }
            // If the user resolved an unknown cable inside the review dialog, the hose row
            // did not exist when the first Ruanguan read ran. Apply the block length once
            // after that replacement, while still honoring an explicit user deletion made
            // in the review itself.
            if (hoseWasMissingBeforeReview && review.FlexibleConduitItem() != null)
                ApplyRuanguanLength(ctx, selection, review, catalog, options.Planning);
            // Machine.Cable remains the source-device value. A BOQ replacement updates only
            // the reviewed cable row, so frame/block attributes never receive a catalog substitute.
            picked = review.Machine;
            List<TableFillRow> tableRows = review.SelectedRows();
            // Device visibility is the final socket authority; DETAIL selects 8.2/8.3 when possible.
            deviceOutlet = TableFillPlanner.BuildOutletRow(picked.Detail, catalog);
            if (deviceHasOutlet && !deviceOutlet.CatalogMatched)
            {
                TableFillRow reviewedOutlet = tableRows.FirstOrDefault(row =>
                    UpdateOutletPolicy.IsOutlet(row) && row.CatalogMatched);
                if (reviewedOutlet == null)
                {
                    MessageBox.Show(new WindowWrapper(
                            Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                        "Device 当前为插座状态，但 DETAIL 电流无法匹配插座 8.2/8.3。"
                            + "请在清单确认中选择对应插座型号。",
                        "U1F 插座型号未匹配", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
                deviceOutlet = reviewedOutlet;
            }
            if (updateMode && !deviceOutletStateKnown)
            {
                tableRows = UpdateOutletPolicy.PreserveExisting(tableRows, existingOutlets);
            }
            else
            {
                tableRows = updateMode
                    ? DeviceOutletPolicy.ApplyForUpdate(tableRows, deviceHasOutlet,
                        deviceOutlet, existingOutlets)
                    : DeviceOutletPolicy.Apply(tableRows, deviceHasOutlet, deviceOutlet);
            }
            // 记录用户确认后的真实输出，而不是默认规划行，便于直接核对取消勾选是否生效。
            Log.Info("U1F confirmed BOQ rows: " + string.Join(" | ",
                tableRows.ConvertAll(row => row.Code + ":" + row.Name)));
            if (tableRows.Count == 0
                && MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "当前没有要生成的清单项。继续将只清空模板数据区，不写入新清单。",
                    "U1F 空清单确认", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                    != DialogResult.Yes)
                return;
            int tableCapacity = ResolveTableWriteCapacity(ctx, selection.TableIds,
                startRow, clearRowCount);
            if (tableRows.Count > tableCapacity)
            {
                MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "清单共 " + tableRows.Count + " 项，所选表格实际可写范围只有 "
                        + tableCapacity + " 行。请删除部分清单项，或在 U1SET 中调整起始行和清除行数。",
                    "U1F 表格容量不足", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            string reviewedCableMeters = review.CableMeters ?? "";
            if (!string.Equals(defaultCableMeters, reviewedCableMeters,
                StringComparison.OrdinalIgnoreCase))
                ApplyCableLengthOverride(statistics, reviewedCableMeters);

            ConfigPrinter.Print(ctx, updateMode ? CommandIds.FillUpdate : CommandIds.Fill,
                ("清单行数", tableRows.Count.ToString()),
                ("表格容量", tableCapacity.ToString()),
                ("顺序", string.Join(" → ", tableRows.ConvertAll(row => row.Name))));

            string automaticExcelPath;
            try
            {
                automaticExcelPath = AutomaticSubmissionService.PrepareTargetPath(
                    new[] { picked.MachineId });
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] BOQ 输出路径不可用，图纸未修改: " + ex.Message);
                return;
            }

            int filled;
            int migratedBridgeLabels;
            XFrameMigrationResult xframeMigration;
            FillWriteResult frameResult, deviceResult, ruanguanResult, frameInfoResult,
                upstreamInfoResult;
            FillWriteResult upstreamStateResult, upstreamAxisResult, downstreamAxisResult;
            int deviceColorBlocks, upstreamColorBlocks;
            short deviceColorIndex = FillColorSettings.DeviceColorIndex();
            short upstreamColorIndex = FillColorSettings.UpstreamColorIndex();
            AutomaticSubmissionWriteResult automaticExcel;
            // 阶段6：先清除模板数据区，再按连续顺序写入清单和块属性；
            // 文件快照覆盖到 CAD Commit，任一失败都不留下单边更新。
            using (var outputBatch = new FileBatchRollback(
                AutomaticSubmissionService.TargetPaths(automaticExcelPath,
                    new[] { picked.MachineId })))
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                xframeMigration = XFrameMigrationService.Migrate(ctx, transaction,
                    new[] { selection });
                migratedBridgeLabels = BridgeLabelMigrationWriter.Migrate(transaction,
                    selection.TextIds, options.MmPerGrid);
                filled = FillTableModule.Write(ctx, transaction, selection.TableIds,
                    startRow, clearRowCount, tableRows, textHeight);
                if (filled < 0) return;
                CadDrawingInfoTableWriter.Write(transaction, selection.DrawingInfoTableIds,
                    picked, DateTime.Now);
                frameResult = CadBlockAttributeWriter.FillFrame(ctx, transaction,
                    selection.FrameBlockIds, picked,
                    updateMode && statistics.BridgeState == MeasurementState.ConfirmedEmpty
                        ? "" : bridgeInfo,
                    statistics,
                    updateMode && statistics.CableState == MeasurementState.Unknown,
                    updateMode && statistics.BridgeState == MeasurementState.Unknown,
                    updateMode && statistics.ConduitState == MeasurementState.Unknown);
                deviceResult = CadBlockAttributeWriter.FillDeviceName(ctx, transaction,
                    selection.DeviceBlockIds, DeviceBlockFiller.BuildValue(picked));
                ruanguanResult = RuanguanBlockWriter.FillModelAndLength(ctx, transaction,
                    selection.RuanguanBlockIds, picked.Dia);
                frameInfoResult = FrameInfoJsonBlockWriter.FillOrMigrate(ctx, transaction,
                    selection.FrameBlockIds, selection.FrameInfoJsonBlockIds, picked, review,
                    updateMode ? CommandIds.FillUpdate : CommandIds.Fill);
                upstreamInfoResult = CadBlockAttributeWriter.FillTagged(ctx, transaction,
                    selection.UpstreamInfoBlockIds, ConnectionBlockFiller.TagUpstreamInfo,
                    ConnectionBlockFiller.UpstreamInfo(picked), true);
                upstreamStateResult = CadDynamicBlockStateService.FillUpstreamState(ctx,
                    transaction, selection.UpstreamStateBlockIds, picked.Next);
                upstreamAxisResult = CadBlockAttributeWriter.FillTagged(ctx, transaction,
                    selection.UpstreamAxisBlockIds, ConnectionBlockFiller.TagUpstreamAxis,
                    ConnectionBlockFiller.UpstreamAxis(picked), false);
                downstreamAxisResult = CadBlockAttributeWriter.FillTagged(ctx, transaction,
                    selection.DownstreamAxisBlockIds, ConnectionBlockFiller.TagDownstreamAxis,
                    ConnectionBlockFiller.DownstreamAxis(picked), false);
                deviceColorBlocks = CadBlockColorWriter.Apply(transaction,
                    selection.DeviceBlockIds.Concat(selection.DownstreamAxisBlockIds)
                        .Concat(selection.DeviceColorBlockIds), deviceColorIndex);
                upstreamColorBlocks = CadBlockColorWriter.Apply(transaction,
                    selection.UpstreamStateBlockIds
                        .Concat(selection.UpstreamInfoBlockIds)
                        .Concat(selection.UpstreamAxisBlockIds)
                        .Concat(selection.UpstreamColorBlockIds),
                    upstreamColorIndex);
                // The reader uses this same transaction, so BOQ failure aborts all CAD writes.
                automaticExcel = AutomaticSubmissionService.Write(ctx, transaction,
                    automaticExcelPath, new[] { selection.SourceIds }, outputBatch);
                transaction.Commit();
                outputBatch.Complete();
            }

            SelectionService.ClearPickFirst(ctx);
            if (xframeMigration.Frames > 0 || xframeMigration.BoqTables > 0
                || xframeMigration.DrawingInfoTables > 0)
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 图框已升级：xframe " + xframeMigration.Frames
                    + " 个，BOQ 表替换 " + xframeMigration.BoqTables
                    + " 个，制图信息表替换 " + xframeMigration.DrawingInfoTables + " 个。");
            ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                + "] 完成：表格写入 " + filled + " 行；块属性更新 "
                + frameResult.Blocks + " 个块共 " + frameResult.Values + " 项；统计电缆 "
                + TextFormatter.FormatNum(statistics.CableSum) + "M，桥架规格 "
                + statistics.Bridges.Count + " 项，线管规格 " + statistics.Conduits.Count
                + " 项（SUM-STAT源 " + summation.SourceLineCount + " 行，命中 "
                + summation.TotalMatchCount + " 行）；设备动态块更新 "
                + deviceResult.Blocks + " 个；Ruanguan 更新 "
                + ruanguanResult.Blocks + " 个块共 " + ruanguanResult.Values + " 项；"
                + "frameinfo_json " + frameInfoResult.Blocks + " 个块共 "
                + frameInfoResult.Values + " 项；上游信息 "
                + upstreamInfoResult.Blocks + " 个，upstream 状态 "
                + upstreamStateResult.Blocks + " 个，上游轴位 " + upstreamAxisResult.Blocks
                + " 个，下游轴位 " + downstreamAxisResult.Blocks + " 个。");
            if (migratedBridgeLabels > 0)
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 已将 " + migratedBridgeLabels
                    + " 个旧桥架格数标注转换为毫米标注（"
                    + TextFormatter.FormatNum(options.MmPerGrid) + " mm/格）。");
            if (deviceColorBlocks + upstreamColorBlocks > 0)
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 颜色已校正：设备端 " + deviceColorBlocks
                    + " 个块，上游端 " + upstreamColorBlocks + " 个块。");
            ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                + "] BOQ 已自动输出：处理记录 " + automaticExcel.AddedCount
                + " 条，覆盖文件 " + automaticExcel.ReplacedCount
                + " 个，材料明细 " + automaticExcel.MaterialCount + " 项；文件 " + automaticExcel.FilePath);
        }

        internal static int ResolveTableWriteCapacity(CadContext ctx, ObjectId[] tableIds,
            int startRow, int configuredRows)
        {
            // Multiple selected tables are written together, so the smallest resolved
            // capacity is the only value that can guarantee the atomic write will fit.
            int capacity = int.MaxValue;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in tableIds ?? Array.Empty<ObjectId>())
                {
                    Table table = transaction.GetObject(id, OpenMode.ForRead, true) as Table;
                    if (table == null) continue;
                    int zeroBasedStart = CadTableFillWriter.ResolveWriteStartRow(table, startRow);
                    int available = zeroBasedStart < 0
                        ? 0 : table.Rows.Count - zeroBasedStart;
                    capacity = Math.Min(capacity,
                        TableClearPolicy.ResolveRows(available, configuredRows));
                }
            }
            return capacity == int.MaxValue ? 0 : capacity;
        }

        internal static void ApplyRuanguanLength(CadContext ctx, FillSelection selection,
            FillReviewData review)
            => ApplyRuanguanLength(ctx, selection, review, null,
                FillPlanningOptions.Default);

        internal static void ApplyRuanguanLength(CadContext ctx, FillSelection selection,
            FillReviewData review, BoqCatalogIndex catalog, FillPlanningOptions options)
        {
            if (review == null) return;
            options = options ?? FillPlanningOptions.Default;
            string meters = FillSelectionCollector.ReadRuanguanLengthMeters(ctx,
                selection?.RuanguanBlockIds);
            if (meters.Length == 0)
            {
                // Ruanguan is the only source of hose length; absent/invalid values mean no hose row.
                FillReviewItem existing = review.FlexibleConduitItem();
                if (existing != null) review.RemoveItem(existing);
                if (selection?.RuanguanBlockIds?.Length > 0)
                    ctx.Write("\n[U1F/U1U] 已找到 Ruanguan 块，但没有有效软管长度，"
                        + "已不加入软管清单。");
                return;
            }
            FillReviewItem flexible = review.FlexibleConduitItem();
            if (flexible == null && !string.IsNullOrWhiteSpace(review.Machine.Dia))
                flexible = review.SetFlexibleConduitDiameter(review.Machine.Dia,
                    catalog, options);
            if (flexible == null)
            {
                ctx.Write("\n[U1F/U1U] 已读取 Ruanguan 长度，但电缆没有可映射的软管直径，未加入软管清单。");
                return;
            }
            if (!flexible.CatalogMatched)
            {
                ctx.Write("\n[U1F/U1U] 软管直径未匹配固定清单，未加入软管清单。");
                review.RemoveItem(flexible);
                return;
            }
            flexible.Quantity = meters;
            ctx.Write("\n[U1F/U1U] Ruanguan 软管长度: " + meters + "M。");
        }

        internal static void ResolveUpdateCableFromExistingTable(CadContext ctx,
            FillSelection selection, MachineRow picked, BoqCatalogIndex catalog)
        {
            if (picked == null) return;
            catalog = catalog ?? new BoqCatalogIndex(null);
            string originalModel = (picked.Cable ?? "").Trim();
            FrameInfoJsonRecord frameInfo = FrameInfoJsonBlockWriter.Read(ctx,
                selection?.FrameInfoJsonBlockIds);
            var sourceIds = new List<ObjectId>();
            sourceIds.AddRange(selection?.FrameBlockIds ?? Array.Empty<ObjectId>());
            sourceIds.AddRange(selection?.TableIds ?? Array.Empty<ObjectId>());
            SubmissionSourceData tableSource = CadSubmissionReader.Read(ctx,
                sourceIds.Distinct().ToArray());

            // The current table is authoritative for procurement. If the row was merged or
            // deleted, frameinfo_json preserves the user's previously confirmed substitute.
            string tableModel = SubmissionRecordExtractor.ExtractTableCableModel(tableSource);
            ListItem tableModelMatch = catalog.FindCable(tableModel);
            if (tableModelMatch != null)
            {
                picked.Cable = tableModelMatch.Alias;
                ctx.Write("\n[U1U] 已按现有清单电缆型号匹配固定清单型号“"
                    + tableModelMatch.Alias + "”（编号 " + tableModelMatch.Code + "）。");
                return;
            }
            ListItem frameInfoMatch = catalog.FindCable(frameInfo?.BoqCableModel);
            if (frameInfoMatch != null)
            {
                picked.Cable = frameInfoMatch.Alias;
                ctx.Write("\n[U1U] 已按 frameinfo_json 保留的 BOQ 电缆替代型号匹配固定清单型号“"
                    + frameInfoMatch.Alias + "”（编号 " + frameInfoMatch.Code + "）。");
                return;
            }

            // The current frame attribute is the next source. It reflects a cable changed
            // in CAD after the workbook was created, while the workbook row can be stale.
            string frameModel = "";
            if (tableSource.Attributes.TryGetValue(FrameBlockFiller.TagCable,
                    out List<string> frameValues))
                frameModel = frameValues.Select(SubmissionRecordExtractor.ExtractCableModel)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
            ListItem frameMatch = catalog.FindCable(frameModel);
            if (frameMatch != null)
            {
                picked.Cable = frameMatch.Alias;
                return;
            }

            string tableFeature = SubmissionRecordExtractor.ExtractTableCableFeature(tableSource);
            ListItem featureMatch = catalog.FindCableByFeature(tableFeature);
            if (featureMatch != null)
            {
                // The table's project feature identifies the fixed catalog row; use its canonical alias
                // for planning instead of trying to compare the rendered model text again.
                picked.Cable = featureMatch.Alias;
                ctx.Write("\n[U1U] 原始电缆型号“" + originalModel
                    + "”无法匹配，已按现有清单项目特征使用固定清单型号“"
                    + featureMatch.Alias + "”（编号 " + featureMatch.Code + "）。");
                return;
            }

            // The workbook value is still useful when the drawing carries no cable
            // attribute at all. Leave an unmatched review row for the single-frame picker
            // or the batch picker instead of aborting before the user can choose.
            ListItem workbookMatch = catalog.FindCable(originalModel);
            if (workbookMatch != null)
            {
                picked.Cable = workbookMatch.Alias;
                return;
            }

            string tableValue = tableModel.Length > 0 ? tableModel
                : tableFeature.Length > 0 ? tableFeature : "未读取到";
            ctx.Write("\n[U1U] 电缆型号暂未匹配固定清单：原始型号“" + originalModel
                + "”；现有清单“" + tableValue + "”。将在确认窗口中选择固定清单型号。");
        }

        private static void ResolveMissingCableCatalog(FillReviewData review,
            BoqCatalogIndex catalog)
        {
            FillAnomaly anomaly = FillAnomalyDetector.MissingCable(review);
            if (anomaly == null) return;
            Log.Warn("U1F " + anomaly.Code + ": " + anomaly.Subject);
            var owner = new WindowWrapper(
                Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle);
            DialogResult replace = MessageBox.Show(owner,
                "固定清单找不到电缆型号：" + anomaly.Subject
                    + "\r\n\r\n是否从固定清单选择替代型号？"
                    + "\r\n替代型号只用于本次清单，图框和块属性中的设备原型号保持不变。",
                "U1F 电缆型号异常", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (replace != DialogResult.Yes) return;
            if (catalog.Cables.Count == 0)
            {
                MessageBox.Show(owner, "固定清单中没有可选择的电缆型号。",
                    "U1F 电缆型号异常", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            using (var picker = new CableCatalogSelectionForm(
                catalog.Cables, review.BoqCableModel))
            {
                if (Autodesk.AutoCAD.ApplicationServices.Application
                    .ShowModalDialog(picker) != DialogResult.OK
                    || picker.SelectedItem == null) return;
                review.SetCableModel(picker.SelectedItem.Alias ?? picker.SelectedItem.Spec,
                    catalog);
            }
        }

        private static void ApplyCableLengthOverride(CableStatResult statistics, string value)
        {
            string text = (value ?? "").Trim();
            double meters;
            bool parsed = double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out meters)
                || double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture, out meters);
            statistics.CableFormatted.Clear();
            statistics.CableSum = parsed && meters > 0 ? meters : 0;
            if (statistics.CableSum > 0)
            {
                statistics.CableFormatted.Add(TextFormatter.FormatNum(statistics.CableSum));
                statistics.CableState = MeasurementState.Measured;
            }
            else
            {
                statistics.CableState = MeasurementState.ConfirmedEmpty;
            }
        }

        private static void WriteMeasurementState(CadContext ctx, CableStatResult statistics)
        {
            string StateText(MeasurementState state)
            {
                switch (state)
                {
                    case MeasurementState.Measured: return "已测量";
                    case MeasurementState.ConfirmedEmpty: return "确认清空";
                    default: return "未完整选择，保留旧值";
                }
            }
            ctx.Write("\n[SUM-STAT/求和统计] 电缆 " + StateText(statistics.CableState)
                + "；桥架 " + StateText(statistics.BridgeState)
                + "；线管 " + StateText(statistics.ConduitState) + "。");
        }

        internal static string ResolveMachineWorkbookPath(
            CadContext ctx, string configuredPath)
        {
            string path = (configuredPath ?? "").Trim();
            if (MachineWorkbookSource.IsRemote(path))
            {
                if (MachineWorkbookSource.TryGetCachedPath(path, out string cachedPath))
                {
                    ctx.Write("\n[U1F/U1U] 使用 U1SET 手动刷新后的网络 Excel 缓存: " + cachedPath);
                    return cachedPath;
                }
                ctx.Write("\n[U1F/U1U] 网络 Excel 尚未缓存，请在 U1SET 中点击“刷新”。");
                Log.Warn("网络机台 Excel 尚未缓存，U1F/U1U 未访问网络。源: " + path);
                return null;
            }
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                ctx.Write("\n[U1F] 使用上次 Excel: " + path
                    + "（U1SET → Excel 填充 可修改）");
                return path;
            }

            using (var dialog = new OpenFileDialog
            {
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                Title = "选择机台数据 Excel"
            })
            {
                if (dialog.ShowDialog(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle))
                    != DialogResult.OK) return null;
                path = dialog.FileName;
            }
            Settings.Set(ConfigKeys.FillExcelPath, path);
            ctx.Write("\n[U1F] 已记住 Excel: " + path);
            return path;
        }

        private static string BuildPreview(CadContext ctx, MachineRow row, BoqCatalogIndex catalog,
            CableStatResult statistics, FillSelection selection, FillRuntimeOptions options)
        {
            // Keep the picker preview on the same cable-derived diameter path as final planning.
            FlexibleConduitCableMap.ApplyTo(row);
            var preview = new StringBuilder();
            preview.AppendLine("▼ 回路详情");
            preview.AppendLine("  机台/设备：" + row.MachineId + " / " + row.CircuitName);
            preview.AppendLine("  盘柜类型："
                + (string.IsNullOrWhiteSpace(row.Next) ? "未指定" : row.Next));
            preview.AppendLine("  FR：" + row.Fr);
            preview.AppendLine("  配电详情：" + row.Detail);
            preview.AppendLine("  下游轴位：" + row.DownstreamAxis + " ｜ 上游轴位："
                + row.UpstreamAxis);

            List<TableFillRow> plannedRows = TableGenerationModule.Plan(
                new TableGenerationRequest(row, catalog, statistics, options.Planning))
                .CopyDefaultRows();
            CadDynamicBlockStateService.TryReadDeviceHasOutlet(ctx,
                selection?.DeviceBlockIds, out bool previewHasOutlet,
                out _, out string previewDeviceState);
            plannedRows = DeviceOutletPolicy.Apply(plannedRows, previewHasOutlet,
                TableFillPlanner.BuildOutletRow(row.Detail, catalog));
            TableFillRow previewHose = plannedRows.FirstOrDefault(item =>
                item.Category == TableFillCategory.FlexibleConduit);
            if (previewHose != null)
            {
                string hoseMeters = FillSelectionCollector.ReadRuanguanLengthMeters(
                    ctx, selection?.RuanguanBlockIds);
                if (hoseMeters.Length == 0)
                    plannedRows.Remove(previewHose);
                else
                    previewHose.Quantity = hoseMeters;
            }
            preview.AppendLine("▼ 表格写入（覆盖，从 No." + options.StartRow + " 行开始，共 "
                + plannedRows.Count + " 项，文字高度 "
                + TextFormatter.FormatNum(options.TextHeight) + "）");
            for (int i = 0; i < plannedRows.Count; i++)
            {
                TableFillRow planned = plannedRows[i];
                preview.AppendLine("  No." + (options.StartRow + i) + " " + planned.Name
                    + " ｜ " + planned.Unit + " "
                    + (planned.Quantity.Length > 0 ? planned.Quantity : "数量待定")
                    + " ｜ 编号 " + (planned.Code.Length > 0 ? planned.Code : "未匹配"));
            }
            if (selection.FrameBlockIds.Length > 0)
            {
                preview.AppendLine("▼ 图框块属性（" + selection.FrameBlockIds.Length + " 个块）");
                foreach (var value in FrameBlockFiller.BuildValues(
                    row, options.BridgeInfo, statistics))
                    preview.AppendLine("  " + value.Key + " = " + value.Value);
            }
            if (selection.DeviceBlockIds.Length > 0)
                preview.AppendLine("▼ 设备动态块（" + selection.DeviceBlockIds.Length
                    + " 个）：DEVICENAME = " + DeviceBlockFiller.BuildValue(row)
                    + " ｜ 状态 " + previewDeviceState + " ｜ 插座 "
                    + (previewHasOutlet ? "输出" : "不输出"));
            if (selection.UpstreamStateBlockIds.Length > 0)
                preview.AppendLine("▼ upstream 状态："
                    + DynamicBlockStatePolicy.UpstreamVisibilityState(row.Next));
            if (selection.UpstreamInfoBlockIds.Length > 0)
                preview.AppendLine("▼ 上游信息块：" + row.Fr + " / " + row.Detail);
            if (selection.UpstreamAxisBlockIds.Length > 0)
                preview.AppendLine("▼ 上游轴位块：US = " + ConnectionBlockFiller.UpstreamAxis(row));
            if (selection.DownstreamAxisBlockIds.Length > 0)
                preview.AppendLine("▼ 下游轴位块：DS = " + ConnectionBlockFiller.DownstreamAxis(row));
            if (selection.TextIds.Length > 0)
                preview.AppendLine("▼ 统计源文字（" + selection.TextIds.Length + " 个，保持原内容）");
            return preview.ToString();
        }
    }
}
