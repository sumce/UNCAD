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

        /// <summary>
        /// 读取 Downstream 块（精确名，含 XFree/Mangle 后缀）的可见性状态。
        ///
        /// 传进来的 id 是图框里的下游/设备端块集合（与 Device 状态读取同一来源：
        /// FillSelectionCollector 把名字以 downstream 开头的块一并归入 DeviceBlockIds）。
        /// 状态未知时返回 false，调用方不得据此删除任何既有行。
        /// </summary>
        public static bool TryReadDownstreamSocketBox(CadContext ctx, ObjectId[] blockIds,
            out bool socketBox, out bool stateKnown, out string stateText, out int blockCount)
        {
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                bool result = TryReadDownstreamSocketBox(transaction, blockIds, out socketBox,
                    out stateKnown, out stateText, out blockCount);
                transaction.Commit();
                return result;
            }
        }

        internal static bool TryReadDownstreamSocketBox(Transaction transaction,
            ObjectId[] blockIds, out bool socketBox, out bool stateKnown, out string stateText,
            out int blockCount)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            socketBox = false;
            stateKnown = false;
            blockCount = 0;
            stateText = "未找到 Downstream 块（状态未知）";
            var states = new List<KeyValuePair<string, bool>>();
            bool unknown = false;
            foreach (ObjectId id in blockIds ?? new ObjectId[0])
            {
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null || !DynamicBlockStatePolicy.IsDownstreamBlock(
                    EffectiveName(transaction, block))) continue;
                blockCount++;
                if (!block.IsDynamicBlock)
                {
                    unknown = true;
                    stateText = "Downstream 不是动态块（状态未知）";
                    continue;
                }
                bool found = false;
                try
                {
                    foreach (DynamicBlockReferenceProperty property
                        in block.DynamicBlockReferencePropertyCollection)
                    {
                        string value = Convert.ToString(property.Value) ?? "";
                        if (!DynamicBlockStatePolicy.TryClassifyDownstreamState(value,
                            out bool isSocketBox)) continue;
                        states.Add(new KeyValuePair<string, bool>(value, isSocketBox));
                        found = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    unknown = true;
                    stateText = "Downstream 状态读取失败（状态未知）";
                    Log.Warn("读取 Downstream 状态失败: " + ex.Message);
                }
                if (!found)
                {
                    unknown = true;
                    stateText = "Downstream 未找到 I-line_Panel/socket box/busbar connector 状态（状态未知）";
                }
            }
            if (states.Count > 0 && states.Any(state => state.Value != states[0].Value))
                throw new InvalidOperationException("同一图框内 Downstream 状态冲突："
                    + string.Join("、", states.Select(state => state.Key)) + "。图纸未修改。");
            if (states.Count == 0 || unknown)
            {
                if (states.Count > 0)
                    stateText = string.Join("、", states.Select(state => state.Key).Distinct())
                        + "（部分状态未知）";
                return false;
            }
            socketBox = states[0].Value;
            stateKnown = true;
            stateText = string.Join("、", states.Select(state => state.Key).Distinct())
                + (socketBox ? "（Socket Box）" : "");
            return socketBox;
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
                        || DynamicBlockStatePolicy.IsUpstreamBlock(normalized)
                        || DynamicBlockStatePolicy.IsDownstreamBlock(normalized)) return name;
                }
                catch { }
            }
            return fallback;
        }
    }
}
