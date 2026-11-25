using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Excel;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// 3.3.0（D-054）：一个图框范围内的连通性报告与图层矫正计划。
    /// D-058：加线路识别结果（通道、无毫米标线段、问题清单）；U1U 另带 3D
    /// 路由预览模型。
    /// </summary>
    internal sealed class FillRouteInspection
    {
        /// <summary>连通性体检（null=范围内没有线段）。报告级，绝不拦截写入。</summary>
        public CircuitConnectivityReport Connectivity { get; set; }
        /// <summary>图层矫正计划（null=UNC_FILL_LAYER_GOVERNOR 关闭）。</summary>
        public LayerGovernorPlan LayerPlan { get; set; }
        /// <summary>确认窗/预检报告用的图层矫正文案（null=无内容）。</summary>
        public string LayerCorrectionText { get; set; }
        /// <summary>3D 路由预览模型（U1U 构建；null=没有带毫米标注的线段或构建失败）。</summary>
        public QuickLineOrbitPreviewModel Preview { get; set; }
        /// <summary>本框已识别路由与恢复的旧 U1L 标签，供写事务复用。</summary>
        public List<QuickLineCadSegment> Segments { get; set; }
            = new List<QuickLineCadSegment>();
        /// <summary>本框带 UNCAD_PARALLEL 元数据的输出。</summary>
        public List<ParallelAnnotationOwnershipEntry> ParallelOutputs { get; set; }
            = new List<ParallelAnnotationOwnershipEntry>();

        /// <summary>D-058：本次连通性/路线图使用的断口容差（图形单位）。</summary>
        public double GapTolerance { get; set; }
        /// <summary>D-058：本框电缆线段的桥架/线管通道（键=电缆线段句柄）。</summary>
        public Dictionary<string, QuickLineOrbitPreviewChannel> Channels { get; set; }
            = new Dictionary<string, QuickLineOrbitPreviewChannel>(
                StringComparer.OrdinalIgnoreCase);
        /// <summary>D-058：没有毫米标、也不是桥架/线管标注线的线段句柄（问题）。</summary>
        public List<string> UnlabelledSegmentHandles { get; set; } = new List<string>();
        /// <summary>D-058：认出的桥架/线管标注线句柄（不算问题）。</summary>
        public List<string> AnnotationLineHandles { get; set; } = new List<string>();
        /// <summary>D-058：没有元数据、按旁边文字就近绑定的桥架/线管。</summary>
        public List<RouteAnnotationTextBinding> TextBoundAnnotations { get; set; }
            = new List<RouteAnnotationTextBinding>();
        /// <summary>D-058：宿主线段已找不到的桥架/线管标注文字。</summary>
        public List<string> OrphanAnnotations { get; set; } = new List<string>();
        /// <summary>D-059：桥架/线管整趟清单（每趟一个标注、一个总长、若干宿主）。</summary>
        public List<FillAnnotationRun> AnnotationRuns { get; set; } = new List<FillAnnotationRun>();
        /// <summary>D-059：楼层摘要（「台账 2F→3F」），进识别摘要。</summary>
        public string FloorSummary { get; set; } = "";
        /// <summary>D-058：识别结果一句话（3D 窗格与命令行共用）。</summary>
        public string RecognitionText { get; set; } = "";
        /// <summary>D-058：识别出的问题，逐条成句。</summary>
        public List<string> RouteIssues { get; set; } = new List<string>();
        /// <summary>D-058：是否已尝试构建预览（每框只建一次）。</summary>
        public bool PreviewAttempted { get; set; }
        /// <summary>D-058：预览构建失败原因（null=未失败）。</summary>
        public string PreviewFailure { get; set; }

        public string ConnectivityText
            => Connectivity == null ? "—" : Connectivity.ToDisplayText();
    }

    /// <summary>
    /// 3.3.0（D-054）：U1F/U1U 整图扫描后执行连通性分析、图层矫正计划和线路识别。
    /// D-058：U1U 重新构建 3D 路由预览（确认窗左半），U1F 仍不构建。
    /// </summary>
    internal static class FillRouteInspectionStage
    {
        internal static FillDrawingSettingsCorrectionResult CorrectDrawingSettings(
            CadContext ctx, Transaction transaction,
            FillRouteInspection inspection, FillDrawingStyleSettings settings,
            bool correctCableLabelPlacement,
            bool correctCableTrayConduitStyles)
            => FillDrawingSettingsCorrector.Apply(ctx, transaction,
                inspection?.Segments, inspection?.ParallelOutputs, settings,
                inspection?.LayerPlan != null, correctCableLabelPlacement,
                correctCableTrayConduitStyles);

        internal static void WriteDrawingStyleCorrectionResult(CadContext ctx,
            string command, int cableLabels, int trayRuns, int conduitRuns,
            int hoseLabels)
        {
            if (cableLabels + trayRuns + conduitRuns + hoseLabels <= 0) return;
            ctx.Write("\n[" + command + "] U1SET 绘图内容已矫正：毫米标位置/角度 "
                + cableLabels + " 个，桥架 " + trayRuns + " 趟，线管 "
                + conduitRuns + " 趟，软管标 " + hoseLabels + " 个。");
        }

        internal static void WriteDrawingStyleCorrectionResult(CadContext ctx,
            string command, FillDrawingSettingsCorrectionResult result)
        {
            if (result == null) return;
            WriteDrawingStyleCorrectionResult(ctx, command, result.CableLabels,
                result.TrayRuns, result.ConduitRuns, result.HoseLabels);
        }

        /// <summary>
        /// 整图扫描（含未标注线，供连通性与图层识别）。D-059：同时带回没有归属
        /// 线段的毫米标（给了图框边界时只报框内的）。
        /// </summary>
        internal static QuickLineScanResult Scan(CadContext ctx,
            FillDrawingStyleSettings styleSettings = null,
            IReadOnlyList<FrameRectangle> boundaries = null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var options = new QuickLineScanOptions
            {
                IncludeUnlabelled = true,
                FrameBoundaries = boundaries,
                // 旧图框用线性标注代替短线时，把标注量出的那段当成电缆段
                // （只影响填充/U1U 扫描；U1LX 仍只认 Line/Polyline）。
                IncludeDimensionSegments = true,
                // 3.8.0.2：带拐角的多段线（每段都在轴上）按段拆开，各段各有自己的
                // 毫米标，3D 里也不再整根丢掉。
                SplitBentPolylines = true
            };
            if (styleSettings != null)
            {
                options.StaleAssociationMaximumDistance =
                    styleSettings.StaleLabelAssociationMaximumDistance;
                options.AdjacentLabelAngleToleranceRadians =
                    styleSettings.AdjacentLabelAngleToleranceRadians;
            }
            QuickLineScanResult scan =
                QuickLineCadService.ScanDetailed(ctx, options);
            timer.Stop();
            Log.Info("[PERF] U1U/U1F 路由扫描: " + timer.ElapsedMilliseconds
                + " ms，线段 " + scan.Segments.Count + "，无归属毫米标 "
                + scan.UnboundLabels.Count + "，合并线恢复毫米标 "
                + scan.Segments.Sum(item => item.RestoredLabelIds?.Count ?? 0));
            return scan;
        }

        /// <summary>
        /// 单框 U1F/U1U 的体检入口：范围=图框实体集，输出连通性诊断和图层矫正预告。
        /// 3.3.0.1：U1U 传图框 Boundary——Collect 模式的 EntityIds 从来不含
        /// Line 电缆线（TryAnchor 严格模式只收表格/文字/块），线段归属必须
        /// 按边界几何判定（现场报告"U1U 没有 3D 预览、电缆/桥架图层没动"的
        /// 根因，UNCAD_ROUTE_DIAG 真图复现 scope∩扫描=0）。
        /// D-058：U1U 在同一读事务里挂上 3D 预览并输出线路识别结果；U1F 不建预览。
        /// </summary>
        internal static FillRouteInspection InspectSelectionAndReport(
            CadContext ctx, FillSelection selection, MachineRow picked,
            bool updateMode, FillDrawingStyleSettings styleSettings = null)
        {
            string command = updateMode ? CommandIds.FillUpdate : CommandIds.Fill;
            System.Collections.Generic.IEnumerable<ObjectId> scope =
                selection?.Region != null
                    ? selection.Region.EntityIds
                    : selection?.SourceIds
                    ?? Array.Empty<ObjectId>();
            QuickLineScanResult scan = Scan(ctx, styleSettings,
                selection?.Region?.Boundary == null ? null
                    : new[] { selection.Region.Boundary });
            var inspectTimer = System.Diagnostics.Stopwatch.StartNew();
            FillRouteInspection inspection;
            using (Transaction readTransaction =
                ctx.Db.TransactionManager.StartTransaction())
            {
                // D-058：桥架/线管标注文字整图只收一次（与线段扫描同一口径）。
                List<RouteAnnotationTextEntity> annotationTexts =
                    RouteAnnotationTextReader.Collect(ctx, readTransaction);
                inspection = Inspect(ctx, readTransaction, scope, scan.Segments,
                    selection?.Region?.Boundary, null, annotationTexts,
                    scan.UnboundLabels, selection);
                // D-058：U1U 的 3D 预览（确认窗左半）。预览是展示件，失败不拦截写入。
                if (updateMode)
                    AttachPreview(ctx, readTransaction, inspection, selection, picked);
                readTransaction.Commit();
            }
            inspectTimer.Stop();
            Log.Info("[PERF] " + command + " 路由归属/诊断: "
                + inspectTimer.ElapsedMilliseconds + " ms，框内线段 "
                + inspection.Segments.Count + "，元数据输出 "
                + inspection.ParallelOutputs.Count);
            ctx.Write("\n[" + command + "] 连通性：" + inspection.ConnectivityText
                + "（报告级检查，不拦截写入）。");
            if (inspection.LayerCorrectionText != null)
                ctx.Write("\n[" + command + "] " + inspection.LayerCorrectionText
                    + "；将随本次写入一并执行（一次 U 可回退）。");
            if (updateMode)
                FillRouteRecognitionStage.WriteReport(ctx, command, inspection);
            return inspection;
        }

        /// <summary>
        /// D-058：给一份体检结果挂上 3D 预览模型（U1U 专用，每框只建一次）。
        /// 场景构建抛异常时降级成一句诊断，绝不能把 U1U 主流程炸掉。
        /// </summary>
        internal static void AttachPreview(CadContext ctx,
            Transaction readTransaction, FillRouteInspection inspection,
            FillSelection selection, MachineRow machine)
        {
            if (inspection == null || inspection.PreviewAttempted) return;
            inspection.PreviewAttempted = true;
            try
            {
                inspection.Preview = BuildPreview(readTransaction, inspection,
                    selection, machine);
            }
            catch (System.Exception ex)
            {
                // 3.8.0.2：日志记完整堆栈——只记消息时定位不到是哪一步炸的。
                Log.Warn("[U1U] 3D 预览场景构建失败（不影响写入）：" + ex);
                inspection.PreviewFailure = "3D 预览场景构建失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 写入提交后的结果行：移正计数 + 无法判定实体点名（特性①）。
        /// </summary>
        internal static void WriteWriteResult(CadContext ctx, string command,
            FillRouteInspection inspection, int movedCount)
        {
            if (inspection?.LayerPlan == null || inspection.LayerPlan.IsEmpty)
                return;
            ctx.Write("\n[" + command + "] 图层矫正完成：" + movedCount
                + " 个实体已移正到法定图层（U_CABLE/U_TRAY/U_CONDUIT/U_HOSE）。");
            foreach (string handle in inspection.LayerPlan.UndeterminableHandles)
                ctx.Write("\n[" + command + "] 实体 " + handle
                    + " 带平行标注元数据但种类无法判定（非 U1Q/U1C/U1B），未移动。");
        }

        /// <summary>
        /// 体检一个图框范围。scopeEntityIds = 图框实体集（region.EntityIds 或
        /// 单框选集 SourceIds）。
        /// 3.3.0.1：boundary（图框边界矩形）非空时线段归属按几何判定（中点在
        /// 界内），EntityIds 仅作无边界时的回退——Collect 严格模式不收 Line。
        /// 平行标注输出按宿主电缆段判框（宿主在框内→输出在框内），红线实体
        /// 同样不在 EntityIds 里，按宿主归属才不会漏移。
        /// D-058/D-059：连通性带断口容差（只有带毫米标的线段参与）；随后做线路识别
        /// （整趟清单/标注线/无毫米标线段/无归属毫米标/轴位块/问题清单）。
        /// annotationTexts 为整图一次收集的桥架/线管标注文字（null 时无法做无元数据
        /// 文字的就近绑定）；unboundLabels 为整图一次扫描带回的没有归属线段的
        /// 毫米标（按边界取框内的）；selection 是本框的选集分区（判轴位块缺不缺）。
        /// </summary>
        internal static FillRouteInspection Inspect(CadContext ctx,
            Transaction readTransaction, IEnumerable<ObjectId> scopeEntityIds,
            IReadOnlyList<QuickLineCadSegment> scanned,
            FrameRectangle boundary = null,
            List<ParallelAnnotationOwnershipEntry> precollectedOutputs = null,
            IReadOnlyList<RouteAnnotationTextEntity> annotationTexts = null,
            IReadOnlyList<QuickLineUnboundLabel> unboundLabels = null,
            FillSelection selection = null)
        {
            var inspectTimer = System.Diagnostics.Stopwatch.StartNew();
            var phaseTimer = System.Diagnostics.Stopwatch.StartNew();
            var scope = new HashSet<ObjectId>(scopeEntityIds
                ?? Enumerable.Empty<ObjectId>());
            var inspection = new FillRouteInspection
            {
                GapTolerance = QuickLineRouteSettings.GapTolerance()
            };
            List<QuickLineCadSegment> inScope = (scanned
                ?? Array.Empty<QuickLineCadSegment>())
                .Where(item => item != null && SegmentInScope(item, scope, boundary))
                .ToList();
            long filterMilliseconds = phaseTimer.ElapsedMilliseconds;

            // ② 连通性（特性②）：只分析，不拦截——D-054。
            // D-058：两端都带毫米标的线段按断口容差接成一条链，接缝记入报告。
            if (inScope.Count > 0)
            {
                var labelledHandles = new HashSet<string>(inScope
                    .Where(item => item.HasMillimetreLabel)
                    .Select(item => item.Key),
                    StringComparer.OrdinalIgnoreCase);
                inspection.Connectivity = CircuitConnectivityAnalyzer.Analyze(
                    inScope.Select(item => new CircuitConnectivityAnalyzer
                        .InputSegment(
                            item.Key,
                            new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                            new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y))),
                    1.0, labelledHandles.Contains, inspection.GapTolerance);
            }

            // ① 图层矫正计划（特性①）+ 特性D 通道归属共用一次归属收集：
            // 识别依据=扫描器认出的线段+匹配到的毫米标、ParallelAnnotationMetadata
            // 输出（KIND=U1Q/U1C，特性④归属）。3.3.0.1：只喂本框的线段（绝不
            // 动图框外的图形）；输出全图收集后按宿主段过滤到本框（Plan 的 scope
            // 传 null——列表已预过滤）。
            List<ParallelAnnotationOwnershipEntry> frameOutputs;
            bool governorEnabled =
                Settings.GetBool(ConfigKeys.FillLayerGovernor, true);
            // 复用整图元数据扫描结果，样式矫正无论图层治理开关状态都可用；
            // 批量路径在循环外传入，避免每框重复扫描。
            phaseTimer.Restart();
            List<ParallelAnnotationOwnershipEntry> outputs =
                precollectedOutputs
                    ?? ParallelAnnotationOwnership.Collect(ctx, readTransaction, null);
            if (boundary != null)
                frameOutputs = FilterOutputsByHostFrame(outputs, inScope,
                    readTransaction, boundary);
            else
            {
                var scopeOnly = new HashSet<ObjectId>(scope
                    ?? Enumerable.Empty<ObjectId>());
                frameOutputs = outputs.Where(entry => entry != null
                    && scopeOnly.Contains(entry.EntityId)).ToList();
            }
            long ownershipMilliseconds = phaseTimer.ElapsedMilliseconds;
            inspection.Segments = inScope;
            inspection.ParallelOutputs = frameOutputs;

            if (governorEnabled && frameOutputs != null)
            {
                phaseTimer.Restart();
                inspection.LayerPlan = LayerGovernorService.Plan(ctx, readTransaction,
                    inScope, frameOutputs, null);
                long layerPlanMilliseconds = phaseTimer.ElapsedMilliseconds;
                if (inspection.LayerPlan != null && !inspection.LayerPlan.IsEmpty)
                    inspection.LayerCorrectionText =
                        inspection.LayerPlan.Counters.Describe();
                if (precollectedOutputs == null)
                    Log.Info("[PERF] 路由图层计划: " + layerPlanMilliseconds + " ms");
            }

            // D-058/D-059：线路识别（整趟清单：元数据归属 → 红线就近 → 文字就近
            // → 认出标注线 → 没有毫米标的线段 / 没有归属的毫米标 / 缺失的轴位块
            // 与其它问题）。U1F/U1U 都做，连通性报告同口径。
            inspection.Channels = BuildChannels(readTransaction, frameOutputs, inScope);
            FillRouteRecognitionStage.Recognize(inspection, inScope, frameOutputs,
                boundary, outputs, annotationTexts, unboundLabels, readTransaction,
                selection);

            inspectTimer.Stop();
            if (precollectedOutputs == null)
                Log.Info("[PERF] 路由范围筛选 " + filterMilliseconds
                    + " ms，元数据归属 " + ownershipMilliseconds
                    + " ms，总体 " + inspectTimer.ElapsedMilliseconds + " ms");
            return inspection;
        }

        /// <summary>
        /// 线段是否属于本次体检的图框：boundary 非空按中点几何判定（3.3.0.1，
        /// EntityIds 严格模式不含 Line）；无边界回退实体集包含（U1F 显式选集）。
        /// </summary>
        private static bool SegmentInScope(QuickLineCadSegment segment,
            HashSet<ObjectId> scope, FrameRectangle boundary)
        {
            if (boundary != null)
                return boundary.Contains(
                    (segment.StartPoint.X + segment.EndPoint.X) / 2.0,
                    (segment.StartPoint.Y + segment.EndPoint.Y) / 2.0);
            return scope == null || scope.Contains(segment.LineId);
        }

        /// <summary>
        /// 输出按宿主电缆段判框（特性④归属的消费端）：宿主段在框内 → 该输出
        /// 属于本框。红线 Line 与其毫米标一样不在 EntityIds 里，按宿主归属才
        /// 不漏；宿主已删/未知的输出不属于任何框，跳过不猜。
        /// 3.3.4（图层收口）：U1B 例外直通——软管三件套（轮廓/引线/文字）
        /// SOURCE=轮廓自引用，而轮廓被扫描器按元数据命中跳过、永不进宿主池，
        /// 按宿主判框必然落空（旧接线下 HoseEntities 计数与 U1B→U_HOSE 移层
        /// 分支不可达，用户手动挪层/建层失败回退后 U1U/U1F 永不纠正软管）。
        /// 软管与电缆无宿主关系，按图框边界几何归属：几何中心在界内即属本框。
        /// 其余 KIND 宿主未知仍跳过不猜（D-054）。
        /// </summary>
        private static List<ParallelAnnotationOwnershipEntry> FilterOutputsByHostFrame(
            List<ParallelAnnotationOwnershipEntry> outputs,
            List<QuickLineCadSegment> inScopeSegments,
            Transaction readTransaction, FrameRectangle boundary)
        {
            // 宿主池=本框线段：解析得到宿主（≥1 条）即属本框；宿主在别的框
            // 或已删（解析不到）都不进本框的计划。
            Dictionary<string, List<QuickLineCadSegment>> hostMap =
                ParallelAnnotationOwnership.MapHostSegments(outputs, inScopeSegments);
            return outputs.Where(entry =>
            {
                if (entry == null || entry.EntityId.IsNull) return false;
                if (hostMap.TryGetValue(entry.EntityId.Handle.ToString(),
                        out List<QuickLineCadSegment> hosts)
                    && hosts.Count > 0) return true;
                return string.Equals(entry.Kind,
                    UNCAD.Core.Dwg.UncadLayerStandard.HoseAnnotationKind,
                    StringComparison.OrdinalIgnoreCase)
                    && OutputCenterInsideBoundary(readTransaction,
                        entry.EntityId, boundary);
            }).ToList();
        }

        /// <summary>U1B 直通归属的几何判：实体几何范围中心是否在图框边界内。
        /// 打不开/已删/取不到范围（锁定、空几何等）= 不属本框，不猜。</summary>
        private static bool OutputCenterInsideBoundary(
            Transaction readTransaction, ObjectId entityId,
            FrameRectangle boundary)
        {
            if (readTransaction == null || boundary == null
                || entityId.IsNull || !entityId.IsValid) return false;
            try
            {
                Entity entity = readTransaction.GetObject(entityId,
                    OpenMode.ForRead, true) as Entity;
                if (entity == null || entity.IsErased) return false;
                Extents3d extents = entity.GeometricExtents;
                return boundary.Contains(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// D-058：U1U 的 3D 路由预览模型。与 U13D 共用毫米标注建模口径（真实
        /// 毫米做距离）；路线端点图带断口容差（只有带毫米标的线段参与）。
        /// 两端按上游块/设备块就近判定，带不回轴位推断；毫米标原文按段句柄
        /// 带入，桥架/线管通道、软管、识别摘要与问题清单一并带入。
        /// 没有带毫米标的线段时返回 null（窗体隐藏预览区）。
        /// </summary>
        private static QuickLineOrbitPreviewModel BuildPreview(
            Transaction readTransaction, FillRouteInspection inspection,
            FillSelection selection, MachineRow machine)
        {
            List<QuickLineCadSegment> labelled = inspection.Segments
                .Where(item => item.HasMillimetreLabel).ToList();
            if (labelled.Count == 0) return null;
            var distances = labelled.ToDictionary(
                item => item.Key,
                item => item.LabelMillimetres.Value,
                StringComparer.OrdinalIgnoreCase);
            var completed = new HashSet<string>(labelled
                .Where(item => item.HasCompletionMarker)
                .Select(item => item.Key),
                StringComparer.OrdinalIgnoreCase);
            var labelTexts = labelled.ToDictionary(
                item => item.Key,
                item => item.LabelText ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
            QuickLineRouteGraph route = QuickLineRouteConnector.Build(
                labelled.Select(item => new QuickLineSegment(
                    item.Key,
                    new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                    new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y))),
                inspection.GapTolerance);
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildMetricPreview(
                route.Graph, distances, completed);
            QuickLineRouteDirection direction =
                FillRouteRecognitionStage.ResolveDirection(scene, route.Graph,
                    labelled, readTransaction, selection, machine,
                    out string directionSource);
            FillRouteRecognitionStage.AddSceneIssues(inspection, labelled, scene,
                direction);
            var sceneNodes = scene.Nodes.ToDictionary(node => node.Id,
                node => node.Position, StringComparer.OrdinalIgnoreCase);
            QuickLineSpatialPoint? upstreamEndpoint =
                direction.IsResolved && sceneNodes.TryGetValue(
                    direction.UpstreamNodeId, out QuickLineSpatialPoint upstream)
                        ? upstream : (QuickLineSpatialPoint?)null;
            QuickLineSpatialPoint? downstreamEndpoint =
                direction.IsResolved && sceneNodes.TryGetValue(
                    direction.DownstreamNodeId, out QuickLineSpatialPoint downstream)
                        ? downstream : (QuickLineSpatialPoint?)null;
            QuickLineOrbitPreviewHose hose = FillRouteRecognitionStage.ReadHose(
                readTransaction, selection, inspection.ParallelOutputs);

            // D-059：楼层平面按台账的上游/设备楼层画；跨层时校验「上游楼层地面
            // 到设备端」的垂直距离（2F→3F 应在 7.6~7.8 m），不在就点名。
            // 3.8.0.3：从线路的基础点起量（端点段是立管时是立管底），插座盘再把
            // 离地那 1 m 加回来——以前直接拿两端高差去比，和 7.6~7.8 不是一个
            // 起算点（现场：「实际 6.2 m」）。
            string rawLevels = Settings.Get(ConfigKeys.PreviewReferenceLevels,
                PreviewReferenceLevels.DefaultList);
            PreviewReferenceLevels.TryParseBaseFloor(rawLevels, out string baseFloorLabel);
            PreviewFloorPlan floors = PreviewFloorPlan.Plan(machine?.PanelFloor,
                machine?.DeviceFloor, baseFloorLabel,
                PreviewReferenceLevels.Parse(rawLevels));
            inspection.FloorSummary = DescribeFloors(floors);
            PreviewCabinetKind cabinetKind = PreviewCabinet.KindOf(machine?.Next);
            if (direction.IsResolved && floors != null)
            {
                string riseWarning = floors.EvaluateRise(
                    PreviewCabinet.BasePoint(upstreamEndpoint.Value,
                        OtherEndOfIncidentSegment(scene, sceneNodes,
                            direction.UpstreamNodeId)).Z,
                    downstreamEndpoint.Value.Z,
                    PreviewCabinet.RouteLiftMillimetres(cabinetKind));
                if (riseWarning != null) inspection.RouteIssues.Add(riseWarning);
            }

            // 3.8.0.4：轴位网格必须按一个轴位 4.8 m 铺，且两端各自落在自己的
            // 轴位格子里；图面距离与台账轴位对不上时点名（以前直接线性插值，
            // 把 11 跨压进几十米的图面，跨距不再 4.8 m，也不提示）。
            if (direction.IsResolved && upstreamEndpoint.HasValue
                && downstreamEndpoint.HasValue)
            {
                string axisWarning = QuickLineColumnGridPlanner.DescribeAxisMismatch(
                    upstreamEndpoint.Value, downstreamEndpoint.Value,
                    machine?.UpstreamAxis, machine?.DownstreamAxis);
                if (axisWarning != null) inspection.RouteIssues.Add(axisWarning);
            }

            inspection.RecognitionText = FillRouteRecognitionStage.BuildSummary(
                inspection, scene.Segments.Count, directionSource, hose);
            string upstreamAxis = machine?.UpstreamAxis;
            string downstreamAxis = machine?.DownstreamAxis;
            return QuickLineOrbitPreviewModel.FromScene(scene,
                QuickLineAxisSpan.Describe(upstreamAxis, downstreamAxis),
                upstreamAxis, downstreamAxis, labelTexts, inspection.Channels,
                upstreamEndpoint, downstreamEndpoint, null,
                direction.IsResolved, direction.Diagnostics,
                hose, inspection.RecognitionText, inspection.RouteIssues,
                floors, cabinetKind,
                FillRouteRecognitionStage.ToPreviewRuns(inspection));
        }

        /// <summary>
        /// 3.8.0.3：与某个场景节点相接的第一段线的另一个端点（上游端点相接的
        /// 那一段，用来认「端点在立管顶上」）。没有相接段时返回 null。
        /// </summary>
        private static QuickLineSpatialPoint? OtherEndOfIncidentSegment(
            QuickLineIsometricScene scene,
            IReadOnlyDictionary<string, QuickLineSpatialPoint> nodes, string nodeId)
        {
            if (scene == null || nodes == null || string.IsNullOrEmpty(nodeId))
                return null;
            foreach (QuickLineIsometricSegment segment in scene.Segments)
            {
                if (string.Equals(segment.StartNodeId, nodeId,
                        StringComparison.OrdinalIgnoreCase)
                    && nodes.TryGetValue(segment.EndNodeId,
                        out QuickLineSpatialPoint end))
                    return end;
                if (string.Equals(segment.EndNodeId, nodeId,
                        StringComparison.OrdinalIgnoreCase)
                    && nodes.TryGetValue(segment.StartNodeId,
                        out QuickLineSpatialPoint start))
                    return start;
            }
            return null;
        }

        /// <summary>楼层摘要：「台账 2F→3F」「台账 3F」「U1SET 本层 2F」「楼层未识别」。</summary>
        internal static string DescribeFloors(PreviewFloorPlan floors)
        {
            if (floors == null || floors.Floors.Count == 0) return "楼层未识别";
            if (floors.CrossesFloors)
                return "台账 " + floors.UpstreamSlab.Label + "→" + floors.DeviceSlab.Label;
            // 3.8.0.2：台账两端楼层都认不出、退回 U1SET「本层=…」时只画出一层平面，
            // 上下游那两块都是空的——取画出来的那一层（以前这里直接空引用，整个
            // 3D 预览跟着构建失败）。
            PreviewFloorSlab slab = floors.UpstreamSlab ?? floors.DeviceSlab
                ?? floors.Floors[0];
            return floors.Source.StartsWith("台账", StringComparison.Ordinal)
                ? "台账 " + slab.Label : floors.Source + " " + slab.Label;
        }

        /// <summary>3.3.1（特性D）：U1Q 标注文本解析不出宽度时的缺省桥架宽（图例注明）。</summary>
        internal const double DefaultTrayWidthMillimetres = 200.0;

        /// <summary>
        /// 3.3.1（D-054 Amendment，特性D）：归属输出 → 宿主段通道字典（键=宿主
        /// 段句柄）。KIND=U1Q→桥架（宽度从 U1Q 标注文本解析，解析不出用 200
        /// 缺省并标记 AssumedDefault 供图例注明）；KIND=U1C→线管（无宽度语义
        /// 不标数）。同一宿主多条输出时桥架取最宽（顺序无关，确定性），宽度
        /// 平手时解析值优先于缺省值；桥架与线管互斥（D-050 Amendment），万一
        /// 同段两者都有，桥架优先绘制、并存不做叠加猜测。只有归属解析到本框
        /// 宿主段的输出才参与（宿主删了的没有——与图层矫正同口径不猜）。
        /// </summary>
        private static Dictionary<string, QuickLineOrbitPreviewChannel>
            BuildChannels(Transaction readTransaction,
                List<ParallelAnnotationOwnershipEntry> outputs,
                List<QuickLineCadSegment> inScope)
        {
            var result = new Dictionary<string, QuickLineOrbitPreviewChannel>(
                StringComparer.OrdinalIgnoreCase);
            if (outputs == null || outputs.Count == 0) return result;
            Dictionary<string, List<QuickLineCadSegment>> hostMap =
                ParallelAnnotationOwnership.MapHostSegments(outputs, inScope);
            // 宿主句柄 → 桥架宽（Item1=宽度，Item2=是否缺省值）/ 是否有线管。
            var trayByHost = new Dictionary<string,
                Tuple<double, bool>>(StringComparer.OrdinalIgnoreCase);
            var conduitHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ParallelAnnotationOwnershipEntry entry in outputs)
            {
                if (entry == null || entry.EntityId.IsNull) continue;
                bool isTray = string.Equals(entry.Kind, "U1Q",
                    StringComparison.OrdinalIgnoreCase);
                bool isConduit = string.Equals(entry.Kind, "U1C",
                    StringComparison.OrdinalIgnoreCase);
                if (!isTray && !isConduit) continue;
                if (!hostMap.TryGetValue(entry.EntityId.Handle.ToString(),
                        out List<QuickLineCadSegment> hosts) || hosts.Count == 0)
                    continue;
                if (isConduit)
                {
                    foreach (QuickLineCadSegment host in hosts)
                        conduitHosts.Add(host.Key);
                    continue;
                }
                string text = TryReadOutputText(readTransaction, entry.EntityId);
                double width;
                bool assumed;
                // 3.8.0.3：宽度也认 U1Q 出标的「200Wx100H」写法（以前一律按缺省 200）。
                if (text != null && RouteAnnotationTextPlanner
                        .TryParseTrayWidthMillimetres(text, out double parsed))
                {
                    width = parsed;
                    assumed = false;
                }
                else
                {
                    width = DefaultTrayWidthMillimetres;
                    assumed = true;
                }
                foreach (QuickLineCadSegment host in hosts)
                {
                    string key = host.Key;
                    if (!trayByHost.TryGetValue(key,
                            out Tuple<double, bool> existing)
                        || width > existing.Item1
                        || (width == existing.Item1 && !assumed))
                        trayByHost[key] = Tuple.Create(width, assumed);
                }
            }
            foreach (string hostHandle in trayByHost.Keys
                .Concat(conduitHosts).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                trayByHost.TryGetValue(hostHandle,
                    out Tuple<double, bool> tray);
                result[hostHandle] = new QuickLineOrbitPreviewChannel(
                    tray?.Item1, tray?.Item2 ?? false,
                    conduitHosts.Contains(hostHandle));
            }
            return result;
        }

        /// <summary>读输出实体的标注文本（DBText 直取；MText 清格式码）。</summary>
        internal static string TryReadOutputText(Transaction transaction,
            ObjectId entityId)
        {
            try
            {
                if (transaction == null || entityId.IsNull) return null;
                if (!(transaction.GetObject(entityId, OpenMode.ForRead, true)
                    is Entity entity) || entity.IsErased) return null;
                if (entity is Autodesk.AutoCAD.DatabaseServices.DBText text)
                    return text.TextString;
                if (entity is Autodesk.AutoCAD.DatabaseServices.MText mtext)
                    return UNCAD.Core.Text.TextParser.CleanMText(mtext.Contents);
                return null;
            }
            catch { return null; }
        }
    }
}
