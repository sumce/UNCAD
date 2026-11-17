using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using UNCAD.Features.Submit;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.Fill
{
    /// <summary>批量 U1U 的单图框更新计划（预检阶段产出，写事务消费）。
    /// 2026-11-09 从 BatchFillUpdateCoordinator 拆出（编排类瘦身，行数守卫
    /// 扩面到 Features 层全部编排器）：预检构建与计划 DTO 同住一个文件。</summary>
    internal sealed class BatchFillUpdatePlan
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
        /// <summary>D-052-A：本框全部待选替代项（请求键 + 清单项引用），确认后逐项应用。</summary>
        public List<BatchCatalogAssignment> CatalogAssignments { get; set; }
            = new List<BatchCatalogAssignment>();
        public bool HoseWasMissingBeforeCableChoice { get; set; }
        /// <summary>D-052-B：复制图框的 CABLE_INFO 残留解析出的设备内预留建议值
        /// （毫米）。仅供预留弹窗预填 + 来源标注，不静默写回（3.0.0.8 语义不变）。</summary>
        public double? DeviceReserveHintMm { get; set; }
        /// <summary>预检阶段（在预检读事务里）算好的表格可写行数；-1 表示未计算。</summary>
        public int Capacity { get; set; } = -1;
        /// <summary>该图框的提示（统计类别不完整/Downstream 状态等），进批量结果窗。</summary>
        public List<string> Notes { get; set; } = new List<string>();
        /// <summary>D-043：已吸收的老版设备预留标注实体（写事务里删除）。</summary>
        public List<ObjectId> DeviceReserveEraseIds { get; set; } = new List<ObjectId>();
        /// <summary>检测到本框携带源框元数据（外来 JSON 被拒）——写事务里
        /// 先显式清空 CABLE_INFO/CONDUIT_INFO/FRAMEINFO 旧信息再写新数据
        /// （2026-11-06，用户要求 U1F/U1U 定义要清干净）。</summary>
        public bool CopiedFrame { get; set; }
        /// <summary>3.3.0（D-054）：路由体检（连通性/图层矫正/预览场景），
        /// 预检阶段构建，确认窗与写事务消费。</summary>
        public FillRouteInspection RouteInspection { get; set; }
    }

    /// <summary>
    /// D-052-A：一个待选替代项——请求键（进批量选择窗）与它对应的清单项引用
    /// （确认后逐项 ReplaceWithCatalogItem）。不再只收集电缆/母线插接箱各一条：
    /// 每一个未匹配固定清单的清单项都必须能在批量窗口里手动选择替代。
    /// </summary>
    internal sealed class BatchCatalogAssignment
    {
        public string RequestKey { get; set; } = "";
        public Core.Fill.FillReviewItem Item { get; set; }
    }

    /// <summary>
    /// 批量 U1U 的单图框预检（2026-11-09 从 BatchFillUpdateCoordinator.Preflight
    /// 拆出）：选集拆分 → 身份/机台匹配 → frameinfo_json 读取（句柄校验 +
    /// D-052 搬场重绑）→ CABLE_INFO 预留恢复（外来 JSON 守卫 + D-052 预留建议值）
    /// → D-043 横排毫米标决策（先于统计，吸收文本从统计排除）→ 统计 → 清单规划
    /// → 全类别替代请求收集。
    /// </summary>
    internal static class BatchFillUpdatePreflight
    {
        internal static void Build(CadContext ctx, Transaction readTransaction,
            FrameRegionGroup region,
            FillRuntimeOptions options, FillWorkbookSnapshot workbook,
            List<BatchFillUpdatePlan> plans, List<string> errors,
            List<BatchCatalogRequest> catalogRequests,
            CadBlockDefinitionReader definitions, StatisticsSettingsSnapshot statisticsSettings,
            IReadOnlyList<UNCAD.Cad.QuickLine.QuickLineCadSegment> scanned = null,
            List<UNCAD.Cad.ParallelAnnotationOwnershipEntry> precollectedOutputs = null)
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
            // 本框是否携带源框元数据（外来 JSON 被拒）——构造 Plan 时挂
            // CopiedFrame，写事务里据此清空源框遗留旧信息属性。
            bool plan_CopiedFrame = false;
            // D-052-B：复制图框的设备内预留建议值（CABLE_INFO 残留解析，仅预填）。
            double? plan_DeviceReserveHintMm = null;

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
                identity,
                out string foreignJsonNote, out string movedJsonNote);
            if (movedJsonNote.Length > 0)
            {
                // D-052-C：整机台搬场（跨文件复制/移动，身份未变）——记录收下，
                // 不算外来、不触发复制框清理；写入时盖成本框句柄。
                notes.Add(movedJsonNote);
                Log.Info("[U1U] " + prefix + movedJsonNote);
            }
            if (foreignJsonNote.Length > 0)
            {
                plan_CopiedFrame = true;
                notes.Add("检测到复制图框遗留的其他图框 JSON，已忽略："
                    + "不继承其设备内预留/电缆替代型号/更新历史；"
                    + "写入时会清空源框遗留的旧信息属性。");
            }
            // 3.2.0.6（审计修复）：批量链补 FrameRedefineGuard——与单框 FillFeature
            // 对齐。批量机台恒来自图框身份（改机分支天然空转），此处防的是
            // 幽灵记录：物理身份就是本机台、但 JSON 记录指向别机（历史残留/
            // 手工移植）。单框链有 SameMachine_GhostRecordIsDropped 防御，批量链
            // 此前没有——别机的设备内预留会被 ResolveCableReserve 采用、经
            // Update 写进全新 JSON，以本机台名义永久化。空机台号会话合成记录
            //（D-043 吸收/块A 恢复）在 Guard 入口原样放行，不受影响。
            string guardNote;
            previousRecord = UNCAD.Core.Fill.FrameRedefineGuard.Apply(
                identity, previousRecord, machine,
                ref plan_DeviceReserveHintMm, out guardNote);
            if (guardNote.Length > 0)
            {
                notes.Add(guardNote);
                Log.Info("[U1U] " + prefix + guardNote);
            }
            // 现场需求（2026-11-02）：JSON 没有设备内预留记录时，默认采用图框
            // CABLE_INFO 文本里已有的「数值（设备内）」——不询问用户；恢复值
            // 写回记录，后续不重复恢复并随 JSON 持久化。
            // 2026-11-06 修复：整框复制会把源框 CABLE_INFO 一起带过来——检测到
            // 外来 JSON（= 本框元数据是复制来的）时，CABLE_INFO 同源同样不可信，
            // 一并跳过恢复，设备内预留交用户重新确认（否则复制框 U1U 会静默
            // 继承源机台的设备预留）。
            if (foreignJsonNote.Length == 0
                && (previousRecord == null
                    || string.IsNullOrWhiteSpace(previousRecord.DeviceReserveMm)))
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
            else if (foreignJsonNote.Length > 0)
            {
                // D-052-B：复制图框不做静默恢复（3.0.0.8 守卫不变），但把源框
                // CABLE_INFO 残留里可解析的「数值（设备内）」作为建议值带出去
                // ——预留弹窗预填 + 来源标注，用户核对确认，不再默认 0。
                string hintCableInfo = CadBlockAttributeWriter.ReadFirstTagged(
                    readTransaction, selection.FrameBlockIds,
                    UNCAD.Core.Fill.FrameBlockFiller.TagCable);
                if (UNCAD.Core.Fill.CableReserveReview.TryParseDeviceReserveMillimeters(
                        hintCableInfo, out double copiedReserveHint))
                {
                    plan_DeviceReserveHintMm = copiedReserveHint;
                    notes.Add("设备内预留已按源框 CABLE_INFO 预填 "
                        + UNCAD.Core.Text.TextFormatter.FormatNum(copiedReserveHint)
                        + " mm（复制源，请在弹窗核对后确认）。");
                }
            }
            // D-043：老版横排毫米标自动识别（U1SET 可关）。恰好一处→吸收并删除
            // 标注；多处→不乱取，备注进结果窗交人工确认。
            // 3.0.0.16：复制框（外来 JSON）例外——随框带过来的毫米标是源框残留，
            // 不自动吸收、不删除（否则源机台设备内预留从这条暗道静默继承，批量
            // 路径连确认窗都不弹）。只从统计排除（“3000mm”不排除会当电缆长度
            // 双算），图上文字保留给用户对照，设备内预留交预留弹窗重新填写。
            DeviceReserveMarkerReader.Result reserveMarkers =
                DeviceReserveMarkerReader.Collect(readTransaction,
                    selection.TextIds);
            var markerExcludedIds = new List<ObjectId>();
            if (reserveMarkers.Hits.Count == 1 && foreignJsonNote.Length == 0)
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
            else if (reserveMarkers.Hits.Count == 1)
            {
                markerExcludedIds.AddRange(reserveMarkers.EntityIds);
                // 3.2.0.7（用户报告：同一图框要跑两次 U1U 才正确）：首跑把复制
                // 残留毫米标值作为设备内预留建议值带出（D-052-B 同语义：预填+
                // 核对，非静默继承）——首跑即得正确预留，文字仍保留。
                if (plan_DeviceReserveHintMm == null)
                {
                    plan_DeviceReserveHintMm = reserveMarkers.Hits[0].Millimeters;
                    markerNote = "复制图框携带源框的横排毫米标 "
                        + reserveMarkers.Hits[0].Text
                        + "，未自动吸收（源框残留，避免静默继承），也不计入米数统计；"
                        + "图上文字保留，弹窗已按该值预填（复制残留毫米标），请核对后确认";
                }
                else
                {
                    markerNote = "复制图框携带源框的横排毫米标 "
                        + reserveMarkers.Hits[0].Text
                        + "，未自动吸收（源框残留，避免静默继承），也不计入米数统计；"
                        + "图上文字保留，如适用请在弹窗手动填写";
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
            // D-052-A：全类别收集——每一个未匹配固定清单的清单项都进批量选择窗。
            // 以前只收集电缆/母线插接箱各一条：桥架/线管/软管等未匹配项（以及
            // 第二条电缆行）在批量里无处可选，用户把窗内项目选完后仍弹
            // 「未匹配固定清单」并整批取消——现场报告的死角。
            var catalogAssignments = new List<BatchCatalogAssignment>();
            foreach (FillReviewItem item in review.Items.Where(item =>
                item.RequiresCatalogConfirmation))
            {
                string category = FillReviewData.CategoryName(item.Category);
                string originalModel = item.Category == TableFillCategory.Cable
                    ? review.BoqCableModel
                    : item.Category == TableFillCategory.BusPlugBox ? machine.Detail
                        : item.Name;
                string key = region.Handle + ":" + category + ":"
                    + review.Items.IndexOf(item);
                var request = new BatchCatalogRequest(key, machine.MachineId,
                    machine.CircuitName, category, originalModel,
                    workbook.Catalog.SelectableItems);
                if (request.Candidates.Count == 0)
                {
                    errors.Add(prefix + "固定清单没有可选择的" + category + "规格。");
                    return;
                }
                catalogRequests.Add(request);
                catalogAssignments.Add(new BatchCatalogAssignment
                {
                    RequestKey = key,
                    Item = item
                });
            }

            if (!string.IsNullOrEmpty(markerNote))
            {
                notes.Add(markerNote);
                Log.Info("[U1U] " + prefix + markerNote + "。");
            }

            // 3.3.0（D-054）：路由体检（连通性/图层矫正/U1U 预览场景）——批量链
            // 整图只扫一次，此处按图框范围体检（机台轴位已解析，柱位摘要不猜）。
            // 3.3.0.1：传 region.Boundary——EntityIds 严格模式不含 Line 电缆线，
            // 线段归属按边界几何判定（单框同根因）。
            FillRouteInspection routeInspection = scanned == null
                ? new FillRouteInspection()
                : FillRouteInspectionStage.Inspect(ctx, readTransaction,
                    region.EntityIds, scanned,
                    machine.UpstreamAxis, machine.DownstreamAxis, true,
                    region.Boundary, precollectedOutputs);
            if (routeInspection.Connectivity != null
                && (routeInspection.Connectivity.IsolatedSegmentIds.Count > 0
                    || routeInspection.Connectivity.Components.Count > 1))
                notes.Add("连通性：" + routeInspection.ConnectivityText
                    + "（报告级，不拦截写入）。");
            if (routeInspection.LayerCorrectionText != null)
                notes.Add(routeInspection.LayerCorrectionText
                    + "；将随本次写入一并执行。");

            plans.Add(new BatchFillUpdatePlan
            {
                Region = region,
                Selection = selection,
                Machine = review.Machine,
                Summation = summation,
                Statistics = statistics,
                Review = review,
                Reserve = reserve,
                Rows = review.SelectedRows(),
                CatalogAssignments = catalogAssignments,
                HoseWasMissingBeforeCableChoice = hoseWasMissingBeforeCableChoice,
                DeviceReserveHintMm = plan_DeviceReserveHintMm,
                DeviceState = deviceState,
                DeviceOutletStateKnown = deviceOutletStateKnown,
                Notes = notes,
                DeviceReserveEraseIds = plan_DeviceReserveEraseIds,
                CopiedFrame = plan_CopiedFrame,
                PreviousRecord = previousRecord,
                RouteInspection = routeInspection,
                ExistingRows = FillRowDiffBuilder.ReadRows(
                    CadSubmissionReader.Read(readTransaction, selection.TableIds)),
                LegacyLastUpdated = FrameLegacyInfoReader.ReadLastUpdatedText(
                    readTransaction, selection),
                // 容量在预检读事务里算一次；以前每个图框在此后还各开一个写事务重算。
                Capacity = FillFeature.ResolveTableWriteCapacity(readTransaction,
                    selection.TableIds, options.StartRow, options.ClearRows)
            });
        }
    }
}
