using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;

namespace UNCAD.Features.Fill
{
    internal readonly struct FillWriteResult
    {
        public FillWriteResult(int blocks, int values)
        {
            Blocks = blocks;
            Values = values;
        }

        public int Blocks { get; }
        public int Values { get; }
        public static FillWriteResult Empty => new FillWriteResult(0, 0);
    }

    internal static class CadBlockAttributeWriter
    {
        /// <summary>
        /// CABLE_INFO 已改为多行属性：文本箱宽度固定 78300 图形单位，超过自动换行。
        /// 这是图框版式要求（每行最大 78300），不要在写入时把宽度重置为 0。
        /// </summary>
        internal const double CableInfoMTextWidth = 78300d;

        /// <summary>读取块实例上指定标签的属性文本（第一个命中；没有返回 null）。
        /// U1U 设备内预留恢复用：从图框现有 CABLE_INFO 文本取「数值（设备内）」。</summary>
        internal static string ReadFirstTagged(Transaction transaction,
            ObjectId[] blockIds, string tag)
        {
            foreach (ObjectId id in blockIds ?? Array.Empty<ObjectId>())
            {
                if (id.IsNull) continue;
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block?.AttributeCollection == null) continue;
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    AttributeReference attribute = transaction.GetObject(attributeId,
                        OpenMode.ForRead, true) as AttributeReference;
                    if (attribute != null
                        && string.Equals(attribute.Tag, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        return attribute.TextString;
                    }
                }
            }
            return null;
        }

        /// <summary>开只读事务读取属性文本（单框路径用，照 RunStandalone 惯例）。</summary>
        internal static string ReadTagged(CadContext ctx, ObjectId[] blockIds, string tag)
        {
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                string result = ReadFirstTagged(transaction, blockIds, tag);
                transaction.Commit();
                return result;
            }
        }

        public static FillWriteResult FillDeviceName(CadContext ctx, ObjectId[] blockIds,
            string circuitName)
            => RunStandalone(ctx, transaction =>
                FillDeviceName(ctx, transaction, blockIds, circuitName));

        internal static FillWriteResult FillDeviceName(CadContext ctx, Transaction transaction,
            ObjectId[] blockIds, string circuitName)
        {
            string value = (circuitName ?? "").Trim();
            if (blockIds == null || blockIds.Length == 0 || value.Length == 0)
                return FillWriteResult.Empty;

            int blocks = 0, values = 0;
            foreach (ObjectId id in blockIds)
            {
                var block = transaction.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                if (block == null) continue;
                bool touched = false;
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    var attribute = transaction.GetObject(attributeId, OpenMode.ForRead, true)
                        as AttributeReference;
                    if (attribute == null || !string.Equals(attribute.Tag,
                        DeviceBlockFiller.TagDeviceName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    SetAttributeValue(ctx, attribute, value, false);
                    values++;
                    touched = true;
                }

                if (!touched && block.IsDynamicBlock)
                {
                    foreach (DynamicBlockReferenceProperty property
                        in block.DynamicBlockReferencePropertyCollection)
                    {
                        if (!string.Equals(property.PropertyName,
                            DeviceBlockFiller.TagDeviceName, StringComparison.OrdinalIgnoreCase)
                            || property.ReadOnly) continue;
                        try
                        {
                            block.UpgradeOpen();
                            property.Value = value;
                            values++;
                            touched = true;
                        }
                        catch (Exception ex)
                        {
                            // 动态属性属于同一次填充事务；失败必须向上传播，让表格和全部块整体回滚。
                            throw new InvalidOperationException(
                                "设备块动态属性写入失败，已取消本次全部填充: "
                                + property.PropertyName, ex);
                        }
                        break;
                    }
                }
                if (touched) blocks++;
            }
            return new FillWriteResult(blocks, values);
        }

        public static FillWriteResult FillTagged(CadContext ctx, ObjectId[] blockIds,
            string tag, string value, bool multiline)
            => RunStandalone(ctx, transaction =>
                FillTagged(ctx, transaction, blockIds, tag, value, multiline));

        internal static FillWriteResult FillTagged(CadContext ctx, Transaction transaction,
            ObjectId[] blockIds, string tag, string value, bool multiline)
        {
            if (blockIds == null || blockIds.Length == 0) return FillWriteResult.Empty;
            int blocks = 0, attributes = 0;
            foreach (ObjectId id in blockIds)
            {
                var block = transaction.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                if (block == null) continue;
                bool touched = false;
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    var attribute = transaction.GetObject(attributeId, OpenMode.ForRead, true)
                        as AttributeReference;
                    if (attribute == null || !string.Equals(attribute.Tag, tag,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    SetAttributeValue(ctx, attribute, value, multiline);
                    attributes++;
                    touched = true;
                }
                if (touched) blocks++;
            }
            return new FillWriteResult(blocks, attributes);
        }

        public static FillWriteResult FillFrame(CadContext ctx, ObjectId[] blockIds,
            MachineRow selected, CableStatResult statistics)
            => RunStandalone(ctx, transaction =>
                FillFrame(ctx, transaction, blockIds, selected, statistics));

        internal static FillWriteResult FillFrame(CadContext ctx, Transaction transaction,
            ObjectId[] blockIds, MachineRow selected, CableStatResult statistics)
            => FillFrame(ctx, transaction, blockIds, selected, statistics, false);

        internal static FillWriteResult FillFrame(CadContext ctx, Transaction transaction,
            ObjectId[] blockIds, MachineRow selected, CableStatResult statistics,
            bool preserveMissingStatistics)
            => FillFrame(ctx, transaction, blockIds, selected, statistics,
                preserveMissingStatistics, preserveMissingStatistics);

        internal static FillWriteResult FillFrame(CadContext ctx, Transaction transaction,
            ObjectId[] blockIds, MachineRow selected, CableStatResult statistics,
            bool preserveCable, bool preserveConduit)
            => FillFrame(ctx, transaction, blockIds, selected, statistics,
                preserveCable, preserveConduit, preserveCable || preserveConduit,
                preserveCable || preserveConduit);

        internal static FillWriteResult FillFrame(CadContext ctx, Transaction transaction,
            ObjectId[] blockIds, MachineRow selected, CableStatResult statistics,
            bool preserveCable, bool preserveConduit, bool preserveBridgeInfo2,
            bool preserveConduitInfo2)
        {
            if (blockIds == null || blockIds.Length == 0) return FillWriteResult.Empty;

            int blocks = 0, attributes = 0;
            foreach (ObjectId id in blockIds)
            {
                var block = transaction.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                if (block == null || block.AttributeCollection == null
                    || block.AttributeCollection.Count == 0) continue;
                string existingInfo2 = null;
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    var attribute = transaction.GetObject(attributeId, OpenMode.ForRead, true)
                        as AttributeReference;
                    if (attribute != null && string.Equals(attribute.Tag,
                        FrameBlockFiller.TagInfo2, StringComparison.OrdinalIgnoreCase))
                    {
                        existingInfo2 = attribute.TextString ?? "";
                        break;
                    }
                }
                Dictionary<string, string> values = FrameBlockFiller.BuildValues(
                    selected, statistics, preserveCable, preserveConduit,
                    preserveBridgeInfo2, preserveConduitInfo2, existingInfo2);
                if (values.Count == 0) continue;

                bool touched = false;
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    var attribute = transaction.GetObject(attributeId, OpenMode.ForRead)
                        as AttributeReference;
                    if (attribute == null
                        || !values.TryGetValue(attribute.Tag, out string newValue)) continue;
                    bool cableInfo = string.Equals(attribute.Tag, FrameBlockFiller.TagCable,
                        StringComparison.OrdinalIgnoreCase);
                    SetAttributeValue(ctx, attribute, newValue, attribute.IsMTextAttribute,
                        cableInfo ? CableInfoMTextWidth : 0d);
                    attributes++;
                    touched = true;
                }
                if (touched) blocks++;
            }
            return new FillWriteResult(blocks, attributes);
        }

        /// <summary>
        /// U1F/U1U 定义复制来的图框时，先把源框带来的旧信息显式清空
        /// （2026-11-06 用户要求“U1F 定义要把信息都清理掉”）：FRAMEINFO JSON
        /// 载荷、CABLE_INFO、CONDUIT_INFO、INFO2——随后正常写入新机台数据。清空与
        /// 写入同一事务：整体提交或整体回滚，不会出现半清空状态。
        /// 返回清空的属性数（日志用）。
        /// </summary>
        internal static int ClearStaleInfo(Transaction transaction, ObjectId[] frameBlockIds)
        {
            if (frameBlockIds == null || frameBlockIds.Length == 0) return 0;
            int cleared = 0;
            foreach (ObjectId id in frameBlockIds)
            {
                if (id.IsNull) continue;
                var block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null) continue;
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    var attribute = transaction.GetObject(attributeId, OpenMode.ForRead)
                        as AttributeReference;
                    if (attribute == null) continue;
                    string tag = attribute.Tag ?? "";
                    bool stale = string.Equals(tag, FrameBlockFiller.TagCable,
                        StringComparison.OrdinalIgnoreCase)
                        || string.Equals(tag, FrameBlockFiller.TagConduit,
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(tag, FrameBlockFiller.TagInfo2,
                            StringComparison.OrdinalIgnoreCase)
                        || FrameInfoJsonBlockWriter.IsJsonTagName(tag);
                    if (!stale) continue;
                    attribute.UpgradeOpen();
                    attribute.TextString = "";
                    cleared++;
                }
            }
            if (cleared > 0)
                UNCAD.Infra.Log.Info("复制图框清理：已清空 " + cleared + " 个旧信息属性"
                    + "（CABLE_INFO/CONDUIT_INFO/FRAMEINFO），随后写入新机台数据。");
            return cleared;
        }

        private static void SetAttributeValue(CadContext ctx, AttributeReference attribute,
            string value, bool multiline, double mtextWidth = 0d)
        {
            string text = value ?? "";
            attribute.UpgradeOpen();
            attribute.TextString = text;
            if (multiline && attribute.IsMTextAttribute)
            {
                using (MText mtext = attribute.MTextAttribute)
                {
                    if (mtext != null)
                    {
                        // Width > 0 才会按文本框自动换行；CABLE_INFO 固定用 78300。
                        if (mtextWidth > 0d) mtext.Width = mtextWidth;
                        mtext.Contents = text;
                        attribute.MTextAttribute = mtext;
                    }
                }
                attribute.UpdateMTextAttribute();
            }
            attribute.AdjustAlignment(ctx.Db);
        }

        private static FillWriteResult RunStandalone(CadContext ctx,
            Func<Transaction, FillWriteResult> write)
        {
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                FillWriteResult result = write(transaction);
                transaction.Commit();
                return result;
            }
        }
    }
}
