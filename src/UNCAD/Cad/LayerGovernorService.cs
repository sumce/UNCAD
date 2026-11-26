using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Core.Dwg;
using UNCAD.Cad.QuickLine;

namespace UNCAD.Cad
{
    /// <summary>
    /// 3.3.0（D-054 特性①）：一次图层矫正的执行计划（预检/读事务里构建，
    /// 写事务里执行）。只包含当前图层不对、需要移动的实体；3.8.0.10 起还包含
    /// 法定图层上「真彩白」需要归随层的实体（白纸上出图不可见）。
    /// </summary>
    public sealed class LayerGovernorPlan
    {
        internal LayerGovernorPlan(List<KeyValuePair<ObjectId, string>> moves,
            List<ObjectId> whiteColorFixes,
            UncadLayerCorrectionCounters counters,
            List<string> undeterminableHandles)
        {
            Moves = moves ?? new List<KeyValuePair<ObjectId, string>>();
            WhiteColorFixes = whiteColorFixes ?? new List<ObjectId>();
            Counters = counters ?? new UncadLayerCorrectionCounters();
            UndeterminableHandles = undeterminableHandles
                ?? new List<string>();
        }

        /// <summary>实体 → 法定图层名（仅真正需要移动的实体）。</summary>
        public IReadOnlyList<KeyValuePair<ObjectId, string>> Moves { get; }

        /// <summary>3.8.0.10：法定图层上颜色是真彩白/255 号白的实体（归随层）。</summary>
        public IReadOnlyList<ObjectId> WhiteColorFixes { get; }
        public UncadLayerCorrectionCounters Counters { get; }
        public IReadOnlyList<string> UndeterminableHandles { get; }
        public bool IsEmpty => Counters.IsEmpty;
    }

    /// <summary>
    /// 3.3.0（D-054 特性①）：图层规范服务——确保 U_CABLE/U_TRAY/U_CONDUIT/
    /// U_HOSE 图层存在（缺则建，色号 7/1/4/3），识别插件实体并移正图层。
    /// 识别依据（本仓库哲学：不猜测）：
    /// - 电缆线 = 扫描器（QuickLineCadService.Scan）认出的 U1L 线段实体；
    ///   毫米标 = QuickLineMetadataStore 关联或扫描器匹配到的标注文本 → U_CABLE；
    /// - 带 ParallelAnnotationMetadata 的输出：KIND=U1Q → U_TRAY、KIND=U1C →
    ///   U_CONDUIT、KIND=U1B → U_HOSE（3.3.3，D-054 Amendment；KIND 自
    ///   D-018/D-050 元数据引入起恒写入；KIND 既非 U1Q/U1C/U1B 的输出不移动、
    ///   点名「无法判定」）。无元数据的旧输出无法识别为插件实体——同样
    ///   不移动、不猜测（D-054）。
    /// U1L 今后新生成的线+毫米标直接落在 U_CABLE（UnlFeature/U1LX 写入路径
    /// 调 EnsureLayer）；U1B 输出（轮廓/引线/文字）直接落在 U_HOSE。
    /// 矫正动作放进 U1U/U1F 的既有写事务（一次 U 可回退），
    /// 开关 UNC_FILL_LAYER_GOVERNOR 默认开。
    /// </summary>
    public static class LayerGovernorService
    {
        /// <summary>确保一个法定图层存在（缺则按规范色号创建）；返回其 ObjectId。</summary>
        public static ObjectId EnsureLayer(CadContext ctx, Transaction transaction,
            string layerName)
        {
            if (ctx == null || transaction == null
                || string.IsNullOrWhiteSpace(layerName))
                return ObjectId.Null;
            short colorIndex = UncadLayerStandard.ColorIndexOf(layerName);
            try
            {
                var layerTable = transaction.GetObject(ctx.Db.LayerTableId,
                    OpenMode.ForRead, false) as LayerTable;
                if (layerTable == null) return ObjectId.Null;
                if (layerTable.Has(layerName))
                    return layerTable[layerName];
                layerTable.UpgradeOpen();
                var record = new LayerTableRecord
                {
                    Name = layerName,
                    Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex)
                };
                layerTable.Add(record);
                transaction.AddNewlyCreatedDBObject(record, true);
                return record.Id;
            }
            catch
            {
                // 建层失败（受限图纸）时返回 Null——调用方按当前图层兜底，
                // 不让图层规范阻断绘制。
                return ObjectId.Null;
            }
        }

        /// <summary>确保全部法定图层存在（建层只做一次幂等检查）。</summary>
        public static bool EnsureGovernedLayers(CadContext ctx,
            Transaction transaction)
        {
            bool all = true;
            foreach (string name in UncadLayerStandard.GovernedLayerNames)
                if (EnsureLayer(ctx, transaction, name).IsNull) all = false;
            return all;
        }

        /// <summary>
        /// 构建矫正计划（读事务里调用）。scanned = QuickLineCadService.Scan 的
        /// 结果（电缆线+匹配到的毫米标）；outputs = ParallelAnnotationOwnership
        /// .Collect 的归属（特性④）；scopeEntityIds = 本次选中的图框实体集——
        /// 只动范围内的实体，绝不碰图框外的图形。
        /// </summary>
        public static LayerGovernorPlan Plan(CadContext ctx, Transaction transaction,
            IReadOnlyList<QuickLineCadSegment> scanned,
            IReadOnlyList<ParallelAnnotationOwnershipEntry> outputs,
            ICollection<ObjectId> scopeEntityIds)
        {
            var counters = new UncadLayerCorrectionCounters();
            var undeterminable = new List<string>();
            var moves = new List<KeyValuePair<ObjectId, string>>();
            var whiteFixes = new List<ObjectId>();
            var planned = new HashSet<ObjectId>();
            var plannedWhite = new HashSet<ObjectId>();
            if (ctx == null || transaction == null) 
                return new LayerGovernorPlan(moves, whiteFixes, counters, undeterminable);
            bool scoped = scopeEntityIds != null;

            // ① 平行标注输出（先于电缆：输出实体与线段实体不重叠，但输出
            // 标注文本若被几何匹配进扫描，输出归属优先）。
            foreach (ParallelAnnotationOwnershipEntry entry in outputs
                ?? Array.Empty<ParallelAnnotationOwnershipEntry>())
            {
                if (entry == null || entry.EntityId.IsNull) continue;
                if (scoped && !scopeEntityIds.Contains(entry.EntityId)) continue;
                string layer = UncadLayerStandard.TryResolveAnnotationLayer(
                    entry.Kind);
                if (layer == null)
                {
                    counters.UndeterminableEntities++;
                    undeterminable.Add(entry.EntityId.Handle.ToString());
                    continue;
                }
                if (TryPlanMove(ctx, transaction, entry.EntityId, layer,
                        planned, moves))
                {
                    if (string.Equals(layer, UncadLayerStandard.TrayLayerName,
                            StringComparison.OrdinalIgnoreCase))
                        counters.TrayEntities++;
                    else if (string.Equals(layer, UncadLayerStandard.HoseLayerName,
                            StringComparison.OrdinalIgnoreCase))
                        counters.HoseEntities++;
                    else
                        counters.ConduitEntities++;
                }
                PlanWhiteFix(transaction, entry.EntityId, plannedWhite,
                    whiteFixes, counters);
            }

            // ② 电缆路由：扫描器认出的线段 + 关联/匹配到的毫米标。
            foreach (QuickLineCadSegment segment in scanned
                ?? Array.Empty<QuickLineCadSegment>())
            {
                if (segment == null || segment.LineId.IsNull) continue;
                // 3.8.0.1：线性标注代替短线时，标注实体不是电缆线——不把
                // DIMENSION 挪到 U_CABLE 图层。
                if (segment.IsDimension) continue;
                if (scoped && !scopeEntityIds.Contains(segment.LineId)) continue;
                if (TryPlanMove(ctx, transaction, segment.LineId,
                        UncadLayerStandard.CableLayerName, planned, moves))
                    counters.CableEntities++;
                PlanWhiteFix(transaction, segment.LineId, plannedWhite,
                    whiteFixes, counters);
                if (!segment.LabelId.IsNull && segment.LabelId.IsValid
                    && (!scoped || scopeEntityIds.Contains(segment.LabelId)))
                {
                    if (TryPlanMove(ctx, transaction, segment.LabelId,
                            UncadLayerStandard.CableLayerName, planned, moves))
                        counters.CableEntities++;
                    PlanWhiteFix(transaction, segment.LabelId, plannedWhite,
                        whiteFixes, counters);
                }
                foreach (ObjectId labelId in segment.RestoredLabelIds
                    ?? Array.Empty<ObjectId>())
                    if (!labelId.IsNull && labelId.IsValid
                        && (!scoped || scopeEntityIds.Contains(labelId)))
                    {
                        if (TryPlanMove(ctx, transaction, labelId,
                                UncadLayerStandard.CableLayerName, planned, moves))
                            counters.CableEntities++;
                        PlanWhiteFix(transaction, labelId, plannedWhite,
                            whiteFixes, counters);
                    }
            }
            return new LayerGovernorPlan(moves, whiteFixes, counters, undeterminable);
        }

        /// <summary>
        /// 3.8.0.10（D-054 Amendment）：实体自身颜色是真彩白/255 号白时登记为
        /// 「归随层」。只看法定图层上被本次矫正认领的实体（范围过滤已由调用方
        /// 完成）；ByLayer/ByBlock 与其它显式颜色一律不动。
        /// </summary>
        private static void PlanWhiteFix(Transaction transaction, ObjectId entityId,
            HashSet<ObjectId> planned, List<ObjectId> fixes,
            UncadLayerCorrectionCounters counters)
        {
            if (entityId.IsNull || !entityId.IsValid || !planned.Add(entityId))
                return;
            try
            {
                Entity entity = transaction.GetObject(entityId,
                    OpenMode.ForRead, false) as Entity;
                if (entity == null || entity.IsErased || !IsPlotInvisibleWhite(entity))
                    return;
                fixes.Add(entityId);
                counters.PlotWhiteEntities++;
            }
            catch
            {
                // 读不到颜色（外部参照/受限图纸）就不登记，不阻断其它矫正。
            }
        }

        /// <summary>只认实体自身的真彩白/255 号白；ACI 7 是自适应色，不算。</summary>
        private static bool IsPlotInvisibleWhite(Entity entity)
        {
            Color color = entity.Color;
            if (color == null) return false;
            if (color.ColorMethod == ColorMethod.ByAci)
                return PlotColorSafety.IsWhiteAci(color.ColorIndex);
            if (color.ColorMethod == ColorMethod.ByColor)
                return PlotColorSafety.IsTrueWhite(
                    (color.Red << 16) | (color.Green << 8) | color.Blue);
            return false;
        }

        /// <summary>
        /// 在既有写事务里执行矫正（先确保图层存在，再逐实体移层，最后把法定
        /// 图层上的真彩白归随层）。返回实际移动的实体数；单个实体失败不阻断
        /// 整体写入。
        /// </summary>
        public static int Apply(CadContext ctx, Transaction transaction,
            LayerGovernorPlan plan)
        {
            if (ctx == null || transaction == null || plan == null
                || (plan.Moves.Count == 0 && plan.WhiteColorFixes.Count == 0))
                return 0;
            var layerIds = new Dictionary<string, ObjectId>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string name in UncadLayerStandard.GovernedLayerNames)
            {
                ObjectId id = EnsureLayer(ctx, transaction, name);
                if (!id.IsNull) layerIds[name] = id;
            }
            int moved = 0;
            foreach (KeyValuePair<ObjectId, string> move in plan.Moves)
            {
                if (!layerIds.TryGetValue(move.Value, out ObjectId layerId))
                    continue;
                try
                {
                    Entity entity = transaction.GetObject(move.Key,
                        OpenMode.ForWrite, false) as Entity;
                    if (entity == null || entity.IsErased) continue;
                    entity.LayerId = layerId;
                    moved++;
                }
                catch
                {
                    // 单个实体移层失败（被锁定的外部参照等）不阻断填充写入。
                }
            }
            foreach (ObjectId entityId in plan.WhiteColorFixes)
            {
                try
                {
                    Entity entity = transaction.GetObject(entityId,
                        OpenMode.ForWrite, false) as Entity;
                    if (entity == null || entity.IsErased
                        || !IsPlotInvisibleWhite(entity)) continue;
                    entity.ColorIndex = 256;   // ByLayer：屏幕颜色不变，白纸出图变黑
                }
                catch
                {
                    // 单个实体改色失败不阻断填充写入。
                }
            }
            return moved;
        }

        /// <summary>图层已正确则不进计划；实体读不到/图层未知时按需要移动处理。</summary>
        private static bool TryPlanMove(CadContext ctx, Transaction transaction,
            ObjectId entityId, string targetLayer, HashSet<ObjectId> planned,
            List<KeyValuePair<ObjectId, string>> moves)
        {
            if (entityId.IsNull || !entityId.IsValid || !planned.Add(entityId))
                return false;
            try
            {
                Entity entity = transaction.GetObject(entityId,
                    OpenMode.ForRead, false) as Entity;
                if (entity == null || entity.IsErased) return false;
                if (string.Equals(entity.Layer, targetLayer,
                        StringComparison.OrdinalIgnoreCase)) return false;
                moves.Add(new KeyValuePair<ObjectId, string>(entityId, targetLayer));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
