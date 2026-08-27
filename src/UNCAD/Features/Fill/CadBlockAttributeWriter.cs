using System;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Infra;

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
        {
            string value = (circuitName ?? "").Trim();
            if (blockIds == null || blockIds.Length == 0 || value.Length == 0)
                return FillWriteResult.Empty;

            int blocks = 0, values = 0;
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in blockIds)
                {
                    var block = tr.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                    if (block == null) continue;
                    bool touched = false;
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true)
                            as AttributeReference;
                        if (attribute == null || !string.Equals(attribute.Tag,
                            DeviceBlockFiller.TagDeviceName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        attribute.UpgradeOpen();
                        attribute.TextString = value;
                        attribute.AdjustAlignment(ctx.Db);
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
                            catch (System.Exception ex)
                            {
                                Log.Warn("UNC_FILL DEVICENAME dynamic property failed: " + ex.Message);
                            }
                            break;
                        }
                    }
                    if (touched) blocks++;
                }
                tr.Commit();
            }
            return new FillWriteResult(blocks, values);
        }

        public static FillWriteResult FillTagged(CadContext ctx, ObjectId[] blockIds,
            string tag, string value, bool multiline)
        {
            if (blockIds == null || blockIds.Length == 0) return FillWriteResult.Empty;
            int blocks = 0, attributes = 0;
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in blockIds)
                {
                    var block = tr.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                    if (block == null) continue;
                    bool touched = false;
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true)
                            as AttributeReference;
                        if (attribute == null || !string.Equals(attribute.Tag, tag,
                            StringComparison.OrdinalIgnoreCase)) continue;

                        attribute.UpgradeOpen();
                        attribute.TextString = value ?? "";
                        if (multiline && attribute.IsMTextAttribute)
                        {
                            using (MText mtext = attribute.MTextAttribute)
                            {
                                if (mtext != null)
                                {
                                    mtext.Contents = value ?? "";
                                    attribute.MTextAttribute = mtext;
                                }
                            }
                            attribute.UpdateMTextAttribute();
                        }
                        attribute.AdjustAlignment(ctx.Db);
                        attributes++;
                        touched = true;
                    }
                    if (touched) blocks++;
                }
                tr.Commit();
            }
            return new FillWriteResult(blocks, attributes);
        }

        public static FillWriteResult FillFrame(CadContext ctx, ObjectId[] blockIds,
            MachineRow selected, string bridgeInfo, CableStatResult statistics)
        {
            if (blockIds == null || blockIds.Length == 0) return FillWriteResult.Empty;
            var values = FrameBlockFiller.BuildValues(selected, bridgeInfo, statistics);
            if (values.Count == 0) return FillWriteResult.Empty;

            int blocks = 0, attributes = 0;
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in blockIds)
                {
                    var block = tr.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                    if (block == null || block.AttributeCollection == null
                        || block.AttributeCollection.Count == 0) continue;
                    bool touched = false;
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        var attribute = tr.GetObject(attributeId, OpenMode.ForRead)
                            as AttributeReference;
                        if (attribute == null
                            || !values.TryGetValue(attribute.Tag, out string newValue)) continue;
                        attribute.UpgradeOpen();
                        attribute.TextString = newValue;
                        if (attribute.IsMTextAttribute)
                        {
                            using (MText mtext = attribute.MTextAttribute)
                            {
                                if (mtext != null)
                                {
                                    mtext.Contents = newValue;
                                    attribute.MTextAttribute = mtext;
                                }
                            }
                            attribute.UpdateMTextAttribute();
                        }
                        attribute.AdjustAlignment(ctx.Db);
                        attributes++;
                        touched = true;
                    }
                    if (touched) blocks++;
                }
                tr.Commit();
            }
            return new FillWriteResult(blocks, attributes);
        }
    }
}
