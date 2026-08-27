using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    internal static class FillSelectionCollector
    {
        public static FillSelection Collect(CadContext ctx)
        {
            var selection = new FillSelection();
            PromptSelectionResult implied = ctx.Ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null
                && implied.Value.Count > 0)
            {
                selection = Split(ctx, implied.Value.GetObjectIds());
                if (!selection.IsEmpty)
                {
                    ctx.Write("\n[UNC_FILL] 已使用预选：表格 " + selection.TableIds.Length
                        + " 个，统计文字 " + selection.TextIds.Length + " 个，图框块 "
                        + selection.FrameBlockIds.Length + " 个，DEVICENAME 设备块 "
                        + selection.DeviceBlockIds.Length + " 个，上游信息块 "
                        + selection.UpstreamInfoBlockIds.Length + " 个，US/DS 轴位块 "
                        + (selection.UpstreamAxisBlockIds.Length
                            + selection.DownstreamAxisBlockIds.Length) + " 个。");
                    return selection;
                }
            }

            ObjectId[] picked = SelectionService.Pick(ctx,
                "请框选或点选清单表/图框块/设备块/统计文字: ",
                new TypedValue(0, "TEXT,MTEXT,ACAD_TABLE,INSERT"));
            return picked == null ? selection : Split(ctx, picked);
        }

        public static CableStatResult CalculateStats(CadContext ctx, ObjectId[] textIds,
            double mmPerGrid)
        {
            var lines = new List<string>();
            if (textIds != null && textIds.Length > 0)
            {
                using (var tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in textIds)
                    {
                        var entity = tr.GetObject(id, OpenMode.ForRead, true) as Entity;
                        if (entity is DBText text) lines.Add(text.TextString);
                        else if (entity is MText mtext)
                            lines.AddRange(TextParser.SplitMTextLines(mtext.Contents));
                    }
                }
            }
            return StatCalculator.Calculate(lines.ConvertAll(TextParser.CleanMText), mmPerGrid);
        }

        private static FillSelection Split(CadContext ctx, ObjectId[] ids)
        {
            var tables = new List<ObjectId>();
            var texts = new List<ObjectId>();
            var frames = new List<ObjectId>();
            var devices = new List<ObjectId>();
            var upstreamInfo = new List<ObjectId>();
            var upstreamAxis = new List<ObjectId>();
            var downstreamAxis = new List<ObjectId>();
            try
            {
                using (var tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids)
                    {
                        var entity = tr.GetObject(id, OpenMode.ForRead, true) as Entity;
                        if (entity is Table) tables.Add(id);
                        else if (entity is DBText || entity is MText) texts.Add(id);
                        else if (entity is BlockReference block)
                        {
                            if (IsFillTargetBlock(tr, block)) frames.Add(id);
                            if (TryGetBlockValue(tr, block, DeviceBlockFiller.TagDeviceName, out _))
                                devices.Add(id);
                            if (TryGetBlockValue(tr, block, ConnectionBlockFiller.TagUpstreamInfo, out _))
                                upstreamInfo.Add(id);
                            if (TryGetBlockValue(tr, block, ConnectionBlockFiller.TagUpstreamAxis, out _))
                                upstreamAxis.Add(id);
                            if (TryGetBlockValue(tr, block, ConnectionBlockFiller.TagDownstreamAxis, out _))
                                downstreamAxis.Add(id);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("UNC_FILL selection split failed: " + ex.Message);
            }

            return new FillSelection
            {
                TableIds = tables.ToArray(),
                TextIds = texts.ToArray(),
                FrameBlockIds = frames.ToArray(),
                DeviceBlockIds = devices.ToArray(),
                UpstreamInfoBlockIds = upstreamInfo.ToArray(),
                UpstreamAxisBlockIds = upstreamAxis.ToArray(),
                DownstreamAxisBlockIds = downstreamAxis.ToArray()
            };
        }

        private static bool IsFillTargetBlock(Transaction tr, BlockReference block)
        {
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true)
                    as AttributeReference;
                if (attribute != null && FrameBlockFiller.IsKnownTag(attribute.Tag)) return true;
            }
            return false;
        }

        private static bool TryGetBlockValue(Transaction tr, BlockReference block,
            string name, out string value)
        {
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true)
                    as AttributeReference;
                if (attribute != null && string.Equals(attribute.Tag, name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = attribute.TextString ?? "";
                    return true;
                }
            }

            if (block.IsDynamicBlock)
            {
                foreach (DynamicBlockReferenceProperty property
                    in block.DynamicBlockReferencePropertyCollection)
                {
                    if (!string.Equals(property.PropertyName, name,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    value = Convert.ToString(property.Value) ?? "";
                    return true;
                }
            }

            var recordIds = new List<ObjectId> { block.BlockTableRecord };
            if (block.IsDynamicBlock && block.DynamicBlockTableRecord != block.BlockTableRecord)
                recordIds.Add(block.DynamicBlockTableRecord);
            foreach (ObjectId recordId in recordIds)
            {
                var record = tr.GetObject(recordId, OpenMode.ForRead, true) as BlockTableRecord;
                if (record == null) continue;
                foreach (ObjectId entityId in record)
                {
                    var definition = tr.GetObject(entityId, OpenMode.ForRead, true)
                        as AttributeDefinition;
                    if (definition == null || !string.Equals(definition.Tag, name,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    value = definition.TextString ?? "";
                    return true;
                }
            }

            value = "";
            return false;
        }
    }
}
