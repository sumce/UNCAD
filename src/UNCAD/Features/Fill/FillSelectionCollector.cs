using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using UNCAD.Cad;
using UNCAD.Core.Fill;
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
                    ctx.Write("\n[U1F] 已使用预选：表格 " + selection.TableIds.Length
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

        public static List<string> ReadStatisticsLines(CadContext ctx, ObjectId[] textIds,
            bool includeText, bool includeMText)
        {
            var lines = new List<string>();
            if (textIds == null || textIds.Length == 0) return lines;
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in textIds)
                {
                    var entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                    if (includeText && entity is DBText text)
                        lines.Add(TextParser.CleanMText(text.TextString));
                    else if (includeMText && entity is MText mtext)
                        lines.AddRange(TextParser.SplitMTextLines(mtext.Contents)
                            .ConvertAll(TextParser.CleanMText));
                }
            }
            return lines;
        }

        public static string ReadRuanguanLengthMeters(CadContext ctx, ObjectId[] blockIds)
        {
            if (ctx == null || blockIds == null || blockIds.Length == 0) return "";
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in blockIds)
                {
                    var block = tr.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                    if (block == null) continue;
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true)
                            as AttributeReference;
                        string value = attribute?.TextString ?? "";
                        string meters = ParseRuanguanMeters(value);
                        if (meters.Length > 0) return meters;
                    }
                    if (!block.IsDynamicBlock) continue;
                    foreach (DynamicBlockReferenceProperty property
                        in block.DynamicBlockReferencePropertyCollection)
                    {
                        string meters = ParseRuanguanMeters(Convert.ToString(property.Value));
                        if (meters.Length > 0) return meters;
                    }
                }
                tr.Commit();
            }
            return "";
        }

        private static string ParseRuanguanMeters(string value)
            => RuanguanLengthParser.ParseMeters(value);

        public static bool TryReadExistingIdentity(CadContext ctx, FillSelection selection,
            out ExistingFillIdentity identity, out string error)
        {
            var powers = new List<string>();
            var composites = new List<string>();
            var devices = new List<string>();
            var ids = new HashSet<ObjectId>();
            foreach (ObjectId id in selection.FrameBlockIds) ids.Add(id);
            foreach (ObjectId id in selection.DeviceBlockIds) ids.Add(id);
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    var block = tr.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                    if (block == null) continue;
                    if (TryGetBlockValue(tr, block, FrameBlockFiller.TagPower, out string power))
                        powers.Add(power);
                    if (TryGetBlockValue(tr, block, FrameBlockFiller.TagDevice, out string composite))
                        composites.Add(composite);
                    if (TryGetBlockValue(tr, block, DeviceBlockFiller.TagDeviceName, out string device))
                        devices.Add(device);
                }
            }
            return ExistingFillIdentityResolver.TryResolve(
                powers, composites, devices, out identity, out error);
        }

        internal static FillSelection Split(CadContext ctx, ObjectId[] ids)
        {
            var tables = new List<ObjectId>();
            var texts = new List<ObjectId>();
            var frames = new List<ObjectId>();
            var devices = new List<ObjectId>();
            var ruanguan = new List<ObjectId>();
            var upstreamInfo = new List<ObjectId>();
            var upstreamState = new List<ObjectId>();
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
                            if (IsRuanguanBlock(tr, block))
                                ruanguan.Add(id);
                            if (TryGetBlockValue(tr, block, ConnectionBlockFiller.TagUpstreamInfo, out _))
                                upstreamInfo.Add(id);
                            if (CadDynamicBlockStateService.IsUpstreamBlock(tr, block))
                                upstreamState.Add(id);
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
                Log.Warn("U1F selection split failed: " + ex.Message);
            }

            return new FillSelection
            {
                SourceIds = ids ?? new ObjectId[0],
                TableIds = tables.ToArray(),
                TextIds = texts.ToArray(),
                FrameBlockIds = frames.ToArray(),
                DeviceBlockIds = devices.ToArray(),
                RuanguanBlockIds = ruanguan.ToArray(),
                UpstreamInfoBlockIds = upstreamInfo.ToArray(),
                UpstreamStateBlockIds = upstreamState.ToArray(),
                UpstreamAxisBlockIds = upstreamAxis.ToArray(),
                DownstreamAxisBlockIds = downstreamAxis.ToArray()
            };
        }

        private static bool IsRuanguanBlock(Transaction tr, BlockReference block)
        {
            try
            {
                ObjectId definitionId = block.IsDynamicBlock
                    ? block.DynamicBlockTableRecord : block.BlockTableRecord;
                var definition = tr.GetObject(definitionId, OpenMode.ForRead, true)
                    as BlockTableRecord;
                return definition != null && string.Equals(definition.Name, "Ruanguan",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
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
