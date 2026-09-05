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
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                List<string> lines = ReadStatisticsLines(transaction, textIds,
                    includeText, includeMText);
                transaction.Commit();
                return lines;
            }
        }

        internal static List<string> ReadStatisticsLines(Transaction transaction,
            ObjectId[] textIds, bool includeText, bool includeMText)
        {
            var lines = new List<string>();
            if (textIds == null || textIds.Length == 0) return lines;
            foreach (ObjectId id in textIds)
            {
                var entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                if (includeText && entity is DBText text)
                    lines.Add(TextParser.CleanMText(text.TextString));
                else if (includeMText && entity is MText mtext)
                    lines.AddRange(TextParser.SplitMTextLines(mtext.Contents)
                        .ConvertAll(TextParser.CleanMText));
            }
            return lines;
        }

        public static string ReadRuanguanLengthMeters(CadContext ctx, ObjectId[] blockIds)
        {
            if (ctx == null || blockIds == null || blockIds.Length == 0) return "";
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                string result = ReadRuanguanLengthMeters(transaction, blockIds);
                transaction.Commit();
                return result;
            }
        }

        internal static string ReadRuanguanLengthMeters(Transaction transaction,
            ObjectId[] blockIds)
        {
            if (blockIds == null || blockIds.Length == 0) return "";
            var values = new List<string>();
            foreach (ObjectId id in blockIds)
            {
                var block = transaction.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                if (block == null) continue;
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    var attribute = transaction.GetObject(attributeId, OpenMode.ForRead, true)
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
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                bool result = TryReadExistingIdentity(transaction, selection,
                    out identity, out error);
                transaction.Commit();
                return result;
            }
        }

        internal static bool TryReadExistingIdentity(Transaction transaction,
            FillSelection selection, out ExistingFillIdentity identity, out string error)
        {
            FrameInfoJsonRecord json = FrameInfoJsonBlockWriter.Read(transaction,
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
            foreach (ObjectId id in ids)
            {
                var block = transaction.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                if (block == null) continue;
                if (TryGetBlockValue(transaction, block, FrameBlockFiller.TagPower,
                    out string power))
                    powers.Add(power);
                if (TryGetBlockValue(transaction, block, FrameBlockFiller.TagDevice,
                    out string composite))
                    composites.Add(composite);
                if (TryGetBlockValue(transaction, block, DeviceBlockFiller.TagDeviceName,
                    out string device))
                    devices.Add(device);
            }
            return ExistingFillIdentityResolver.TryResolve(
                powers, composites, devices, out identity, out error);
        }

        internal static FillSelection Split(CadContext ctx, ObjectId[] ids,
            bool statisticsScopeComplete = false)
        {
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                FillSelection result = Split(transaction, ids, statisticsScopeComplete);
                transaction.Commit();
                return result;
            }
        }

        internal static FillSelection Split(Transaction transaction, ObjectId[] ids,
            bool statisticsScopeComplete = false, CadBlockDefinitionReader definitions = null)
        {
            definitions = definitions ?? new CadBlockDefinitionReader(transaction);
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
            var definitionCache = new Dictionary<ObjectId, BlockDefinitionFlags>();
            var tr = transaction;
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
                        BlockDefinitionFlags flags = GetDefinitionFlags(tr, block,
                            definitionCache, definitions);
                        // Classify each insert once. Definition flags are copied above so one
                        // insert's extra attributes cannot contaminate another insert.
                        foreach (ObjectId attributeId in block.AttributeCollection)
                        {
                            var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true)
                                as AttributeReference;
                            if (attribute == null) continue;
                            string tag = attribute.Tag ?? "";
                            flags.IsFrame |= FrameBlockFiller.IsKnownTag(tag);
                            flags.IsJson |= string.Equals(tag, "JSON", StringComparison.OrdinalIgnoreCase);
                            flags.IsUpstreamColor |= IsUpstreamTag(tag);
                            flags.IsDeviceColor |= IsDownstreamTag(tag);
                            ApplyValueTag(flags, tag);
                        }
                        if (block.IsDynamicBlock)
                            foreach (DynamicBlockReferenceProperty property
                                in block.DynamicBlockReferencePropertyCollection)
                                ApplyValueTag(flags, property.PropertyName);
                        if (flags.IsFrame) frames.Add(id);
                        if (flags.IsDevice) devices.Add(id);
                        if (flags.IsRuanguan)
                            ruanguan.Add(id);
                        if (flags.IsJson)
                            frameInfoJson.Add(id);
                        if (flags.IsUpstreamInfo)
                            upstreamInfo.Add(id);
                        if (flags.IsUpstream)
                            upstreamState.Add(id);
                        if (flags.HasUpstreamAxis)
                            upstreamAxis.Add(id);
                        if (flags.HasDownstreamAxis)
                            downstreamAxis.Add(id);
                        if (flags.IsDeviceColor || flags.IsDevice || flags.HasDownstreamAxis)
                            deviceColor.Add(id);
                        if (flags.IsUpstreamColor || flags.IsUpstream || flags.IsUpstreamInfo
                            || flags.HasUpstreamAxis)
                            upstreamColor.Add(id);
                    }
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
        private sealed class BlockDefinitionFlags
        {
            public bool IsFrame;
            public bool IsRuanguan;
            public bool IsJson;
            public bool IsUpstream;
            public bool IsUpstreamInfo;
            public bool IsDevice;
            public bool HasUpstreamAxis;
            public bool HasDownstreamAxis;
            public bool IsUpstreamColor;
            public bool IsDeviceColor;
        }

        private static BlockDefinitionFlags GetDefinitionFlags(Transaction tr,
            BlockReference block, IDictionary<ObjectId, BlockDefinitionFlags> cache,
            CadBlockDefinitionReader definitions)
        {
            var result = new BlockDefinitionFlags();
            foreach (ObjectId definitionId in DefinitionIds(block))
            {
                if (!cache.TryGetValue(definitionId, out BlockDefinitionFlags flags))
                {
                    flags = new BlockDefinitionFlags();
                    CadBlockDefinitionReader.Definition definition = definitions.Read(definitionId);
                    if (definition != null)
                    {
                        string name = BlockNameNormalizer.RemoveMangledSuffix(definition.Name);
                        flags.IsFrame = FrameRegionCollector.IsSupportedFrameName(name);
                        flags.IsRuanguan = string.Equals(name, "Ruanguan",
                            StringComparison.OrdinalIgnoreCase);
                        flags.IsJson = name.IndexOf("frameinfo_json",
                            StringComparison.OrdinalIgnoreCase) >= 0;
                        flags.IsUpstream = IsUpstreamName(name);
                        flags.IsUpstreamInfo = name.StartsWith("upstream_info",
                            StringComparison.OrdinalIgnoreCase);
                        flags.IsDevice = IsDeviceName(name);
                        foreach (KeyValuePair<string, string> attribute in definition.Attributes)
                        {
                            string tag = attribute.Key;
                            flags.IsFrame |= FrameBlockFiller.IsKnownTag(tag);
                            flags.IsJson |= tag.IndexOf("JSON",
                                StringComparison.OrdinalIgnoreCase) >= 0;
                            flags.IsUpstreamColor |= IsUpstreamTag(tag);
                            flags.IsDeviceColor |= IsDownstreamTag(tag);
                            ApplyValueTag(flags, tag);
                        }
                    }
                    cache.Add(definitionId, flags);
                }
                result.IsFrame |= flags.IsFrame;
                result.IsRuanguan |= flags.IsRuanguan;
                result.IsJson |= flags.IsJson;
                result.IsUpstream |= flags.IsUpstream;
                result.IsUpstreamInfo |= flags.IsUpstreamInfo;
                result.IsDevice |= flags.IsDevice;
                result.HasUpstreamAxis |= flags.HasUpstreamAxis;
                result.HasDownstreamAxis |= flags.HasDownstreamAxis;
                result.IsUpstreamColor |= flags.IsUpstreamColor;
                result.IsDeviceColor |= flags.IsDeviceColor;
            }
            return result;
        }

        private static void ApplyValueTag(BlockDefinitionFlags flags, string tag)
        {
            flags.IsDevice |= string.Equals(tag, DeviceBlockFiller.TagDeviceName,
                StringComparison.OrdinalIgnoreCase);
            flags.IsUpstreamInfo |= string.Equals(tag, ConnectionBlockFiller.TagUpstreamInfo,
                StringComparison.OrdinalIgnoreCase);
            flags.HasUpstreamAxis |= string.Equals(tag, ConnectionBlockFiller.TagUpstreamAxis,
                StringComparison.OrdinalIgnoreCase);
            flags.HasDownstreamAxis |= string.Equals(tag, ConnectionBlockFiller.TagDownstreamAxis,
                StringComparison.OrdinalIgnoreCase);
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
            => FrameRegionCollector.DefinitionIds(block);

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
            => new string(BlockNameNormalizer.RemoveMangledSuffix(value).Where(character =>
                !char.IsWhiteSpace(character) && character != '_' && character != '-')
                .ToArray()).ToLowerInvariant();

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

            foreach (ObjectId recordId in DefinitionIds(block))
            {
                BlockTableRecord record;
                try
                {
                    record = tr.GetObject(recordId, OpenMode.ForRead, true)
                        as BlockTableRecord;
                }
                catch
                {
                    continue;
                }
                if (record == null) continue;
                foreach (ObjectId entityId in record)
                {
                    AttributeDefinition definition;
                    try
                    {
                        definition = tr.GetObject(entityId, OpenMode.ForRead, true)
                            as AttributeDefinition;
                    }
                    catch
                    {
                        continue;
                    }
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
