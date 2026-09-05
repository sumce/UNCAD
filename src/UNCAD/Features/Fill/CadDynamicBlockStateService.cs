using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    internal static class CadDynamicBlockStateService
    {
        public static bool ReadDeviceHasOutlet(CadContext ctx, ObjectId[] blockIds,
            out string stateText)
        {
            return TryReadDeviceHasOutlet(ctx, blockIds, out bool hasOutlet,
                out _, out stateText) && hasOutlet;
        }

        /// <summary>
        /// Reads the Device_Build20260716 outlet state without treating a missing or
        /// incompatible block as "equipment".  The latter is important for U1U:
        /// an unknown state must preserve an existing outlet row rather than delete it.
        /// </summary>
        public static bool TryReadDeviceHasOutlet(CadContext ctx, ObjectId[] blockIds,
            out bool hasOutlet, out bool stateKnown, out string stateText)
        {
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                bool result = TryReadDeviceHasOutlet(transaction, blockIds, out hasOutlet,
                    out stateKnown, out stateText);
                transaction.Commit();
                return result;
            }
        }

        internal static bool TryReadDeviceHasOutlet(Transaction transaction,
            ObjectId[] blockIds, out bool hasOutlet, out bool stateKnown, out string stateText)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            hasOutlet = false;
            stateKnown = false;
            stateText = "未找到 Device_Build20260716（状态未知）";
            var states = new List<KeyValuePair<string, bool>>();
            bool unknown = false;
            foreach (ObjectId id in blockIds ?? new ObjectId[0])
            {
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null || !DynamicBlockStatePolicy.IsDeviceBlock(
                    EffectiveName(transaction, block))) continue;
                if (!block.IsDynamicBlock)
                {
                    unknown = true;
                    stateText = "Device_Build20260716 不是动态块（状态未知）";
                    continue;
                }
                bool found = false;
                try
                {
                    foreach (DynamicBlockReferenceProperty property
                        in block.DynamicBlockReferencePropertyCollection)
                    {
                        string value = Convert.ToString(property.Value) ?? "";
                        if (!DynamicBlockStatePolicy.TryClassifyDeviceState(value,
                            out bool outlet)) continue;
                        states.Add(new KeyValuePair<string, bool>(value, outlet));
                        found = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    unknown = true;
                    stateText = "Device_Build20260716 状态读取失败（状态未知）";
                    Log.Warn("读取 Device_Build20260716 状态失败: " + ex.Message);
                }
                if (!found)
                {
                    unknown = true;
                    stateText = "Device_Build20260716 未找到设备/插座状态（状态未知）";
                }
            }
            if (states.Count > 0 && states.Any(state => state.Value != states[0].Value))
                throw new InvalidOperationException("同一图框内 Device_Build20260716 状态冲突："
                    + string.Join("、", states.Select(state => state.Key)) + "。图纸未修改。");
            if (states.Count == 0 || unknown)
            {
                if (states.Count > 0)
                    stateText = string.Join("、", states.Select(state => state.Key).Distinct())
                        + "（部分状态未知）";
                return false;
            }
            hasOutlet = states[0].Value;
            stateKnown = true;
            stateText = string.Join("、", states.Select(state => state.Key).Distinct());
            return hasOutlet;
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
            catch (Exception ex)
            {
                // 允许值读取失败时该动态块状态将按"无此状态"处理，直接影响
                // 填充结果，必须留痕。
                Log.Warn("动态块允许值读取失败: " + ex.Message);
            }
            return null;
        }

        private static string EffectiveName(Transaction transaction, BlockReference block)
        {
            string fallback = "";
            foreach (ObjectId recordId in FrameRegionCollector.DefinitionIds(block))
            {
                try
                {
                    BlockTableRecord record = transaction.GetObject(recordId,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    string name = record?.Name ?? "";
                    if (fallback.Length == 0) fallback = name;
                    string normalized = BlockNameNormalizer.RemoveMangledSuffix(name);
                    if (DynamicBlockStatePolicy.IsDeviceBlock(normalized)
                        || DynamicBlockStatePolicy.IsUpstreamBlock(normalized)) return name;
                }
                catch { }
            }
            return fallback;
        }
    }
}
