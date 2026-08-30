using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;

namespace UNCAD.Features.Fill
{
    internal static class CadDynamicBlockStateService
    {
        public static bool ReadDeviceHasOutlet(CadContext ctx, ObjectId[] blockIds,
            out string stateText)
        {
            var states = new List<KeyValuePair<string, bool>>();
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in blockIds ?? new ObjectId[0])
                {
                    BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                        as BlockReference;
                    if (block == null || !DynamicBlockStatePolicy.IsDeviceBlock(
                        EffectiveName(transaction, block))) continue;
                    if (!block.IsDynamicBlock)
                        throw new InvalidOperationException("Device_Build20260716 不是动态块，无法读取插座状态。");
                    bool found = false;
                    foreach (DynamicBlockReferenceProperty property
                        in block.DynamicBlockReferencePropertyCollection)
                    {
                        string value = Convert.ToString(property.Value) ?? "";
                        if (!DynamicBlockStatePolicy.TryClassifyDeviceState(value,
                            out bool hasOutlet)) continue;
                        states.Add(new KeyValuePair<string, bool>(value, hasOutlet));
                        found = true;
                        break;
                    }
                    if (!found)
                        throw new InvalidOperationException("Device_Build20260716 未找到“设备/插座”动态状态。");
                }
                transaction.Commit();
            }
            if (states.Count == 0)
            {
                stateText = "未找到 Device_Build20260716（不输出插座）";
                return false;
            }
            bool decision = states[0].Value;
            if (states.Any(state => state.Value != decision))
                throw new InvalidOperationException("同一图框内 Device_Build20260716 状态冲突："
                    + string.Join("、", states.Select(state => state.Key)) + "。图纸未修改。");
            stateText = string.Join("、", states.Select(state => state.Key).Distinct());
            return decision;
        }

        public static FillWriteResult FillUpstreamState(CadContext ctx,
            Transaction transaction, ObjectId[] blockIds, string next)
        {
            string target = DynamicBlockStatePolicy.UpstreamVisibilityState(next);
            if (target.Length == 0 || blockIds == null || blockIds.Length == 0)
                return FillWriteResult.Empty;
            int blocks = 0, values = 0;
            foreach (ObjectId id in blockIds)
            {
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null || !DynamicBlockStatePolicy.IsUpstreamBlock(
                    EffectiveName(transaction, block))) continue;
                if (!block.IsDynamicBlock)
                    throw new InvalidOperationException("upstream 不是动态块，无法同步盘型状态。");
                bool changed = false;
                foreach (DynamicBlockReferenceProperty property
                    in block.DynamicBlockReferencePropertyCollection)
                {
                    if (property.ReadOnly) continue;
                    object allowed = AllowedValue(property, target);
                    if (allowed == null) continue;
                    try
                    {
                        block.UpgradeOpen();
                        property.Value = allowed;
                        values++;
                        changed = true;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("upstream 动态状态写入失败：" + target, ex);
                    }
                    break;
                }
                if (!changed)
                    throw new InvalidOperationException("upstream 块没有可用状态“" + target + "”。");
                blocks++;
            }
            return new FillWriteResult(blocks, values);
        }

        public static bool IsUpstreamBlock(Transaction transaction, BlockReference block)
            => block != null && DynamicBlockStatePolicy.IsUpstreamBlock(
                EffectiveName(transaction, block));

        private static object AllowedValue(DynamicBlockReferenceProperty property, string target)
        {
            try
            {
                foreach (object value in property.GetAllowedValues() ?? new object[0])
                    if (string.Equals(Convert.ToString(value), target,
                        StringComparison.OrdinalIgnoreCase)) return value;
            }
            catch { }
            return null;
        }

        private static string EffectiveName(Transaction transaction, BlockReference block)
        {
            ObjectId recordId = block.IsDynamicBlock
                ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            BlockTableRecord record = transaction.GetObject(recordId, OpenMode.ForRead, true)
                as BlockTableRecord;
            return record?.Name ?? "";
        }
    }
}
