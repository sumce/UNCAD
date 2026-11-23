using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
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
                    // 3.2.0.6（审计修复）：U1F/U1U 两模式共用本收集器，前缀写死
                    // [U1F] 会在 U1U 误导向——改中性双标。
                    ctx.Write("\n[U1F/U1U] 已使用预选：表格 " + selection.TableIds.Length
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
                new TypedValue(0, "TEXT,MTEXT,DIMENSION,ACAD_TABLE,INSERT"));
            return picked == null ? selection : ExpandSingleFrame(ctx, Split(ctx, picked));
        }

        /// <summary>
        /// A frame is the natural U1F/U1U selection target. Selecting only that block must
        /// still include the table, statistics, Device and Ruanguan objects in the same
        /// frame; otherwise U1U sees zero measurements and cannot read identity.
        /// 3.3.4（图层收口，审计 B-5）：展开时顺带带上图框边界（Region）——
        /// 严格分区 EntityIds 从来不含电缆线（TryAnchor 只收表格/文字/块），
        /// 无边界时路由体检按 EntityIds 包含判归属、对图框内容整体空转
        ///（连通性无段、图层矫正无目标）。U1F 图框块选集此前正是这种空转
        ///（U1U 靠自己的分区赋值拿到 Boundary，U1F 没有）。机会式：只在
        /// "恰好一个图框块"的展开路径设置；分区失败/空分区/部分显式选集不设
        /// Region，回退既有语义。U1U 后续分区的 Groups==1 赋值是同值覆写。
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
            if (group.EntityIds.Count == 0)
                return selection;
            FillSelection expanded = Split(ctx, group.EntityIds.ToArray(), true);
            expanded.Region = group;
            return expanded;
        }

        public static List<string> ReadStatisticsLines(CadContext ctx, ObjectId[] textIds,
            bool includeText, bool includeMText, bool includeDimension)
        {
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                List<string> lines = ReadStatisticsLines(transaction, textIds,
                    includeText, includeMText, includeDimension);
                transaction.Commit();
                return lines;
            }
        }

        internal static List<string> ReadStatisticsLines(Transaction transaction,
            ObjectId[] textIds, bool includeText, bool includeMText, bool includeDimension)
        {
            var lines = new List<string>();
            if (textIds == null || textIds.Length == 0) return lines;
            foreach (ObjectId id in textIds)
            {
                var entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                if (entity == null || entity.IsErased) continue;
                StatisticsTextReader.AppendLines(entity, includeText, includeMText,
                    includeDimension, lines);
            }
            return lines.ConvertAll(TextParser.CleanMText);
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
            double[] lengths = ReadRuanguanLengthMillimeters(transaction, blockIds);
            if (lengths.Length == 0) return "";
            double total = lengths.Sum() / 1000d;
            if (double.IsNaN(total) || double.IsInfinity(total))
                throw new InvalidDataException("Ruanguan 软管长度合计超出可处理范围。");
            return TextFormatter.FormatNum(total);
        }

        internal static double[] ReadRuanguanLengthMillimeters(
            Transaction transaction, ObjectId[] blockIds)
        {
            if (blockIds == null || blockIds.Length == 0) return Array.Empty<double>();
            var lengths = new List<double>();
            var seenBlocks = new HashSet<ObjectId>();
            foreach (ObjectId id in blockIds)
            {
                if (!seenBlocks.Add(id)) continue;
                var block = transaction.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                if (block == null) continue;
                var blockLengths = new HashSet<double>();
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    var attribute = transaction.GetObject(attributeId, OpenMode.ForRead, true)
                        as AttributeReference;
                    if (RuanguanBlockWriter.TryReadLength(attribute?.TextString ?? "",
                        attribute?.Tag, out double millimetres))
                        blockLengths.Add(millimetres);
                }
                if (block.IsDynamicBlock)
                {
                    foreach (DynamicBlockReferenceProperty property
                        in block.DynamicBlockReferencePropertyCollection)
                    {
                        // Dynamic blocks expose many numeric coordinates. Only a
                        // property explicitly named as a hose length may use a bare
                        // number; all other values require an mm/m unit or prefix.
                        if (RuanguanBlockWriter.TryReadLength(
                            Convert.ToString(property.Value), property.PropertyName,
                            out double millimetres))
                            blockLengths.Add(millimetres);
                    }
                }

                if (blockLengths.Count > 1)
                    throw new InvalidDataException("Ruanguan 块 " + block.Handle
                        + " 存在多个不一致的软管长度。");
                if (blockLengths.Count == 1) lengths.Add(blockLengths.Single());
            }
            return lengths.ToArray();
        }

        public static bool TryReadExistingIdentity(CadContext ctx, FillSelection selection,
            out ExistingFillIdentity identity, out string error)
        {
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                // 2026-11-04：身份读取同样带句柄校验——复制图框的源框 JSON
                // 在这里被拒，身份回退到物理标注，防止幽灵机台。
                string ownHandle = FrameInfoJsonBlockWriter.OwnFrameHandle(
                    transaction, selection?.FrameBlockIds);
                bool result = TryReadExistingIdentity(transaction, selection,
                    ownHandle, out identity, out error, out _);
                transaction.Commit();
                return result;
            }
        }

        internal static bool TryReadExistingIdentity(Transaction transaction,
            FillSelection selection, out ExistingFillIdentity identity, out string error)
            => TryReadExistingIdentity(transaction, selection,
                FrameInfoJsonBlockWriter.OwnFrameHandle(transaction,
                    selection?.FrameBlockIds),
                out identity, out error, out _);

        /// <summary>
        /// 带图框句柄校验的身份读取：归属不符的外来 frameinfo_json 会被忽略
        /// （foreignJsonNote 说明来源），身份回退到设备块/标签推断——防止图框
        /// 移动后遗留 JSON 被几何错误认领成幽灵机台（2026-11-02）。
        /// 第二轮加固：JSON 与图框物理标注分歧时信物理标注（复制图框的旧 JSON
        /// 不再压过用户已改新的机台号），分歧信息并入 foreignJsonNote。
        /// </summary>
        internal static bool TryReadExistingIdentity(Transaction transaction,
            FillSelection selection, string expectedFrameHandle,
            out ExistingFillIdentity identity, out string error, out string foreignJsonNote)
            => TryReadExistingIdentity(transaction, selection, expectedFrameHandle,
                out identity, out error, out foreignJsonNote, true);

        /// <summary>
        /// 带设备块开关的身份读取。includeDeviceBlocks=false 供 XLAYOUT 使用
        /// （D-045 修订，2026-11-22）：设备块按插入点归属分区，全图与单机选择的
        /// 边界集不同会把外来块归进区域——同一图框在不同选择下可能读出不同的
        /// DEVICENAME，布局身份必须只认图框自身属性才与选择集无关。
        /// </summary>
        internal static bool TryReadExistingIdentity(Transaction transaction,
            FillSelection selection, string expectedFrameHandle,
            out ExistingFillIdentity identity, out string error,
            out string foreignJsonNote, bool includeDeviceBlocks)
        {
            // D-040 后 JSON 主存储 = 图框块 FRAMEINFO 属性，独立块仅遗留读源：
            // 读身份必须用 ReadSources（含图框块），只传独立块会漏读新模板。
            FrameInfoJsonRecord json = FrameInfoJsonBlockWriter.Read(transaction,
                FrameInfoJsonBlockWriter.ReadSources(selection), expectedFrameHandle,
                out foreignJsonNote);
            ExistingFillIdentity jsonIdentity = null;
            if (json != null && !string.IsNullOrWhiteSpace(json.MachineId)
                && !string.IsNullOrWhiteSpace(json.DeviceName))
            {
                jsonIdentity = new ExistingFillIdentity
                {
                    MachineId = json.MachineId.Trim(),
                    DeviceName = json.DeviceName.Trim(),
                    // D-056：键只在 JSON 身份胜出时生效（物理标注胜出=旧键
                    // 属于他机回路，必须丢弃，走按名匹配+歧义弹窗）。
                    CircuitKey = (json.CircuitKey ?? "").Trim()
                };
            }

            ExistingFillIdentity physicalIdentity = null;
            string physicalError = "";
            if (!TryReadPhysicalIdentity(transaction, selection,
                    out physicalIdentity, out physicalError, includeDeviceBlocks))
            {
                physicalIdentity = null;
            }

            ExistingFillIdentity decided = UNCAD.Core.Fill.FrameIdentityArbiter.Decide(
                jsonIdentity, physicalIdentity,
                out bool physicalWins, out string divergenceNote);
            if (physicalWins)
            {
                if (foreignJsonNote.Length > 0) foreignJsonNote += " ";
                foreignJsonNote += divergenceNote;
            }
            if (decided != null)
            {
                identity = decided;
                error = "";
                return true;
            }
            identity = null;
            error = physicalError;
            return false;
        }

        /// <summary>
        /// 从图框/设备块的 MACHINEID 属性与标注推断物理身份。includeDeviceBlocks=false
        /// 时设备块完全缺席：只读图框自身的 MACHINEID-POWER/MACHINEID-DEVICE，
        /// DEVICENAME 一律不收集（连图框块自带的同名属性也不读）——XLAYOUT 布局
        /// 身份与选择集边界无关（D-045 修订，2026-11-22）。
        /// </summary>
        private static bool TryReadPhysicalIdentity(Transaction transaction,
            FillSelection selection, out ExistingFillIdentity identity,
            out string error, bool includeDeviceBlocks)
        {
            var powers = new List<string>();
            var composites = new List<string>();
            var devices = new List<string>();
            var deviceBlocks = new List<ObjectId>();
            var ids = new HashSet<ObjectId>();
            foreach (ObjectId id in selection.FrameBlockIds) ids.Add(id);
            if (includeDeviceBlocks)
                foreach (ObjectId id in selection.DeviceBlockIds) ids.Add(id);
            foreach (ObjectId id in ids)
            {
                var block = transaction.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                if (block == null) continue;
                if (TryGetBlockValue(transaction, block, FrameBlockFiller.TagPower,
                    out string power))
                    powers.Add(NormalizeIdentityValue(power));
                if (TryGetBlockValue(transaction, block, FrameBlockFiller.TagDevice,
                    out string composite))
                    composites.Add(NormalizeIdentityValue(composite));
                if (includeDeviceBlocks
                    && TryGetBlockValue(transaction, block, DeviceBlockFiller.TagDeviceName,
                        out string device))
                {
                    devices.Add(NormalizeIdentityValue(device));
                    deviceBlocks.Add(id);
                }
            }
            bool resolved = ExistingFillIdentityResolver.TryResolve(
                powers, composites, devices, out identity, out error);
            if (!resolved && error.IndexOf("DEVICENAME",
                StringComparison.OrdinalIgnoreCase) >= 0)
            {
                List<string> candidates = deviceBlocks.Distinct()
                    .Select(id => DescribeDeviceNameCandidate(transaction, id))
                    .Where(item => item.Length > 0)
                    .ToList();
                if (candidates.Count > 0)
                    error += Environment.NewLine + "DEVICENAME 候选块诊断："
                        + Environment.NewLine + string.Join(Environment.NewLine, candidates);
            }
            return resolved;
        }

        private static string DescribeDeviceNameCandidate(Transaction transaction,
            ObjectId id)
        {
            BlockReference block;
            try
            {
                block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
            }
            catch { return ""; }
            if (block == null) return "";
            if (!TryGetBlockValue(transaction, block, DeviceBlockFiller.TagDeviceName,
                    out string value, out string valueSource,
                    out Point3d? attributePosition)) return "";

            string definitionName = "";
            foreach (ObjectId definitionId in DefinitionIds(block))
            {
                try
                {
                    BlockTableRecord definition = transaction.GetObject(definitionId,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    if (string.IsNullOrWhiteSpace(definition?.Name)) continue;
                    definitionName = definition.Name;
                    if (!definitionName.StartsWith("*", StringComparison.Ordinal)) break;
                }
                catch { }
            }

            string detail = "块句柄 " + block.Handle
                + "，块名“" + (definitionName.Length > 0
                    ? definitionName : SafeBlockName(block)) + "”"
                + "，插入点" + FormatPoint(block.Position)
                + "，DEVICENAME=【" + NormalizeIdentityValue(value)
                + "】，取值来源=" + valueSource;
            if (attributePosition.HasValue)
                detail += "，属性位置" + FormatPoint(attributePosition.Value);
            try
            {
                Extents3d extents = block.GeometricExtents;
                var center = new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2d,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2d,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) / 2d);
                detail += "，可见几何中心" + FormatPoint(center);
            }
            catch { }
            return detail;
        }

        private static string SafeBlockName(BlockReference block)
        {
            try { return block?.Name ?? ""; }
            catch { return ""; }
        }

        private static string FormatPoint(Point3d point)
            => "(" + TextFormatter.FormatNum(point.X) + ","
                + TextFormatter.FormatNum(point.Y) + ")";

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
                    else if (entity is DBText || entity is MText
                        || entity is AlignedDimension || entity is RotatedDimension)
                        texts.Add(id);
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
                        flags.IsRuanguan = BlockNameNormalizer.IsFlexibleConduitBlock(name);
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
            => TryGetBlockValue(tr, block, name, out value, out _, out _);

        private static bool TryGetBlockValue(Transaction tr, BlockReference block,
            string name, out string value, out string valueSource,
            out Point3d? valuePosition)
        {
            value = "";
            valueSource = "";
            valuePosition = null;
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true)
                    as AttributeReference;
                if (attribute != null && string.Equals(attribute.Tag, name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = attribute.TextString ?? "";
                    valueSource = "实例 AttributeReference";
                    try { valuePosition = attribute.Position; }
                    catch { }
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
                    valueSource = "动态块属性";
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
                    valueSource = "块定义 AttributeDefinition 默认值";
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeIdentityValue(string value)
            => string.Join(" ", TextParser.SplitMTextLines(
                TextParser.CleanMText(value ?? ""))).Trim();
    }
}
