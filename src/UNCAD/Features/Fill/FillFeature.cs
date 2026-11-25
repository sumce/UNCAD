using System;
using System.Collections.Generic;
using System.Linq;
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
using UNCAD.Cad.Frame;
using UNCAD.Features.Shared;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// U1F/U1U 命令编排：按 docs/DEVELOPMENT.md 的阶段顺序（检测→求和→规划→
    /// 评审→清除/写入→完成）串联各阶段服务，并亲自持有阶段6/7 的 CAD 事务与
    /// BOQ 批次回滚边界。具体阶段逻辑在 FillStatisticsStage / FillWorkbookStage /
    /// FillMachinePickStage / FillCoverageGate / FillPlanningStage /
    /// FillReviewStage / FillRuanguanStage 与 Core 的 CableReserveReview；
    /// 本类保留被 BatchFillUpdateCoordinator、CircuitCoverageGuard、
    /// PdfExportFeature 和测试引用的兼容薄壳。
    /// </summary>
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
            var stageClock = System.Diagnostics.Stopwatch.StartNew();
            // 阶段1：检测写入目标和统计文字。此阶段只读图纸，不产生任何修改。
            FillSelection selection = FillSelectionCollector.Collect(ctx);
            stageClock.Stop();
            Log.Info((updateMode ? "U1U" : "U1F") + " 选集收集: "
                + stageClock.ElapsedMilliseconds + " ms");
            if (selection.IsEmpty)
            {
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 未找到清单表/图框块/设备块/上下游信息块/统计文字。");
                return;
            }
            if (updateMode && BatchFillUpdateCoordinator
                .TryHandleMultiFrameSelection(ctx, selection)) return;
            if (!selection.HasWriteTargets)
            {
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 已选到统计文字，但没有清单表或可写入块；本次未修改图纸。");
                return;
            }
            if (selection.TableIds.Length > 1 || selection.FrameBlockIds.Length > 1)
            {
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 一次只允许一个清单表和一个目标图框块，避免批量误写。");
                return;
            }

            // 阶段2：冻结配置并按实际标注求和，后续预览和写入复用同一结果。
            // 2026-11-05：上一轮预留恢复与 D-043 标记决策先于统计执行——吸收的
            // 横排毫米标（如 "3000mm"）同时命中电缆长度判据（^数字0mm$），不从
            // 统计排除就会双算（统计一次、设备内预留又一次，总长虚高一份）。
            FillRuntimeOptions options = FillSettings.Current();
            FillDrawingStyleSettings drawingStyle = FillDrawingStyleSettings.Capture();
            FrameInfoJsonRecord frameInfoPrevious = FillReserveStage.Resolve(ctx,
                selection, options, updateMode,
                out List<ObjectId> deviceReserveEraseIds,
                out List<ObjectId> markerExcludedIds,
                out bool copiedFrameDetected,
                out double? copiedDeviceReserveHint,
                out UNCAD.Core.Fill.ExistingFillIdentity frameIdentity);
            stageClock.Restart();
            SummationOutput summation = FillStatisticsStage.Execute(ctx, selection,
                options, updateMode, markerExcludedIds);
            stageClock.Stop();
            Log.Info("[PERF] " + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                + " 统计求和: " + stageClock.ElapsedMilliseconds + " ms");
            CableStatResult statistics = summation.Statistics;

            string path = ResolveMachineWorkbookPath(ctx, options.MachineWorkbookPath);
            if (path == null) return;

            // 阶段3：加载机台/盘柜数据（用户唯一的外部 Excel）和内嵌固定清单。
            // 清单随插件版本固化，不再检查外部清单文件；资源缺失属于程序集缺陷。
            FillWorkbookSnapshot workbook = FillWorkbookStage.Load(ctx, path, updateMode);
            if (workbook == null) return;
            BoqCatalogIndex catalog = workbook.Catalog;

            int startRow = options.StartRow;
            int clearRowCount = options.ClearRows;
            double textHeight = options.TextHeight;

            MachineRow picked;
            string originalCableModel = "";
            if (updateMode)
            {
                if (!FillSelectionCollector.TryReadExistingIdentity(ctx, selection,
                    out ExistingFillIdentity identity, out string identityError))
                {
                    ctx.Write("\n[U1U] " + identityError);
                    return;
                }
                // D-056：键/唯一同名直接命中；同名歧义弹窗指认（CircuitAmbiguityPrompt）。
                picked = CircuitAmbiguityPrompt.MatchOrPrompt(identity,
                    workbook.FindRows(identity.MachineId), out string matchError);
                if (picked == null)
                {
                    ctx.Write("\n[U1U] " + matchError);
                    return;
                }
                originalCableModel = (picked.Cable ?? "").Trim();
                FillMachinePickStage.ResolveUpdateCableFromExistingTable(ctx, selection,
                    picked, catalog);
                ctx.Write("\n[U1U] 已自动读取: "
                    + picked.MachineId + " " + picked.CircuitName);
            }
            else
            {
                picked = FillMachinePickStage.PickNewMachine(ctx, workbook, selection,
                    statistics, options);
                if (picked == null) return;
                ctx.Write("\n[U1F] 已选择: "
                    + picked.MachineId + " " + picked.CircuitName);
            }

            // 3.0.0.17 机台身份守卫（现场报告：U1F 会把复制过来之前的预留米数
            // 算进新机台）：旧 JSON 记录与 CABLE_INFO 建议值属于「图框原来的
            // 那台机」。U1F 改选别的机台（复制改号/换机台重定义）时整体丢弃，
            // 不进清单、不进公式、不进新 JSON；同机台（重跑/同机台第二只框/
            // 跨文件搬场）不受影响。U1U 的机台来自图框自身身份，恒等于原机台，
            // 守卫天然空转。
            frameInfoPrevious = UNCAD.Core.Fill.FrameRedefineGuard.Apply(frameIdentity,
                frameInfoPrevious, picked, ref copiedDeviceReserveHint,
                out string redefineNote);
            if (redefineNote.Length > 0)
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] " + redefineNote);

            // 路由体检：连通性报告、图层矫正计划和线路识别；U1U 另建 3D 预览（阶段类内，U1F 不建）。
            FillRouteInspection routeInspection =
                FillRouteInspectionStage.InspectSelectionAndReport(ctx, selection,
                    picked, updateMode, drawingStyle);

            // 整机回路校验（U1SET 可关闭）。D-034：部分回路的 BOQ 会丢掉未选
            // 回路的工程量，宁可少生成也不能写错数。D-044（2026-11-03）：U1F 与
            // U1U 同口径——缺回路时图纸照常填写（每框只依赖自己的统计与机台行），
            // 只跳过 BOQ；补全后执行 U1S 补清单。
            List<CircuitCoverageResult> coverageFailures = FillCoverageGate.EvaluateCircuitCoverage(
                ctx, workbook, updateMode ? CommandIds.FillUpdate : CommandIds.Fill,
                new[]
                {
                    // D-056：选中的行带回路身份键参与整机校验。
                    UNCAD.Core.Fill.CircuitSelection.Of(picked)
                });
            bool boqBlockedByCoverage = coverageFailures.Count > 0;
            bool boqForcedByUser = false;
            if (boqBlockedByCoverage && updateMode
                && FillCoverageGate.OfferForceBoq(ctx, CommandIds.FillUpdate,
                    coverageFailures).Contains(picked.MachineId.Trim()))
            {
                // D-048：用户确认后为缺回路机台强制生成 BOQ（仅含已画回路）。
                boqForcedByUser = true;
                boqBlockedByCoverage = false;
                ctx.Write("\n[" + CommandIds.FillUpdate + "] 机台 "
                    + picked.MachineId + " 回路不全，BOQ 已按你的确认强制生成"
                    + "（仅含已画回路）；补画后执行 " + CommandIds.Submit
                    + " 重新生成完整清单。");
            }
            if (boqBlockedByCoverage)
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 机台 " + picked.MachineId + " 回路不全：本次照常填写图框数据、"
                    + "不生成 BOQ；全部回路画完后执行 " + CommandIds.Submit + " 生成清单。"
                    + "若该机台不需要绘制全部回路，可在 U1SET 关闭「整机回路校验」。");
            ctx.Operation?.Machine(picked.MachineId, picked.CircuitName);

            string automaticExcelPath = null;
            IReadOnlyList<BoqWorkbookRevision> automaticExcelRevisions = null;
            // U1F（新建）与 U1U（更新）共用同一套电缆预留：盘内预留按上游类型，
            // 设备内预留从 frameinfo_json 复用；没有记录时在清单确认窗里填写。
            CableReserveInfo cableReserve = CableReserveReview.ResolveCableReserve(
                frameInfoPrevious, picked, statistics,
                Settings.GetDouble(ConfigKeys.FillCableOwnerReserve,
                    CableReservePolicy.DefaultOwnerReserveMillimeters),
                options.CableReserve);
            // D-052-B：复制图框的设备内预留建议值预填进清单确认窗（可见、可改，
            // 用户确认后才落盘——与 3.0.0.8 的“不静默继承”不冲突）。
            if (copiedDeviceReserveHint.HasValue && copiedDeviceReserveHint.Value > 0d
                && cableReserve != null && cableReserve.DeviceReserveMillimeters <= 0d)
                ApplyDeviceReserve(statistics, cableReserve, copiedDeviceReserveHint.Value);
            // 缺回路的 U1U 跳过 BOQ：不准备输出路径，也就不会登记批次文件。
            if (updateMode && !boqBlockedByCoverage)
            {
                try
                {
                    automaticExcelPath = AutomaticSubmissionService.PrepareTargetPath(
                        new[] { picked.MachineId });
                    automaticExcelRevisions = AutomaticSubmissionService.CaptureTargetRevisions(
                        automaticExcelPath, new[] { picked.MachineId });
                }
                catch (System.Exception ex)
                {
                    ctx.Write("\n[" + CommandIds.FillUpdate
                        + "] BOQ 输出路径不可用，图纸未修改: " + ex.Message);
                    return;
                }
            }

            // 阶段4：根据机台、盘柜和实际求和结果生成有序默认清单。
            FillPlanResult plan = FillPlanningStage.Build(ctx, selection, picked, catalog,
                statistics, options, updateMode, frameInfoPrevious, cableReserve,
                originalCableModel);
            // Resolve a replacement before reading Ruanguan so an initially unknown
            // cable can still create the correctly mapped hose row.
            if (!FillRuanguanStage.Apply(ctx, selection, plan.Review, catalog,
                    options.Planning, updateMode)) return;
            bool hoseWasMissingBeforeReview = plan.Review.FlexibleConduitItem() == null;

            // 阶段5：用户修改、增加、删除或取消清单项；异常型号必须明确确认。
            string updateMachineId = picked.MachineId;
            string updateDeviceName = picked.CircuitName;
            Log.Info("U1F 审阅窗前清单: " + Describe(plan.Review));
            if (!FillReviewStage.ShowReview(plan.Review, catalog, options.Planning,
                    updateMode, out FillReviewData review, routeInspection)) return;
            if (cableReserve != null)
                CableReserveReview.ApplyReviewReserve(statistics, cableReserve, review);
            Log.Info("U1F 审阅窗后清单: " + Describe(review));
            if (!FillReviewStage.ValidateIdentityAfterReview(ctx, updateMode,
                    updateMachineId, updateDeviceName, review, workbook)) return;
            // If the user resolved an unknown cable inside the review dialog, the hose row
            // did not exist when the first Ruanguan read ran. Apply the block length once
            // after that replacement, while still honoring an explicit user deletion made
            // in the review itself.
            if (hoseWasMissingBeforeReview && review.FlexibleConduitItem() != null
                && !FillRuanguanStage.Apply(ctx, selection, review, catalog,
                    options.Planning, updateMode)) return;
            // Machine.Cable remains the source-device value. A BOQ replacement updates only
            // the reviewed cable row, so frame/block attributes never receive a catalog substitute.
            picked = review.Machine;
            List<TableFillRow> tableRows = review.SelectedRows();
            // Device visibility is the final socket authority; DETAIL selects 8.2/8.3 when possible.
            tableRows = FillPlanningStage.ApplyReviewedOutlets(ctx, tableRows, picked,
                plan, catalog, updateMode);
            if (tableRows == null) return;
            // 记录用户确认后的真实输出，而不是默认规划行，便于直接核对取消勾选是否生效。
            Log.Info("U1F confirmed BOQ rows: " + string.Join(" | ",
                tableRows.ConvertAll(row => row.Code + ":" + row.Name)));
            if (!FillReviewStage.ConfirmRowsAndCapacity(ctx, tableRows, selection,
                    options, out int tableCapacity)) return;
            string reviewedCableMeters = review.CableMeters ?? "";
            if (!string.Equals(plan.DefaultCableMeters, reviewedCableMeters,
                StringComparison.OrdinalIgnoreCase)
                && !CableReserveReview.ApplyCableLengthOverride(statistics,
                    reviewedCableMeters))
                ctx.Write("\n[U1F/U1U] 电缆米数 \"" + reviewedCableMeters
                    + "\" 无法识别，已保留图上实测值。");
            if (cableReserve != null)
                CableReserveReview.SyncCableReserveTotal(cableReserve, reviewedCableMeters);

            // U1U 强化：写表前展示上次更新时间/用户与字段级变化对比。
            // 必须放在清单确认窗之后、用确认后的最终行构建——用户在确认窗里
            // 改设备内预留或电缆米数之后，先弹的对比就成了假数据；字段变化
            // 也必须用同一份最终 review 预演写入结果。
            // 2026-11-04：传入 cableReserve 驱动电缆长度卡片（实测/盘内箱内/
            // 设备内/业主分解）；内容无变化时不弹窗，直接更新。
            if (updateMode && !ShowUpdateCompare(ctx, selection, picked, tableRows,
                cableReserve, review)) return;

            ConfigPrinter.Print(ctx, updateMode ? CommandIds.FillUpdate : CommandIds.Fill,
                ("清单行数", tableRows.Count.ToString()),
                ("表格容量", tableCapacity.ToString()),
                ("顺序", string.Join(" → ", tableRows.ConvertAll(row => row.Name))));

            try
            {
                if (!updateMode && !boqBlockedByCoverage)
                {
                    automaticExcelPath = AutomaticSubmissionService.PrepareTargetPath(
                        new[] { picked.MachineId });
                    automaticExcelRevisions = AutomaticSubmissionService.CaptureTargetRevisions(
                        automaticExcelPath, new[] { picked.MachineId });
                }
                AutomaticSubmissionService.ValidateTargetRevisions(
                    automaticExcelRevisions);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] BOQ 文件已变化，图纸未修改: " + ex.Message);
                return;
            }

            int filled;
            int layerGovernorMoved = 0;
            var drawingStyleCorrection = new FillDrawingSettingsCorrectionResult();
            XFrameMigrationResult xframeMigration;
            FillWriteResult frameResult, deviceResult, ruanguanResult, frameInfoResult,
                upstreamInfoResult;
            FillWriteResult upstreamStateResult, upstreamAxisResult, downstreamAxisResult;
            int deviceColorBlocks, upstreamColorBlocks;
            short deviceColorIndex = FillColorSettings.DeviceColorIndex();
            short upstreamColorIndex = FillColorSettings.UpstreamColorIndex();
            string command = updateMode ? CommandIds.FillUpdate : CommandIds.Fill;
            AutomaticSubmissionWriteResult automaticExcel = null;
            // 阶段6：CAD 提交点——先清除模板数据区，再按连续顺序写入清单和块属性。
            // BOQ xlsx 不在这里写：按 docs/DEVELOPMENT.md，Excel 输出在 CAD 提交之后、
            // 读取真实 CAD 结果再写（见下面阶段7）。
            using (var outputBatch = new FileBatchRollback(
                boqBlockedByCoverage
                    ? new string[0]
                    : AutomaticSubmissionService.TargetPaths(automaticExcelPath,
                        new[] { picked.MachineId })))
            {
            // Recheck while the batch snapshot is held. BeginWrite then guards the
            // remaining CAD-processing window against another external save.
            AutomaticSubmissionService.ValidateTargetRevisions(
                automaticExcelRevisions);
            try
            {
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    xframeMigration = XFrameMigrationService.Migrate(ctx, transaction,
                        new[] { selection });
                    // 3.3.0（D-054 特性①）：图层矫正进既有写事务（一次 U 连同填充一起回退）。
                    layerGovernorMoved = LayerGovernorService.Apply(ctx, transaction, routeInspection.LayerPlan);
                    drawingStyleCorrection =
                        FillRouteInspectionStage.CorrectDrawingSettings(ctx,
                            transaction, routeInspection, drawingStyle,
                            correctCableLabelPlacement:
                                drawingStyle.CorrectCableLabelPlacement,
                            correctCableTrayConduitStyles: false);
                    // D-043：删除已吸收的老版设备预留横排标注（吸收判定在读阶段）。
                    DeviceReserveMarkerReader.EraseAbsorbed(transaction,
                        deviceReserveEraseIds.ToArray(),
                        updateMode ? CommandIds.FillUpdate : CommandIds.Fill);
                    int migratedCapacity = FillPlanningStage.ResolveWriteCapacity(
                        transaction, selection.TableIds, startRow, clearRowCount);
                    if (tableRows.Count > migratedCapacity)
                        throw new InvalidOperationException("图框迁移后的新版清单表容量只有 "
                            + migratedCapacity + " 行，当前清单需要 " + tableRows.Count
                            + " 行；本次已回滚，请删除清单项后重试。");
                    filled = FillTableModule.Write(ctx, transaction, selection.TableIds,
                        startRow, clearRowCount, tableRows, textHeight);
                    // 3.2.0.6（审计修复）：写表失败不再静默返回——此前单框只无声
                    // return（事务 Dispose 回滚，用户只看到一行表格日志、不知道
                    // 整体已回滚），批量同条件却抛异常。统一为抛出+点名回滚，
                    // 与批量链同口径（CommandBase 打印"错误: "）。
                    if (filled < 0)
                        throw new InvalidOperationException(
                            "清单表写入失败（表格列数不足或格式不兼容），"
                            + "本次图纸修改已整体回滚；请检查清单表是否被改动后重试。");
                    // D-057：制图信息表六列按 U1SET 模板写（留空的列不动图框里的内容）；
                    // (U1F创建时间)/(U1U更新时间) 与写进 frameinfo_json 的时间同源。
                    CadDrawingInfoTableWriter.Write(transaction, selection.DrawingInfoTableIds,
                        picked, DateTime.UtcNow, options.DrawingInfo, frameInfoPrevious,
                        command);
                    // 2026-11-06（用户要求 U1F 定义要清干净）：复制来的图框先把
                    // 源框遗留的 CABLE_INFO/CONDUIT_INFO/FRAMEINFO 显式清空，再写
                    // 新机台数据——同一事务，整体提交或整体回滚。
                    // 3.0.0.14 修正顺序：清空必须先于 FillFrame。旧代码清空排在
                    // FillFrame 之后，把本次刚写入的 CABLE_INFO/CONDUIT_INFO 一并
                    // 抹掉——复制框的电缆信息永远为空（用户报告：左下角电缆信息
                    // 直接不出现）。
                    // 3.2.0.4：U1F = 重新定义——无条件清空图框旧信息属性再写新
                    // 数据（旧 JSON 记录已在 FillReserveStage 清掉，这里清图上
                    // 可见残留）；U1U 保持只在复制框（copiedFrameDetected）时清。
                    if (!updateMode || copiedFrameDetected)
                        CadBlockAttributeWriter.ClearStaleInfo(transaction,
                            selection.FrameBlockIds);
                    // 3.0.0.16：复制框不走“保留旧值”——旧值刚被清空，“保留”保住
                    // 的是空串（CABLE_INFO 停留空，与 U1F 写型号行不一致）。
                    // 复制框一律按 U1F 口径现算现写；普通框的 U1U 保留语义不变。
                    frameResult = CadBlockAttributeWriter.FillFrame(ctx, transaction,
                        selection.FrameBlockIds, picked,
                        statistics,
                        !copiedFrameDetected
                            && updateMode && statistics.CableState == MeasurementState.Unknown,
                        !copiedFrameDetected
                            && updateMode && statistics.ConduitState == MeasurementState.Unknown,
                        !copiedFrameDetected && updateMode
                            && statistics.BridgeState == MeasurementState.Unknown,
                        !copiedFrameDetected && updateMode
                            && statistics.ConduitState == MeasurementState.Unknown);
                    deviceResult = CadBlockAttributeWriter.FillDeviceName(ctx, transaction,
                        selection.DeviceBlockIds, DeviceBlockFiller.BuildValue(picked));
                    ruanguanResult = RuanguanBlockWriter.FillModelAndLength(ctx, transaction,
                        selection.RuanguanBlockIds, picked.Dia);
                    frameInfoResult = FrameInfoJsonBlockWriter.FillOrMigrate(ctx, transaction,
                        selection.FrameBlockIds, selection.FrameInfoJsonBlockIds, picked, review,
                        updateMode ? CommandIds.FillUpdate : CommandIds.Fill, null,
                        cableReserve, frameInfoPrevious,
                        // 3.2.0.5：U1F=重新定义，不继承遗留独立块记录（D-052 附录7）。
                        updateMode);
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
                    // 读表会触发 AutoCAD 的懒重算，提交前先按固定行高回写一次，
                    // 保证提交出去的图纸就是固定行高（D-027）。
                    CadTableFillWriter.FixGeneratedRowHeights(transaction,
                        selection.TableIds, startRow, clearRowCount);
                    transaction.Commit();
                }
            }
            catch (System.Exception writeError)
            {
                // 事务没有提交 → CAD 全部自动回滚；BOQ 文件此时还没有 BeginWrite。
                string status = "图纸和 BOQ 修改均已回滚。";
                try { outputBatch.Rollback(); }
                catch (System.Exception rollbackError)
                {
                    Log.Error(command + " batch rollback failed (CAD already rolled back)",
                        rollbackError);
                }
                Log.Error(command + " write failed", writeError);
                ctx.Write("\n[" + command + "] 写入失败；" + status + " "
                    + writeError.Message);
                return;
            }

            // 阶段7：CAD 已提交。按 docs/DEVELOPMENT.md 的顺序，现在才读取真实 CAD 结果
            // 并写 BOQ —— 提交前写 Excel 会在"替换完成到 Commit 之间"留下新表+旧图的窗口。
            // 代价：这一段失败不再回滚图纸，只回滚本批 BOQ 文件并明确要求重跑 U1S。
            // 缺回路的 U1U 没有登记任何 BOQ 文件，批次直接收尾、图纸保留更新。
            if (boqBlockedByCoverage)
            {
                outputBatch.Complete();
            }
            else
            {
            try
            {
                // 提交后窗口更短，但外部改动的可能性依然存在，再校验一次文件指纹。
                AutomaticSubmissionService.ValidateTargetRevisions(
                    automaticExcelRevisions);
                using (Transaction readTransaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    automaticExcel = AutomaticSubmissionService.Write(ctx, readTransaction,
                        automaticExcelPath, new[] { selection.SourceIds }, outputBatch);
                    readTransaction.Commit();
                }
                // 读清单表会让 AutoCAD 懒重算表几何，提交后必须再按固定行高回写一次，
                // 否则用户图纸里的行高会被重算结果改掉。
                using (Transaction fixTransaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    CadTableFillWriter.FixGeneratedRowHeights(fixTransaction,
                        selection.TableIds, startRow, clearRowCount);
                    fixTransaction.Commit();
                }
                outputBatch.Complete();
                RecordOutputs(ctx, "BOQ", automaticExcel.FilePath);
            }
            catch (System.Exception boqError)
            {
                string status = "本批 BOQ 文件已恢复为修改前的版本。";
                try { outputBatch.Rollback(); }
                catch (System.Exception rollbackError)
                {
                    status = "部分 BOQ 文件无法安全恢复，已保留当前文件。";
                    Log.Error("图纸已提交，但 BOQ 文件回滚不完整。", new AggregateException(
                        "图纸已提交，但 BOQ 文件回滚不完整。", boqError, rollbackError));
                }
                Log.Error(command + " BOQ 输出失败（CAD 已提交）", boqError);
                ctx.Write("\n[" + command + "] 图纸已更新，但 BOQ 输出失败；" + status
                    + " 请重跑 " + CommandIds.Submit + " 重新导出清单：" + boqError.Message);
                return;
            }
            }
            }

            SelectionService.ClearPickFirst(ctx);
            if (xframeMigration.Frames > 0 || xframeMigration.BoqTables > 0
                || xframeMigration.DrawingInfoTables > 0)
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 图框已升级：" + FrameRegionCollector.CurrentFrameName + " "
                    + xframeMigration.Frames
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
            FillRouteInspectionStage.WriteWriteResult(ctx, command, routeInspection, layerGovernorMoved);
            FillRouteInspectionStage.WriteDrawingStyleCorrectionResult(ctx,
                command, drawingStyleCorrection);
            if (deviceColorBlocks + upstreamColorBlocks > 0)
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 颜色已校正：设备端 " + deviceColorBlocks
                    + " 个块，上游端 " + upstreamColorBlocks + " 个块。");
            if (boqBlockedByCoverage)
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 机台 " + picked.MachineId + " 回路不全，本次未生成 BOQ；"
                    + "全部回路画完后执行 " + CommandIds.Submit + " 生成清单。");
            else
            {
                if (boqForcedByUser)
                    ctx.Write("\n[" + CommandIds.FillUpdate + "] 本 BOQ 为强制生成"
                        + "（回路不全，仅含已画回路）；补画后执行 " + CommandIds.Submit
                        + " 重新生成完整清单。");
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] BOQ 已自动输出：处理记录 " + automaticExcel.AddedCount
                    + " 条，覆盖文件 " + automaticExcel.ReplacedCount
                    + " 个，材料明细 " + automaticExcel.MaterialCount + " 项；文件 "
                    + automaticExcel.FilePath);
            }
            if (cableReserve != null
                && (cableReserve.PanelReserveMillimeters > 0d
                    || cableReserve.DeviceReserveMillimeters > 0d))
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] 电缆预留：实测 " + TextFormatter.FormatNum(cableReserve.MeasuredMeters)
                    + " m + 盘内 " + TextFormatter.FormatNum(cableReserve.PanelReserveMillimeters)
                    + " mm + 设备 " + TextFormatter.FormatNum(cableReserve.DeviceReserveMillimeters)
                    + " mm = 总长 " + TextFormatter.FormatNum(cableReserve.TotalMeters) + " m。");
        }

        /// <summary>读 frameinfo_json 里已记录的设备内预留（无记录返回 null）。</summary>
        internal static double? ParseJsonReserve(FrameInfoJsonRecord previous)
        {
            string text = previous?.DeviceReserveMm;
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double value)
                && value >= 0d) return value;
            return null;
        }

        /// <summary>
        /// U1U 写表前的清单对比窗（最终确认闸）：用清单确认窗之后的最终行构建，
        /// 显示的米数/数量与即将写入的内容一致。返回 false 表示用户取消本次更新。
        /// 上次清单直接读 CAD 表格现有行(老图框同样有效);上次更新
        /// 时间/用户来自 frameinfo_json,旧图回退到制图信息表日期。
        /// </summary>
        private static bool ShowUpdateCompare(CadContext ctx, FillSelection selection,
            MachineRow picked, List<TableFillRow> plannedRows,
            CableReserveInfo cableReserve, FillReviewData finalReview)
        {
            try
            {
                // D-040：对比窗用 ReadSources 读全（含图框块 FRAMEINFO）；
                // ReadOwn 带句柄校验——复制图框的源框历史不显示、不误报"上次更新"。
                FrameInfoJsonRecord previous = FrameInfoJsonBlockWriter.ReadOwn(ctx,
                    selection);
                List<TableFillRow> existing = FillRowDiffBuilder.ReadRows(
                    CadSubmissionReader.Read(ctx, selection?.TableIds));
                FillUpdateCompareItem item = FillReviewStage.BuildCompareItem(picked,
                    plannedRows, existing, previous, "",
                    FrameLegacyInfoReader.ReadLastUpdatedText(ctx, selection),
                    cableReserve, finalReview);
                // 2026-11-04 用户要求：内容没有变化就不弹对比窗，不打扰预览。
                if (!item.HasChanges)
                {
                    ctx.Write("\n[" + CommandIds.FillUpdate
                        + "] 本次更新内容与上次一致（清单无变化），直接更新。");
                    return true;
                }
                // 3.0 WPF 窗体（D-038）：结果用 Confirmed 表达。
                var window = new FillUpdateCompareWindow(
                    new List<FillUpdateCompareItem> { item });
                UNCAD.UI.ModalWindowHost.Show(window);
                return window.Confirmed;
            }
            catch (System.Exception ex)
            {
                Log.Error("U1U 更新对比读取失败，已停止更新", ex);
                ctx.Write("\n[U1U] 更新对比读取失败，图纸未修改: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 把电缆型号的解析结果直接打到命令行:1*16 → 8.5 接地、匹配到
        /// 哪一项、或未匹配(未匹配行会被严格模式丢弃,只看表格发现的
        /// "只剩线管/软管"问题在这里一眼见底)。
        /// </summary>
        private static string Describe(FillReviewData review)
            => review == null ? "null" : string.Join(" | ", review.Items.Select(item =>
                item.Code + ":" + item.Name + "(含=" + item.Included
                + ",匹配=" + item.CatalogMatched + ")"));

        /// <summary>把 BOQ 输出路径登记到操作记录（FilePath 可能是多个文件用 ";" 拼接）。</summary>
        internal static void RecordOutputs(CadContext ctx, string kind, string filePath)
        {
            if (ctx?.Operation == null || string.IsNullOrWhiteSpace(filePath)) return;
            foreach (string part in filePath.Split(new[] { ';' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string path = part.Trim();
                if (path.Length > 0) ctx.Operation.Output(kind, path);
            }
        }

        internal static string ResolveMachineWorkbookPath(
            CadContext ctx, string configuredPath)
        {
            string path = (configuredPath ?? "").Trim();
            try
            {
                if (!string.IsNullOrEmpty(path)
                    && MachineWorkbookSource.TryGetSnapshot(path,
                        out MachineWorkbookSnapshotInfo snapshot))
                {
                    ctx.Write("\n[U1F/U1U] 使用上次成功刷新的 SQLite 机台快照: "
                        + (snapshot?.SourceDisplay ?? path)
                        + "（本命令不会读取源 Excel）");
                    return path;
                }
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[U1F/U1U] SQLite 机台快照不可用，请执行 U1DATA 或在 U1SET 重新刷新: "
                    + ex.Message);
                Log.Warn("U1F/U1U machine SQLite snapshot check failed: " + ex.Message);
                return null;
            }

            if (!string.IsNullOrEmpty(path))
            {
                ctx.Write("\n[U1F/U1U] 机台数据尚未导入 SQLite，请执行 U1DATA 或在 U1SET 点击“刷新”。");
                Log.Warn("机台数据 SQLite 快照不存在，U1F/U1U 未读取源 Excel。源: " + path);
                return null;
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
            ctx.Write("\n[U1F/U1U] 已记住 Excel: " + path
                + "；请执行 U1DATA，或在 U1SET 点击“刷新”导入 SQLite。");
            return null;
        }

        // ---- 以下为兼容薄壳：签名被 BatchFillUpdateCoordinator / CircuitCoverageGuard /
        // PdfExportFeature / 测试引用，实现已按阶段拆到对应服务。----

        /// <summary>
        /// （<see cref="FillMachinePickStage.TryResolveFrameCircuit"/> 读图框持久身份 +
        /// 匹配 SQLite 快照行）。U1PDF 复用它，两处口径漂移会让 PDF 挂到错误的回路上。
        /// </summary>
        internal static bool TryResolveFrameCircuit(Transaction transaction,
            FrameRegionGroup region, FillWorkbookSnapshot workbook,
            CadBlockDefinitionReader definitions,
            out string machineId, out string circuitName, out string upstreamId,
            out string error)
            => FillMachinePickStage.TryResolveFrameCircuit(transaction, region, workbook,
                definitions, out machineId, out circuitName, out upstreamId, out error);

        internal static int ResolveTableWriteCapacity(CadContext ctx, ObjectId[] tableIds,
            int startRow, int configuredRows)
            => FillPlanningStage.ResolveWriteCapacity(ctx, tableIds, startRow, configuredRows);

        internal static int ResolveTableWriteCapacity(Transaction transaction,
            ObjectId[] tableIds, int startRow, int configuredRows)
            => FillPlanningStage.ResolveWriteCapacity(transaction, tableIds, startRow,
                configuredRows);

        internal static bool ApplyRuanguanLength(CadContext ctx, FillSelection selection,
            FillReviewData review)
            => FillRuanguanStage.Apply(ctx, selection, review);

        internal static bool ApplyRuanguanLength(CadContext ctx, FillSelection selection,
            FillReviewData review, BoqCatalogIndex catalog, FillPlanningOptions options,
            bool updateMode = false, Transaction transaction = null)
            => FillRuanguanStage.Apply(ctx, selection, review, catalog, options,
                updateMode, transaction);

        internal static RuanguanLengthState ClassifyRuanguanLength(int blockCount,
            bool completeFrameScope, bool updateMode, string meters)
            => FillRuanguanStage.ClassifyRuanguanLength(blockCount, completeFrameScope,
                updateMode, meters);

        internal static void WriteCablePlanNote(CadContext ctx, MachineRow machine,
            TableGenerationOutput tablePlan)
            => FillPlanningStage.WriteCablePlanNote(ctx, machine, tablePlan);

        internal static FillUpdateCompareItem BuildCompareItem(MachineRow machine,
            List<TableFillRow> plannedRows, List<TableFillRow> existingRows,
            FrameInfoJsonRecord previous, string selectedHandle,
            string legacyLastUpdated = "", CableReserveInfo reserve = null,
            FillReviewData finalReview = null)
            => FillReviewStage.BuildCompareItem(machine, plannedRows, existingRows,
                previous, selectedHandle, legacyLastUpdated, reserve, finalReview);

        internal static void ResolveUpdateCableFromExistingTable(CadContext ctx,
            FillSelection selection, MachineRow picked, BoqCatalogIndex catalog,
            Transaction transaction = null, CadBlockDefinitionReader definitions = null)
            => FillMachinePickStage.ResolveUpdateCableFromExistingTable(ctx, selection,
                picked, catalog, transaction, definitions);

        /// <summary>应用审阅窗中修改的电缆米数；无效输入保留图上实测值并返回 false。</summary>
        internal static bool ApplyCableLengthOverride(CableStatResult statistics, string value)
            => CableReserveReview.ApplyCableLengthOverride(statistics, value);

        internal static void SyncCableReserveTotal(CableReserveInfo reserve, string cableMeters)
            => CableReserveReview.SyncCableReserveTotal(reserve, cableMeters);

        /// <summary>硬拦截版整机回路校验（U1F/U1S/U1DWG 用）。</summary>
        internal static bool EnsureCircuitCoverage(CadContext ctx,
            FillWorkbookSnapshot workbook, string command, string machineId,
            string circuitName)
            => FillCoverageGate.EnsureCircuitCoverage(ctx, workbook, command,
                machineId, circuitName);

        /// <summary>硬拦截版整机回路校验（U1F/U1S/U1DWG 用）。</summary>
        internal static bool EnsureCircuitCoverage(CadContext ctx,
            FillWorkbookSnapshot workbook, string command,
            IEnumerable<KeyValuePair<string, string>> machineCircuits)
            => FillCoverageGate.EnsureCircuitCoverage(ctx, workbook, command,
                machineCircuits);

        /// <summary>评估版整机回路校验：只列缺失回路，不打印中止文案（U1U 用）。</summary>
        internal static List<CircuitCoverageResult> EvaluateCircuitCoverage(
            CadContext ctx, FillWorkbookSnapshot workbook, string command,
            IEnumerable<KeyValuePair<string, string>> machineCircuits)
            => FillCoverageGate.EvaluateCircuitCoverage(ctx, workbook, command,
                machineCircuits);

        /// <summary>返回未通过整机回路校验的机台 ID（规范化键，供按机台过滤 BOQ）。</summary>
        internal static List<string> CoverageIncompleteMachines(CadContext ctx,
            FillWorkbookSnapshot workbook, string command,
            IEnumerable<KeyValuePair<string, string>> machineCircuits)
            => FillCoverageGate.CoverageIncompleteMachines(ctx, workbook, command,
                machineCircuits);

        internal static CableReserveInfo ResolveCableReserve(FrameInfoJsonRecord previous,
            MachineRow machine, CableStatResult statistics,
            double ownerReserveMillimeters = CableReservePolicy.DefaultOwnerReserveMillimeters,
            CableReserveSettings reserveSettings = null)
            => CableReserveReview.ResolveCableReserve(previous, machine, statistics,
                ownerReserveMillimeters, reserveSettings);

        internal static void InitializeReserveReview(FillReviewData review,
            CableReserveInfo reserve)
            => CableReserveReview.InitializeReserveReview(review, reserve);

        internal static void ApplyReviewReserve(CableStatResult statistics,
            CableReserveInfo reserve, FillReviewData review)
            => CableReserveReview.ApplyReviewReserve(statistics, reserve, review);

        internal static void ApplyDeviceReserve(CableStatResult statistics,
            CableReserveInfo reserve, double deviceReserveMillimeters)
            => CableReserveReview.ApplyDeviceReserve(statistics, reserve,
                deviceReserveMillimeters);

        /// <summary>Downstream 状态对应的附加清单说明（命令行输出用）。</summary>
        internal static string DescribeDownstreamExtra(DownstreamState state,
            FillRuntimeOptions options)
            => FillPlanningStage.DescribeDownstreamExtra(state, options);
    }
}
