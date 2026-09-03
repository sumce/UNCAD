using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                selection = ExpandSingleFrame(ctx, Split(ctx, implied.Value.GetObjectIds()));
                if (!selection.IsEmpty)
                {
                    ctx.Write("\n[U1F] 已使用预选：表格 " + selection.TableIds.Length
                        + " 个，统计文字 " + selection.TextIds.Length + " 个，图框块 "
                        + selection.FrameBlockIds.Length + " 个，DEVICENAME 设备块 "
                        + selection.DeviceBlockIds.Length + " 个，上游信息块 "
                        + selection.UpstreamInfoBlockIds.Length + " 个，US/DS 轴位块 "
                        + (selection.UpstreamAxisBlockIds.Length
                            + selection.DownstreamAxisBlockIds.Length) + " 个，上游颜色目标 "
                        + selection.UpstreamColorBlockIds.Length + " 个，设备颜色目标 "
                        + selection.DeviceColorBlockIds.Length + " 个。");
                    return selection;
                }
            }

            ObjectId[] picked = SelectionService.Pick(ctx,
                "请框选或点选清单表/图框块/设备块/统计文字: ",
                new TypedValue(0, "TEXT,MTEXT,ACAD_TABLE,INSERT"));
            return picked == null ? selection : ExpandSingleFrame(ctx, Split(ctx, picked));
        }

        /// <summary>
        /// A frame is the natural U1F/U1U selection target. Selecting only that block must
        /// still include the table, statistics, Device and Ruanguan objects in the same
        /// frame; otherwise U1U sees zero measurements and cannot read identity.
        /// </summary>
        private static FillSelection ExpandSingleFrame(CadContext ctx, FillSelection selection)
        {
            if (ctx == null || selection == null || selection.FrameBlockIds.Length != 1)
                return selection;
            FrameRegionCollection regions = FrameRegionCollector.Collect(ctx,
                selection.FrameBlockIds);
            if (regions.Errors.Count > 0 || regions.Groups.Count != 1)
                return selection;
            FrameRegionGroup group = regions.Groups[0];
            return group.EntityIds.Count == 0
                ? selection
                : Split(ctx, group.EntityIds.ToArray(), true);
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
            var values = new List<string>();
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
                        string meters = ParseRuanguanMeters(attribute?.TextString ?? "",
                            attribute?.Tag);
                        if (meters.Length > 0) values.Add(meters);
                    }
                    if (!block.IsDynamicBlock) continue;
                    foreach (DynamicBlockReferenceProperty property
                        in block.DynamicBlockReferencePropertyCollection)
                    {
                        // Dynamic blocks expose many numeric coordinates. Only a
                        // property explicitly named as a hose length may use a bare
                        // number; all other values require an mm/m unit or prefix.
                        string meters = ParseRuanguanMeters(Convert.ToString(property.Value),
                            property.PropertyName);
                        if (meters.Length > 0) values.Add(meters);
                    }
                }
                tr.Commit();
            }
            if (values.Count == 0) return "";
            string first = values[0];
            if (values.Any(value => !string.Equals(value, first,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("同一图框中存在多个不一致的 Ruanguan 软管长度。请只保留一个有效长度。");
            return first;
        }

        private static string ParseRuanguanMeters(string value, string tag)
        {
            if (!RuanguanBlockWriter.TryReadLength(value, tag,
                out double millimetres)) return "";
            return TextFormatter.FormatNum(millimetres / 1000d);
        }

        public static bool TryReadExistingIdentity(CadContext ctx, FillSelection selection,
            out ExistingFillIdentity identity, out string error)
        {
            // frameinfo_json is the durable identity source written by U1F/U1U. Prefer it
            // over editable legacy attributes so a stale or partially migrated block cannot
            // redirect an update to another BOQ column.
            FrameInfoJsonRecord json = FrameInfoJsonBlockWriter.Read(ctx,
                selection?.FrameInfoJsonBlockIds);
            if (json != null && !string.IsNullOrWhiteSpace(json.MachineId)
                && !string.IsNullOrWhiteSpace(json.DeviceName))
            {
                identity = new ExistingFillIdentity
                {
                    MachineId = json.MachineId.Trim(),
                    DeviceName = json.DeviceName.Trim()
                };
                error = "";
                return true;
            }
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

        internal static FillSelection Split(CadContext ctx, ObjectId[] ids,
            bool statisticsScopeComplete = false)
        {
            var tables = new List<ObjectId>();
            var drawingInfoTables = new List<ObjectId>();
            var texts = new List<ObjectId>();
            var frames = new List<ObjectId>();
            var devices = new List<ObjectId>();
            var deviceColor = new List<ObjectId>();
            var ruanguan = new List<ObjectId>();
            var frameInfoJson = new List<ObjectId>();
            var upstreamInfo = new List<ObjectId>();
            var upstreamState = new List<ObjectId>();
            var upstreamAxis = new List<ObjectId>();
            var downstreamAxis = new List<ObjectId>();
            var upstreamColor = new List<ObjectId>();
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids ?? Array.Empty<ObjectId>())
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead, true) as Entity;
                    if (entity is Table table)
                    {
                        if (CadDrawingInfoTableWriter.IsDrawingInfoTable(table))
                            drawingInfoTables.Add(id);
                        else tables.Add(id);
                    }
                    else if (entity is DBText || entity is MText) texts.Add(id);
                    else if (entity is BlockReference block)
                    {
                        if (IsFillTargetBlock(tr, block)) frames.Add(id);
                        if (TryGetBlockValue(tr, block, DeviceBlockFiller.TagDeviceName, out _))
                            devices.Add(id);
                        if (IsRuanguanBlock(tr, block))
                            ruanguan.Add(id);
                        if (FrameInfoJsonBlockWriter.IsJsonHostBlock(tr, block))
                            frameInfoJson.Add(id);
                        if (IsUpstreamInfoBlock(tr, block))
                            upstreamInfo.Add(id);
                        if (CadDynamicBlockStateService.IsUpstreamBlock(tr, block))
                            upstreamState.Add(id);
                        if (TryGetBlockValue(tr, block, ConnectionBlockFiller.TagUpstreamAxis, out _))
                            upstreamAxis.Add(id);
                        if (TryGetBlockValue(tr, block, ConnectionBlockFiller.TagDownstreamAxis, out _))
                            downstreamAxis.Add(id);
                        if (IsDeviceColorBlock(tr, block))
                            deviceColor.Add(id);
                        if (IsUpstreamColorBlock(tr, block))
                            upstreamColor.Add(id);
                    }
                }
                tr.Commit();
            }

            return new FillSelection
            {
                SourceIds = ids ?? new ObjectId[0],
                TableIds = tables.ToArray(),
                DrawingInfoTableIds = drawingInfoTables.ToArray(),
                TextIds = texts.ToArray(),
                FrameBlockIds = frames.ToArray(),
                DeviceBlockIds = devices.ToArray(),
                DeviceColorBlockIds = deviceColor.ToArray(),
                RuanguanBlockIds = ruanguan.ToArray(),
                FrameInfoJsonBlockIds = frameInfoJson.ToArray(),
                UpstreamInfoBlockIds = upstreamInfo.ToArray(),
                UpstreamStateBlockIds = upstreamState.ToArray(),
                UpstreamAxisBlockIds = upstreamAxis.ToArray(),
                DownstreamAxisBlockIds = downstreamAxis.ToArray(),
                UpstreamColorBlockIds = upstreamColor.ToArray(),
                StatisticsScopeComplete = statisticsScopeComplete
            };
        }

        /// <summary>
        /// Returns every model-space upstream insert that should render in the configured
        /// upstream color. Several drawing templates use separate axis/number blocks without
        /// a writable US/UPSTREAM_INFO attribute, so color discovery must also use the effective
        /// block name and attribute-definition tags.
        /// </summary>
        private static bool IsUpstreamColorBlock(Transaction tr, BlockReference block)
        {
            if (block == null) return false;
            try
            {
                if (IsUpstreamInfoBlock(tr, block)
                    || CadDynamicBlockStateService.IsUpstreamBlock(tr, block)) return true;

                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    AttributeReference attribute = tr.GetObject(attributeId,
                        OpenMode.ForRead, true) as AttributeReference;
                    if (attribute != null && IsUpstreamTag(attribute.Tag)) return true;
                }

                foreach (ObjectId definitionId in DefinitionIds(block))
                {
                    BlockTableRecord definition = tr.GetObject(definitionId,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    if (definition == null) continue;
                    if (IsUpstreamName(definition.Name)) return true;
                    foreach (ObjectId entityId in definition)
                    {
                        AttributeDefinition attribute = tr.GetObject(entityId,
                            OpenMode.ForRead, true) as AttributeDefinition;
                        if (attribute != null && IsUpstreamTag(attribute.Tag)) return true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("上游颜色目标识别失败，已跳过该块: " + ex.Message);
            }
            return false;
        }

        /// <summary>Finds device/downstream inserts that may be purely graphical.</summary>
        private static bool IsDeviceColorBlock(Transaction tr, BlockReference block)
        {
            if (block == null) return false;
            try
            {
                if (TryGetBlockValue(tr, block, DeviceBlockFiller.TagDeviceName, out _)
                    || TryGetBlockValue(tr, block, ConnectionBlockFiller.TagDownstreamAxis, out _))
                    return true;
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    AttributeReference attribute = tr.GetObject(attributeId,
                        OpenMode.ForRead, true) as AttributeReference;
                    if (attribute != null && IsDownstreamTag(attribute.Tag)) return true;
                }
                foreach (ObjectId definitionId in DefinitionIds(block))
                {
                    BlockTableRecord definition = tr.GetObject(definitionId,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    if (definition == null) continue;
                    if (IsDeviceName(definition.Name)) return true;
                    foreach (ObjectId entityId in definition)
                    {
                        AttributeDefinition attribute = tr.GetObject(entityId,
                            OpenMode.ForRead, true) as AttributeDefinition;
                        if (attribute != null && IsDownstreamTag(attribute.Tag)) return true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("设备颜色目标识别失败，已跳过该块: " + ex.Message);
            }
            return false;
        }

        private static bool IsDeviceName(string value)
        {
            string name = (value ?? "").Trim();
            if (name.Length == 0) return false;
            string normalized = NormalizeMarker(name);
            return normalized.StartsWith("device", StringComparison.Ordinal)
                || normalized == "ds"
                || normalized.StartsWith("dsaxis", StringComparison.Ordinal)
                || normalized.StartsWith("dsnumber", StringComparison.Ordinal)
                || normalized.StartsWith("dsnum", StringComparison.Ordinal)
                || normalized.StartsWith("dsno", StringComparison.Ordinal)
                || normalized.StartsWith("dsid", StringComparison.Ordinal)
                || normalized.StartsWith("downstream", StringComparison.Ordinal)
                || normalized.IndexOf("下游", StringComparison.Ordinal) >= 0;
        }

        private static bool IsDownstreamTag(string value)
        {
            string tag = (value ?? "").Trim();
            if (tag.Length == 0) return false;
            string normalized = NormalizeMarker(tag);
            return normalized == "ds"
                || normalized.StartsWith("dsaxis", StringComparison.Ordinal)
                || normalized.StartsWith("dsnumber", StringComparison.Ordinal)
                || normalized.StartsWith("dsnum", StringComparison.Ordinal)
                || normalized.StartsWith("dsno", StringComparison.Ordinal)
                || normalized.StartsWith("dsid", StringComparison.Ordinal)
                || normalized.StartsWith("downstream", StringComparison.Ordinal)
                || normalized.IndexOf("下游", StringComparison.Ordinal) >= 0;
        }

        private static IEnumerable<ObjectId> DefinitionIds(BlockReference block)
        {
            yield return block.BlockTableRecord;
            if (block.IsDynamicBlock && block.DynamicBlockTableRecord != block.BlockTableRecord)
                yield return block.DynamicBlockTableRecord;
        }

        private static bool IsUpstreamName(string value)
        {
            string name = (value ?? "").Trim();
            if (name.Length == 0) return false;
            string normalized = NormalizeMarker(name);
            return normalized == "us"
                || normalized.StartsWith("upstream", StringComparison.Ordinal)
                || normalized.StartsWith("usaxis", StringComparison.Ordinal)
                || normalized.StartsWith("usnumber", StringComparison.Ordinal)
                || normalized.StartsWith("usnum", StringComparison.Ordinal)
                || normalized.StartsWith("usno", StringComparison.Ordinal)
                || normalized.StartsWith("usid", StringComparison.Ordinal)
                || normalized.IndexOf("上游", StringComparison.Ordinal) >= 0;
        }

        private static bool IsUpstreamTag(string value)
        {
            string tag = (value ?? "").Trim();
            if (tag.Length == 0) return false;
            string normalized = NormalizeMarker(tag);
            return normalized == "us"
                || normalized.StartsWith("upstream", StringComparison.Ordinal)
                || normalized.StartsWith("usaxis", StringComparison.Ordinal)
                || normalized.StartsWith("usid", StringComparison.Ordinal)
                || normalized.StartsWith("usnumber", StringComparison.Ordinal)
                || normalized.StartsWith("usnum", StringComparison.Ordinal)
                || normalized.StartsWith("usno", StringComparison.Ordinal)
                || normalized.IndexOf("上游", StringComparison.Ordinal) >= 0;
        }

        private static string NormalizeMarker(string value)
            => new string((value ?? "").Where(character =>
                !char.IsWhiteSpace(character) && character != '_' && character != '-')
                .ToArray()).ToLowerInvariant();

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
            catch (System.Exception ex)
            {
                throw new InvalidOperationException("无法识别 Ruanguan 块。", ex);
            }
        }

        private static bool IsUpstreamInfoBlock(Transaction tr, BlockReference block)
        {
            if (block == null) return false;
            if (TryGetBlockValue(tr, block, ConnectionBlockFiller.TagUpstreamInfo, out _))
                return true;
            try
            {
                ObjectId definitionId = block.IsDynamicBlock
                    ? block.DynamicBlockTableRecord : block.BlockTableRecord;
                BlockTableRecord definition = tr.GetObject(definitionId, OpenMode.ForRead, true)
                    as BlockTableRecord;
                string name = definition?.Name ?? "";
                return name.StartsWith("upstream_info", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFillTargetBlock(Transaction tr, BlockReference block)
        {
            if (block == null) return false;
            ObjectId recordId = block.IsDynamicBlock
                ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            BlockTableRecord record = tr.GetObject(recordId, OpenMode.ForRead, true)
                as BlockTableRecord;
            // Legacy frame inserts may have no recognizable attributes yet. The stable
            // definition name is the migration anchor for adding frameinfo_json.
            if (record != null && FrameRegionCollector.IsSupportedFrameName(record.Name))
                return true;
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
