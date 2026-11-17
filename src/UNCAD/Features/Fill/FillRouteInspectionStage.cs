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
    /// 3.3.0（D-054）：一个图框范围内的路由体检结果——连通性报告（特性②，
    /// 报告级不拦截）、图层矫正计划与报告文案（特性①）、3D 路由预览模型
    /// （特性③，场景在 Features 阶段构建后传入窗体）。
    /// </summary>
    internal sealed class FillRouteInspection
    {
        /// <summary>连通性体检（null=范围内没有线段）。报告级，绝不拦截写入。</summary>
        public CircuitConnectivityReport Connectivity { get; set; }
        /// <summary>图层矫正计划（null=UNC_FILL_LAYER_GOVERNOR 关闭）。</summary>
        public LayerGovernorPlan LayerPlan { get; set; }
        /// <summary>确认窗/预检报告用的图层矫正文案（null=无内容）。</summary>
        public string LayerCorrectionText { get; set; }
        /// <summary>3D 路由预览模型（null=没有带毫米标注的线段，窗体隐藏预览区）。</summary>
        public QuickLineOrbitPreviewModel Preview { get; set; }

        public string ConnectivityText
            => Connectivity == null ? "—" : Connectivity.ToDisplayText();
    }

    /// <summary>
    /// 3.3.0（D-054）：U1F/U1U 的路由体检阶段——整图扫描一次（QuickLineCadService
    /// .Scan，3.3.0 起含 U1R/Xauto 开洞后的开放 Polyline），按图框范围分桶后：
    /// ① 连通性分析（CircuitConnectivityAnalyzer，端点容差与 U1LX 同口径）；
    /// ② 图层矫正计划（LayerGovernorService + ParallelAnnotationOwnership 归属）；
    /// ③ U1U 预览场景（QuickLineIsometricSceneBuilder.BuildAll 带毫米标 + 地面归零，
    ///    柱位摘要按 QuickLineAxisSpan 解析，解析失败显示原文不猜）。
    /// </summary>
    internal static class FillRouteInspectionStage
    {
        /// <summary>整图扫描（含未标注线，供连通性与图层识别）。</summary>
        internal static IReadOnlyList<QuickLineCadSegment> Scan(CadContext ctx)
            => QuickLineCadService.Scan(ctx, new QuickLineScanOptions
            {
                IncludeUnlabelled = true
            });

        /// <summary>
        /// 单框 U1F/U1U 的体检入口：范围=图框实体集（U1U 单框用分区结果，
        /// U1F 用本次选集），柱位摘要取机台行的上下游轴位；体检完输出命令行
        /// 诊断（连通性报告级 + 图层矫正预告）。updateMode=U1U 时构建 3D 预览。
        /// 3.3.0.1：U1U 传图框 Boundary——Collect 模式的 EntityIds 从来不含
        /// Line 电缆线（TryAnchor 严格模式只收表格/文字/块），线段归属必须
        /// 按边界几何判定（现场报告"U1U 没有 3D 预览、电缆/桥架图层没动"的
        /// 根因，UNCAD_ROUTE_DIAG 真图复现 scope∩扫描=0）。
        /// </summary>
        internal static FillRouteInspection InspectSelectionAndReport(
            CadContext ctx, FillSelection selection, MachineRow picked,
            bool updateMode)
        {
            string command = updateMode ? CommandIds.FillUpdate : CommandIds.Fill;
            System.Collections.Generic.IEnumerable<ObjectId> scope =
                selection?.Region != null
                    ? selection.Region.EntityIds
                    : selection?.SourceIds
                    ?? Array.Empty<ObjectId>();
            IReadOnlyList<QuickLineCadSegment> scanned = Scan(ctx);
            FillRouteInspection inspection;
            using (Transaction readTransaction =
                ctx.Db.TransactionManager.StartTransaction())
            {
                inspection = Inspect(ctx, readTransaction, scope, scanned,
                    picked?.UpstreamAxis, picked?.DownstreamAxis, updateMode,
                    selection?.Region?.Boundary);
                readTransaction.Commit();
            }
            ctx.Write("\n[" + command + "] 连通性：" + inspection.ConnectivityText
                + "（报告级检查，不拦截写入）。");
            if (inspection.LayerCorrectionText != null)
                ctx.Write("\n[" + command + "] " + inspection.LayerCorrectionText
                    + "；将随本次写入一并执行（一次 U 可回退）。");
            return inspection;
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
        /// 单框选集 SourceIds）；upstream/downstreamAxis 用于柱位摘要（预览）。
        /// buildPreview=false（U1F）时跳过场景构建。
        /// 3.3.0.1：boundary（图框边界矩形）非空时线段归属按几何判定（中点在
        /// 界内），EntityIds 仅作无边界时的回退——Collect 严格模式不收 Line。
        /// 平行标注输出按宿主电缆段判框（宿主在框内→输出在框内），红线实体
        /// 同样不在 EntityIds 里，按宿主归属才不会漏移。
        /// </summary>
        internal static FillRouteInspection Inspect(CadContext ctx,
            Transaction readTransaction, IEnumerable<ObjectId> scopeEntityIds,
            IReadOnlyList<QuickLineCadSegment> scanned,
            string upstreamAxis, string downstreamAxis,
            bool buildPreview, FrameRectangle boundary = null,
            List<ParallelAnnotationOwnershipEntry> precollectedOutputs = null)
        {
            var scope = new HashSet<ObjectId>(scopeEntityIds
                ?? Enumerable.Empty<ObjectId>());
            var inspection = new FillRouteInspection();
            List<QuickLineCadSegment> inScope = (scanned
                ?? Array.Empty<QuickLineCadSegment>())
                .Where(item => item != null && SegmentInScope(item, scope, boundary))
                .ToList();

            // ② 连通性（特性②）：只分析，不拦截——D-054。
            if (inScope.Count > 0)
            {
                inspection.Connectivity = CircuitConnectivityAnalyzer.Analyze(
                    inScope.Select(item => new CircuitConnectivityAnalyzer
                        .InputSegment(
                            item.LineId.Handle.ToString(),
                            new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                            new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y))),
                    1.0);
            }

            // ① 图层矫正计划（特性①）+ 特性D 通道归属共用一次归属收集：
            // 识别依据=扫描器认出的线段+匹配到的毫米标、ParallelAnnotationMetadata
            // 输出（KIND=U1Q/U1C，特性④归属）。3.3.0.1：只喂本框的线段（绝不
            // 动图框外的图形）；输出全图收集后按宿主段过滤到本框（Plan 的 scope
            // 传 null——列表已预过滤）。
            List<ParallelAnnotationOwnershipEntry> frameOutputs = null;
            bool governorEnabled =
                Settings.GetBool(ConfigKeys.FillLayerGovernor, true);
            if (governorEnabled || buildPreview)
            {
                // 3.3.3.2（审计 #4）：批量链在预检循环外收一次经参数传入
                //（逐框各收一遍是 O(图框×实体)——与 routeScanned 同一教训）；
                // 单框路径不传，此处自收（保持 3.3.0.1 行为）。
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
            }
            if (governorEnabled && frameOutputs != null)
            {
                inspection.LayerPlan = LayerGovernorService.Plan(ctx, readTransaction,
                    inScope, frameOutputs, null);
                if (inspection.LayerPlan != null && !inspection.LayerPlan.IsEmpty)
                    inspection.LayerCorrectionText =
                        inspection.LayerPlan.Counters.Describe();
            }

            // ③ 预览（特性③）：带毫米标的线段重建等测场景并整体地面归零。
            // 3.3.0.1：包裹 try/catch——场景构建对方向不可分类的草图会抛
            // ArgumentException，预览是展示件，绝不能把 U1U 主流程炸掉。
            // 3.3.1（D-054 Amendment）：逐段带入毫米标原文（特性B）、上下游
            // 轴位原文（特性C 端点钉标签）、桥架/线管通道归属（特性D 双线）。
            if (buildPreview)
            {
                try
                {
                    Dictionary<string, QuickLineOrbitPreviewChannel> channels =
                        BuildChannels(readTransaction, frameOutputs, inScope);
                    inspection.Preview = BuildPreview(inScope,
                        QuickLineAxisSpan.Describe(upstreamAxis, downstreamAxis),
                        upstreamAxis, downstreamAxis, channels);
                }
                catch (System.Exception ex)
                {
                    Log.Warn("[U1U] 3D 预览场景构建失败（不影响写入）：" + ex.Message);
                }
            }
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
        /// 场景构建与 QuickLine3dPreviewFeature.BuildAll 同口径：真实毫米做距离、
        /// 未确认占位标按当前画长、COMPLETED 标记保留。3.3.1：毫米标原文按段
        /// 句柄带入（特性B），上下游轴位原文带入（特性C），桥架/线管通道归属
        /// 带入（特性D）。
        /// </summary>
        private static QuickLineOrbitPreviewModel BuildPreview(
            IReadOnlyList<QuickLineCadSegment> inScope, string axisSummary,
            string upstreamAxis, string downstreamAxis,
            Dictionary<string, QuickLineOrbitPreviewChannel> channels)
        {
            List<QuickLineCadSegment> labelled = inScope
                .Where(item => item.HasMillimetreLabel).ToList();
            if (labelled.Count == 0) return null;
            var distances = labelled.ToDictionary(
                item => item.LineId.Handle.ToString(),
                item => item.LabelMillimetres.Value,
                StringComparer.OrdinalIgnoreCase);
            var displayDistances = labelled.ToDictionary(
                item => item.LineId.Handle.ToString(),
                item => !item.HasCompletionMarker
                    && QuickLineMillimeterText.IsPlaceholder(item.LabelText)
                        ? item.Length : item.LabelMillimetres.Value,
                StringComparer.OrdinalIgnoreCase);
            var completed = new HashSet<string>(labelled
                .Where(item => item.HasCompletionMarker)
                .Select(item => item.LineId.Handle.ToString()),
                StringComparer.OrdinalIgnoreCase);
            var labelTexts = labelled.ToDictionary(
                item => item.LineId.Handle.ToString(),
                item => item.LabelText ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
            QuickLineGraph graph = QuickLineGraph.Build(labelled.Select(item =>
                new QuickLineSegment(item.LineId.Handle.ToString(),
                    new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                    new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y))), 1.0);
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, displayDistances, completed);
            return QuickLineOrbitPreviewModel.FromScene(scene, axisSummary,
                upstreamAxis, downstreamAxis, labelTexts, channels);
        }

        /// <summary>3.3.1（特性D）：U1Q 标注文本解析不出宽度时的缺省桥架宽（图例注明）。</summary>
        private const double DefaultTrayWidthMillimetres = 200.0;

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
                        conduitHosts.Add(host.LineId.Handle.ToString());
                    continue;
                }
                string text = TryReadOutputText(readTransaction, entry.EntityId);
                double width;
                bool assumed;
                if (text != null && UNCAD.Core.Text.BridgeLabelFormatter
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
                    string key = host.LineId.Handle.ToString();
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
        private static string TryReadOutputText(Transaction transaction,
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
