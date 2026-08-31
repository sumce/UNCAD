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
            MachineRow selected, string bridgeInfo, CableStatResult statistics)
            => RunStandalone(ctx, transaction =>
                FillFrame(ctx, transaction, blockIds, selected, bridgeInfo, statistics));

        internal static FillWriteResult FillFrame(CadContext ctx, Transaction transaction,
            ObjectId[] blockIds, MachineRow selected, string bridgeInfo,
            CableStatResult statistics)
            => FillFrame(ctx, transaction, blockIds, selected, bridgeInfo,
                statistics, false);

        internal static FillWriteResult FillFrame(CadContext ctx, Transaction transaction,
            ObjectId[] blockIds, MachineRow selected, string bridgeInfo,
            CableStatResult statistics, bool preserveMissingStatistics)
            => FillFrame(ctx, transaction, blockIds, selected, bridgeInfo, statistics,
                preserveMissingStatistics, preserveMissingStatistics,
                preserveMissingStatistics);

        internal static FillWriteResult FillFrame(CadContext ctx, Transaction transaction,
            ObjectId[] blockIds, MachineRow selected, string bridgeInfo,
            CableStatResult statistics, bool preserveCable, bool preserveBridge,
            bool preserveConduit)
        {
            if (blockIds == null || blockIds.Length == 0) return FillWriteResult.Empty;
            Dictionary<string, string> values = FrameBlockFiller.BuildValues(
                selected, bridgeInfo, statistics, preserveCable, preserveBridge,
                preserveConduit);
            if (values.Count == 0) return FillWriteResult.Empty;

            int blocks = 0, attributes = 0;
            foreach (ObjectId id in blockIds)
            {
                var block = transaction.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                if (block == null || block.AttributeCollection == null
                    || block.AttributeCollection.Count == 0) continue;
                bool touched = false;
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    var attribute = transaction.GetObject(attributeId, OpenMode.ForRead)
                        as AttributeReference;
                    if (attribute == null
                        || !values.TryGetValue(attribute.Tag, out string newValue)) continue;
                    SetAttributeValue(ctx, attribute, newValue, attribute.IsMTextAttribute);
                    attributes++;
                    touched = true;
                }
                if (touched) blocks++;
            }
            return new FillWriteResult(blocks, attributes);
        }

        private static void SetAttributeValue(CadContext ctx, AttributeReference attribute,
            string value, bool multiline)
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
