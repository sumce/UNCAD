using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Colors;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    internal static class CadBlockColorWriter
    {
        internal static int Apply(Transaction transaction,
            IEnumerable<ObjectId> blockIds, short colorIndex)
            => Apply(transaction, blockIds, colorIndex, null);

        /// <summary>
        /// <paramref name="sharedNormalizedDefinitions"/> lets one batch share the
        /// "already normalized" set across frames: the same shared device/upstream
        /// block definition would otherwise be re-walked recursively for every
        /// frame in the batch.
        /// </summary>
        internal static int Apply(Transaction transaction,
            IEnumerable<ObjectId> blockIds, short colorIndex,
            ISet<ObjectId> sharedNormalizedDefinitions)
        {
            if (transaction == null || blockIds == null) return 0;
            var seen = new HashSet<ObjectId>();
            ISet<ObjectId> normalizedDefinitions = sharedNormalizedDefinitions
                ?? new HashSet<ObjectId>();
            int changedBlocks = 0;

            foreach (ObjectId id in blockIds)
            {
                if (id.IsNull || !seen.Add(id)) continue;
                BlockReference block;
                try
                {
                    block = transaction.GetObject(id, OpenMode.ForRead, true)
                        as BlockReference;
                }
                catch (System.Exception ex)
                {
                    Log.Warn("设备/上游块颜色读取失败，已跳过: " + ex.Message);
                    continue;
                }
                if (block == null) continue;

                bool changed = false;
                try { changed |= SetColor(block, colorIndex); }
                catch (System.Exception ex)
                {
                    Log.Warn("设备/上游块颜色写入失败，已保留原色: " + ex.Message);
                }
                try
                {
                    changed |= NormalizeDefinition(transaction, block.BlockTableRecord,
                        normalizedDefinitions);
                    if (block.IsDynamicBlock
                        && block.DynamicBlockTableRecord != block.BlockTableRecord)
                        changed |= NormalizeDefinition(transaction, block.DynamicBlockTableRecord,
                            normalizedDefinitions);
                }
                catch (System.Exception ex)
                {
                    Log.Warn("设备/上游块定义颜色更新失败，已跳过不可写实体: " + ex.Message);
                }
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    try
                    {
                        AttributeReference attribute = transaction.GetObject(attributeId,
                            OpenMode.ForRead, true) as AttributeReference;
                        if (attribute != null && SetColor(attribute, colorIndex)) changed = true;
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warn("设备/上游属性颜色写入失败，已保留原色: " + ex.Message);
                    }
                }

                if (!changed) continue;
                try
                {
                    if (!block.IsWriteEnabled) block.UpgradeOpen();
                    block.RecordGraphicsModified(true);
                    changedBlocks++;
                }
                catch (System.Exception ex)
                {
                    Log.Warn("设备/上游块刷新失败: " + ex.Message);
                }
            }

            return changedBlocks;
        }

        private static bool NormalizeDefinition(Transaction transaction,
            ObjectId definitionId, ISet<ObjectId> visited)
        {
            if (definitionId.IsNull || !definitionId.IsValid || !visited.Add(definitionId))
                return false;
            BlockTableRecord definition;
            try
            {
                definition = transaction.GetObject(definitionId, OpenMode.ForRead, true)
                    as BlockTableRecord;
            }
            catch (System.Exception ex)
            {
                Log.Warn("块定义不可读，跳过颜色更新: " + ex.Message);
                return false;
            }
            if (definition == null || definition.IsLayout
                || definition.IsFromExternalReference || definition.IsFromOverlayReference)
                return false;

            bool changed = false;
            foreach (ObjectId entityId in definition)
            {
                Entity entity;
                try { entity = transaction.GetObject(entityId, OpenMode.ForRead, true) as Entity; }
                catch (System.Exception ex)
                {
                    Log.Warn("块定义实体不可读，跳过颜色更新: " + ex.Message);
                    continue;
                }
                if (entity == null || entity.IsErased) continue;
                try
                {
                    changed |= SetColor(entity, 0); // ByBlock: inherit the selected reference color.
                }
                catch (System.Exception ex)
                {
                    Log.Warn("块定义实体颜色不可写，已保留原色: " + ex.Message);
                    continue;
                }
                if (!(entity is BlockReference nested)) continue;
                foreach (ObjectId attributeId in nested.AttributeCollection)
                {
                    try
                    {
                        AttributeReference attribute = transaction.GetObject(attributeId,
                            OpenMode.ForRead, true) as AttributeReference;
                        if (attribute != null) changed |= SetColor(attribute, 0);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warn("嵌套上游属性颜色写入失败，已保留原色: " + ex.Message);
                    }
                }
                changed |= NormalizeDefinition(transaction, nested.BlockTableRecord, visited);
                if (nested.IsDynamicBlock
                    && nested.DynamicBlockTableRecord != nested.BlockTableRecord)
                    changed |= NormalizeDefinition(transaction,
                        nested.DynamicBlockTableRecord, visited);
            }
            return changed;
        }

        private static bool SetColor(Entity entity, short colorIndex)
        {
            if (entity == null) return false;
            bool changed = entity.ColorIndex != colorIndex;
            if (!entity.IsWriteEnabled) entity.UpgradeOpen();
            // Assigning Color as well as ColorIndex clears a stale TrueColor/ByLayer
            // method that can otherwise survive an ACI-only update in dynamic blocks.
            Color target = colorIndex == 0
                ? Color.FromColorIndex(ColorMethod.ByBlock, 0)
                : Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            if (entity.Color == null || entity.Color.ColorMethod != target.ColorMethod
                || entity.ColorIndex != colorIndex)
            {
                entity.Color = target;
                changed = true;
            }
            return changed;
        }
    }
}
