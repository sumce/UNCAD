using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad.QuickLine;

namespace UNCAD.Cad
{
    /// <summary>
    /// 3.3.0（D-054 特性④）：一条 U1Q/U1C 平行标注输出实体的归属信息——
    /// KIND（U1Q/U1C）+ 宿主电缆段句柄集（SOURCE 首成员 + RUN 全部成员，
    /// D-050；单段标注只有 SOURCE）。按图框归组由调用方把 scope 限制在该框
    /// 实体集内实现。
    /// </summary>
    public sealed class ParallelAnnotationOwnershipEntry
    {
        public ObjectId EntityId { get; set; }
        /// <summary>元数据 KIND（「U1Q」=桥架、「U1C」=线管；其它=无法判定）。</summary>
        public string Kind { get; set; } = "";
        /// <summary>SOURCE（首成员）句柄。</summary>
        public string SourceHandle { get; set; } = "";
        /// <summary>全部宿主句柄（SOURCE + RUN 去重，顺序稳定）。</summary>
        public List<string> HostHandles { get; } = new List<string>();
    }

    /// <summary>
    /// 3.3.0（D-054 特性④）：归属识别 API——收集当前空间（或给定实体范围）内
    /// 带 ParallelAnnotationMetadata 的输出，并解析到扫描所得的宿主电缆段。
    /// 消费方：图层矫正（特性①按 KIND 移层）、后续叠加展示（本期不画）。
    /// </summary>
    public static class ParallelAnnotationOwnership
    {
        /// <summary>
        /// 收集输出实体。scopeEntityIds 非空时只收范围内的实体（按图框归组），
        /// 为 null 时收当前空间全部输出。读失败/无元数据的实体被跳过。
        /// </summary>
        public static List<ParallelAnnotationOwnershipEntry> Collect(
            CadContext ctx, Transaction transaction,
            ICollection<ObjectId> scopeEntityIds = null)
        {
            var result = new List<ParallelAnnotationOwnershipEntry>();
            if (ctx == null || transaction == null) return result;
            BlockTableRecord space;
            try
            {
                space = transaction.GetObject(ctx.Db.CurrentSpaceId,
                    OpenMode.ForRead, false) as BlockTableRecord;
            }
            catch
            {
                return result;
            }
            if (space == null) return result;

            foreach (ObjectId id in space)
            {
                if (scopeEntityIds != null && !scopeEntityIds.Contains(id))
                    continue;
                if (!QuickLineCadService.IsCandidateClass(id)) continue;
                Entity entity;
                try
                {
                    entity = transaction.GetObject(id, OpenMode.ForRead, true)
                        as Entity;
                }
                catch { continue; }
                if (entity == null || entity.IsErased) continue;
                try
                {
                    if (!ParallelAnnotationMetadata.TryRead(entity,
                            out string kind, out string sourceHandle,
                            out string runHandles)) continue;
                    var entry = new ParallelAnnotationOwnershipEntry
                    {
                        EntityId = id,
                        Kind = kind,
                        SourceHandle = sourceHandle ?? ""
                    };
                    AddHandle(entry.HostHandles, entry.SourceHandle);
                    foreach (string runHandle in runHandles
                        .Split(new[] { ParallelAnnotationMetadata.RunHandleSeparator },
                            StringSplitOptions.RemoveEmptyEntries))
                        AddHandle(entry.HostHandles, runHandle);
                    result.Add(entry);
                }
                catch { continue; }
            }
            return result;
        }

        /// <summary>
        /// 输出 → 宿主电缆段映射：宿主句柄在 scanned 里能找到的段（找不到的
        /// 句柄——源线已删/别的图纸——直接跳过，不猜测）。
        /// </summary>
        public static Dictionary<string, List<QuickLineCadSegment>> MapHostSegments(
            IReadOnlyList<ParallelAnnotationOwnershipEntry> entries,
            IReadOnlyList<QuickLineCadSegment> scanned)
        {
            var segmentsByHandle = new Dictionary<string, QuickLineCadSegment>(
                StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineCadSegment segment in scanned ?? Array.Empty<QuickLineCadSegment>())
            {
                if (segment == null) continue;
                string handle = segment.LineId.Handle.ToString();
                if (!segmentsByHandle.ContainsKey(handle))
                    segmentsByHandle.Add(handle, segment);
            }
            var result = new Dictionary<string, List<QuickLineCadSegment>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (ParallelAnnotationOwnershipEntry entry in entries ?? Array.Empty<ParallelAnnotationOwnershipEntry>())
            {
                if (entry == null) continue;
                var hosts = new List<QuickLineCadSegment>();
                foreach (string handle in entry.HostHandles)
                    if (segmentsByHandle.TryGetValue(handle,
                            out QuickLineCadSegment segment))
                        hosts.Add(segment);
                result[entry.EntityId.Handle.ToString()] = hosts;
            }
            return result;
        }

        private static void AddHandle(List<string> target, string handle)
        {
            string value = (handle ?? "").Trim();
            if (value.Length == 0) return;
            if (!target.Contains(value, StringComparer.OrdinalIgnoreCase))
                target.Add(value);
        }
    }
}
