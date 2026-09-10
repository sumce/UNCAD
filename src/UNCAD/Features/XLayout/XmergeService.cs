using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Cad;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.Core.Submission;
using UNCAD.Features.Submit;
using UNCAD.Infra;

namespace UNCAD.Features.XLayout
{
    internal sealed class XmergeResult
    {
        public int FileCount { get; set; }
        public int FrameCount { get; set; }
        public int EntityCount { get; set; }
        public int UnplacedEntityCount { get; set; }
        public string[] Files { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Collects source frames in detached databases, builds the complete layout in RAM,
    /// then commits that prepared drawing to the active space in one operation.
    /// </summary>
    internal static class XmergeService
    {
        private static readonly Regex MangledName = new Regex(
            @"^(?:\$\d+\$)+|(?:\$\d+\$)+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static XmergeResult Merge(CadContext ctx, IEnumerable<string> filePaths)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            return Merge(ctx.Db, filePaths);
        }

        public static XmergeResult Merge(Database database, IEnumerable<string> filePaths)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            string activeDrawing = GetFullPathOrEmpty(database.Filename);
            var fileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in filePaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw) || !File.Exists(raw)) continue;
                string path = GetFullPathOrEmpty(raw);
                if (path.Length > 0 && !string.Equals(path, activeDrawing,
                    StringComparison.OrdinalIgnoreCase)) fileSet.Add(path);
            }
            string[] files = fileSet.OrderBy(value => value,
                StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length == 0) throw new InvalidDataException("没有可合并的 DWG 文件。");

            var sources = new List<ImportedDrawing>();
            PreparedDrawing prepared = null;
            try
            {
                // Every source stays detached and read-only on disk. Nothing reaches the
                // user's drawing until collection and the complete RAM layout both succeed.
                foreach (string file in files) sources.Add(ReadSource(file));
                List<ImportedFrame> frames = sources.SelectMany(source => source.Frames).ToList();
                var itemByFrame = new Dictionary<ImportedFrame, XLayoutFrameItem>();
                var items = frames.Select((frame, index) =>
                {
                    var item = new XLayoutFrameItem(frame.MachineId, frame.DeviceName,
                        frame.Boundary, index.ToString(CultureInfo.InvariantCulture));
                    itemByFrame.Add(frame, item);
                    return item;
                }).ToList();
                Dictionary<XLayoutFrameItem, XLayoutPlacement> placements =
                    XLayoutLayout.Arrange(items).ToDictionary(placement => placement.Item);
                prepared = BuildInMemory(sources, itemByFrame, placements);

                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    BlockTableRecord target = transaction.GetObject(database.CurrentSpaceId,
                        OpenMode.ForWrite) as BlockTableRecord;
                    if (target == null) throw new InvalidOperationException("无法打开当前图纸空间。");

                    // Replacing defaults is safe in an empty space and keeps source appearance.
                    // In an existing drawing, reuse same-name symbols and reject incompatible
                    // definitions below; MangleName would create a $N$ block family every run.
                    bool targetHasEntities = target.Cast<ObjectId>().Any();
                    DuplicateRecordCloning policy = targetHasEntities
                        ? DuplicateRecordCloning.Ignore
                        : DuplicateRecordCloning.Replace;
                    if (!targetHasEntities)
                        StyleManager.CopyStandardTextStyle(prepared.Database,
                            database, transaction);
                    var mapping = new IdMapping();
                    prepared.Database.WblockCloneObjects(prepared.EntityIds, target.ObjectId,
                        mapping, policy, false);
                    ValidateClones(prepared.Snapshots, mapping, transaction,
                        "写入当前图纸", true);
                    RecomputeClonedTables(prepared.EntityIds, mapping, transaction);
                    transaction.Commit();
                }

                int unplaced = sources.Sum(source => source.UnplacedEntityCount);
                if (unplaced > 0)
                    Log.Warn("Xmerge 有 " + unplaced + " 个无可用坐标的实体无法随图框移动，"
                        + "已按源坐标导入: "
                        + string.Join("、", sources.Where(source =>
                                source.UnplacedEntityCount > 0)
                            .Select(source => source.Path)));
                return new XmergeResult
                {
                    FileCount = files.Length,
                    FrameCount = frames.Count,
                    EntityCount = prepared.Snapshots.Count,
                    UnplacedEntityCount = unplaced,
                    Files = files
                };
            }
            finally
            {
                prepared?.Dispose();
                foreach (ImportedDrawing source in sources) source.Dispose();
            }
        }

        private static PreparedDrawing BuildInMemory(IReadOnlyList<ImportedDrawing> sources,
            IDictionary<ImportedFrame, XLayoutFrameItem> itemByFrame,
            IDictionary<XLayoutFrameItem, XLayoutPlacement> placements)
        {
            var database = new Database(true, true);
            try
            {
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    BlockTableRecord target = transaction.GetObject(database.CurrentSpaceId,
                        OpenMode.ForWrite) as BlockTableRecord;
                    if (target == null) throw new InvalidOperationException(
                        "无法创建内存聚合图纸。");

                    bool first = true;
                    var moved = new HashSet<ObjectId>();
                    foreach (ImportedDrawing source in sources)
                    {
                        if (first)
                            StyleManager.CopyStandardTextStyle(source.Database, database,
                                transaction);
                        var mapping = new IdMapping();
                        source.Database.WblockCloneObjects(source.EntityIds, target.ObjectId,
                            mapping, first ? DuplicateRecordCloning.Replace
                                : DuplicateRecordCloning.Ignore, false);
                        ValidateClones(source.Snapshots, mapping, transaction,
                            "合并 " + Path.GetFileName(source.Path), true);

                        foreach (ImportedFrame frame in source.Frames)
                        {
                            XLayoutPlacement placement = placements[itemByFrame[frame]];
                            var displacement = new Vector3d(placement.TranslationX,
                                placement.TranslationY, 0d);
                            foreach (ObjectId sourceId in frame.EntityIds)
                            {
                                ObjectId cloneId = MappedId(mapping, sourceId,
                                    Path.GetFileName(source.Path));
                                if (!moved.Add(cloneId)) continue;
                                Entity entity = transaction.GetObject(cloneId,
                                    OpenMode.ForWrite, true) as Entity;
                                if (entity == null || entity.IsErased) continue;
                                entity.TransformBy(Matrix3d.Displacement(displacement));
                            }
                        }
                        first = false;
                    }

                    var ids = new ObjectIdCollection(target.Cast<ObjectId>().ToArray());
                    List<EntitySnapshot> snapshots = CaptureSnapshots(transaction, ids);
                    transaction.Commit();
                    return new PreparedDrawing(database, ids, snapshots);
                }
            }
            catch
            {
                database.Dispose();
                throw;
            }
        }

        private static ImportedDrawing ReadSource(string path)
        {
            var database = new Database(false, true);
            try
            {
                database.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, false, "");
                database.CloseInput(true);
                NormalizeInMemoryBlockNames(database);

                ObjectId modelSpaceId;
                var allIds = new List<ObjectId>();
                var frameIds = new List<ObjectId>();
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    BlockTable blocks = transaction.GetObject(database.BlockTableId,
                        OpenMode.ForRead) as BlockTable;
                    modelSpaceId = blocks?[BlockTableRecord.ModelSpace] ?? ObjectId.Null;
                    BlockTableRecord modelSpace = modelSpaceId.IsNull ? null
                        : transaction.GetObject(modelSpaceId,
                            OpenMode.ForRead) as BlockTableRecord;
                    if (modelSpace != null)
                    {
                        foreach (ObjectId id in modelSpace)
                        {
                            Entity entity = transaction.GetObject(id,
                                OpenMode.ForRead, true) as Entity;
                            if (entity == null || entity.IsErased) continue;
                            allIds.Add(id);
                            if (entity is BlockReference block && !(entity is Table)
                                && IsSourceFrame(transaction, block))
                                frameIds.Add(id);
                        }
                    }
                    transaction.Commit();
                }

                if (allIds.Count == 0)
                    throw new InvalidDataException("DWG 没有模型空间实体: " + path);
                if (frameIds.Count == 0)
                    throw new InvalidDataException("DWG 未识别到图框（"
                        + FrameRegionCollector.SupportedFrameDescription + "）: " + path);

                FrameRegionCollection regions = FrameRegionCollector.CollectForLayout(
                    database, modelSpaceId, frameIds.ToArray());
                if (regions.Errors.Count > 0)
                    throw new InvalidDataException("DWG 图框识别失败: " + path + "；"
                        + string.Join("；", regions.Errors));
                if (regions.Groups.Count == 0)
                    throw new InvalidDataException("DWG 没有有效图框: " + path);

                var frames = new List<ImportedFrame>();
                List<EntitySnapshot> snapshots;
                int unplaced;
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    var definitions = new CadBlockDefinitionReader(transaction);
                    foreach (FrameRegionGroup group in regions.Groups)
                    {
                        SubmissionRecord identity;
                        try
                        {
                            identity = FrameIdentityReader.Read(transaction, group, definitions);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidDataException("DWG 图框 " + group.Handle
                                + " 身份读取失败: " + path + "；" + ex.Message, ex);
                        }
                        string machineId = (identity.MachineId ?? "").Trim();
                        if (machineId.Length == 0)
                            throw new InvalidDataException("DWG 图框 " + group.Handle
                                + " 缺少机台ID: " + path);
                        frames.Add(new ImportedFrame(group, machineId,
                            (identity.DeviceName ?? "").Trim()));
                    }
                    unplaced = AssignRemainingEntities(transaction, allIds, frames);
                    snapshots = CaptureSnapshots(transaction,
                        new ObjectIdCollection(allIds.ToArray()));
                    transaction.Commit();
                }

                return new ImportedDrawing(path, database,
                    new ObjectIdCollection(allIds.ToArray()), frames, snapshots, unplaced);
            }
            catch
            {
                database.Dispose();
                throw;
            }
        }

        private static List<EntitySnapshot> CaptureSnapshots(Transaction transaction,
            ObjectIdCollection ids)
        {
            var result = new List<EntitySnapshot>();
            foreach (ObjectId id in ids ?? new ObjectIdCollection())
            {
                Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                if (entity == null || entity.IsErased) continue;
                result.Add(EntitySnapshot.Capture(transaction, entity));
            }
            return result;
        }

        private static int AssignRemainingEntities(Transaction transaction,
            IReadOnlyList<ObjectId> allIds, IReadOnlyList<ImportedFrame> frames)
        {
            var assigned = new HashSet<ObjectId>(frames.SelectMany(frame => frame.EntityIds));
            foreach (ObjectId id in allIds ?? Array.Empty<ObjectId>())
            {
                if (assigned.Contains(id)) continue;
                Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                ImportedFrame owner = TryEntityCenter(entity, out Point3d center)
                    ? NearestFrame(frames, center)
                    : frames.Count == 1 ? frames[0] : null;
                if (owner == null) continue;
                owner.EntityIds.Add(id);
                assigned.Add(id);
            }
            return Math.Max(0, (allIds?.Count ?? 0) - assigned.Count);
        }

        private static ImportedFrame NearestFrame(IReadOnlyList<ImportedFrame> frames,
            Point3d point)
        {
            ImportedFrame best = null;
            double bestDistance = double.MaxValue;
            foreach (ImportedFrame frame in frames ?? Array.Empty<ImportedFrame>())
            {
                double dx = point.X < frame.Boundary.MinX
                    ? frame.Boundary.MinX - point.X
                    : point.X > frame.Boundary.MaxX ? point.X - frame.Boundary.MaxX : 0d;
                double dy = point.Y < frame.Boundary.MinY
                    ? frame.Boundary.MinY - point.Y
                    : point.Y > frame.Boundary.MaxY ? point.Y - frame.Boundary.MaxY : 0d;
                double distance = dx * dx + dy * dy;
                if (best == null || distance < bestDistance
                    || (Math.Abs(distance - bestDistance) < 0.0000001d
                        && FrameRectangle.CompareReadingOrder(frame.Boundary,
                            best.Boundary) < 0))
                {
                    best = frame;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static bool TryEntityCenter(Entity entity, out Point3d center)
        {
            center = Point3d.Origin;
            if (entity == null || entity.IsErased) return false;
            try
            {
                Extents3d extents = entity.GeometricExtents;
                center = new Point3d((extents.MinPoint.X + extents.MaxPoint.X) / 2d,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2d,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) / 2d);
                if (IsFinite(center)) return true;
            }
            catch { }
            if (entity is BlockReference block && IsFinite(block.Position))
            {
                center = block.Position;
                return true;
            }
            return false;
        }

        private static bool IsFinite(Point3d point)
            => !double.IsNaN(point.X) && !double.IsInfinity(point.X)
                && !double.IsNaN(point.Y) && !double.IsInfinity(point.Y)
                && !double.IsNaN(point.Z) && !double.IsInfinity(point.Z);

        private static void RecomputeClonedTables(ObjectIdCollection sourceIds,
            IdMapping mapping, Transaction transaction)
        {
            foreach (ObjectId sourceId in sourceIds ?? new ObjectIdCollection())
            {
                ObjectId cloneId = MappedId(mapping, sourceId, "刷新合并表格");
                Table table = transaction.GetObject(cloneId,
                    OpenMode.ForRead, true) as Table;
                if (table == null) continue;
                table.UpgradeOpen();
                table.RecomputeTableBlock(true);
            }
        }

        private static void ValidateClones(IReadOnlyList<EntitySnapshot> expected,
            IdMapping mapping, Transaction transaction, string stage,
            bool compareEntityBounds)
        {
            foreach (EntitySnapshot snapshot in expected ?? Array.Empty<EntitySnapshot>())
            {
                ObjectId cloneId = MappedId(mapping, snapshot.Id, stage);
                Entity clone = transaction.GetObject(cloneId, OpenMode.ForRead, true) as Entity;
                if (clone == null || clone.IsErased)
                    throw new InvalidDataException(stage + " 时实体 " + snapshot.Handle
                        + " 未完整复制。");
                EntitySnapshot actual = EntitySnapshot.Capture(transaction, clone);
                if (!snapshot.Matches(actual, compareEntityBounds, out string reason))
                    throw new InvalidDataException(stage + " 时实体 " + snapshot.Handle
                        + " 的同名定义不兼容（" + reason
                        + "）。操作已停止，请使用空白图纸或统一源块版本。");
            }
        }

        private static void NormalizeInMemoryBlockNames(Database database)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTable blocks = transaction.GetObject(database.BlockTableId,
                    OpenMode.ForRead) as BlockTable;
                if (blocks == null) return;
                foreach (ObjectId id in blocks.Cast<ObjectId>().ToArray())
                {
                    BlockTableRecord record = transaction.GetObject(id,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    if (record == null) continue;
                    string name = record.Name ?? "";
                    string cleanName = LogicalName(name);
                    if (cleanName.Length == 0 || cleanName.StartsWith("*",
                        StringComparison.Ordinal) || string.Equals(cleanName, name,
                        StringComparison.Ordinal)) continue;
                    if (blocks.Has(cleanName))
                    {
                        // A suffix already follows the naming contract used by existing
                        // readers. Only convert the old $N$name prefix form here.
                        if (!name.StartsWith("$", StringComparison.Ordinal)) continue;
                        cleanName = NextMangledBlockName(blocks, cleanName);
                    }
                    record.UpgradeOpen();
                    record.Name = cleanName;
                }
                transaction.Commit();
            }
        }

        private static string NextMangledBlockName(SymbolTable table, string cleanName)
        {
            for (int index = 0; index < int.MaxValue; index++)
            {
                string candidate = cleanName + "$"
                    + index.ToString(CultureInfo.InvariantCulture) + "$";
                if (!table.Has(candidate)) return candidate;
            }
            throw new InvalidOperationException("无法为冲突块生成唯一名称: " + cleanName);
        }

        private static ObjectId MappedId(IdMapping mapping, ObjectId sourceId, string stage)
        {
            try
            {
                IdPair pair = mapping[sourceId];
                if (pair.IsCloned && !pair.Value.IsNull && pair.Value.IsValid)
                    return pair.Value;
            }
            catch { }
            throw new InvalidDataException(stage + " 时对象 " + sourceId
                + " 未被完整复制。");
        }

        private static string GetFullPathOrEmpty(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path); }
            catch (Exception ex)
            {
                Log.Warn("Xmerge 忽略无效 DWG 路径: " + path + "，" + ex.Message);
                return "";
            }
        }

        private static string LogicalName(string name)
            => MangledName.Replace(name ?? "", "");

        internal static bool IsSourceFrameName(string name)
            => FrameRegionCollector.IsSupportedFrameName(LogicalName(name));

        private static bool IsSourceFrame(Transaction transaction, BlockReference block)
        {
            if (FrameRegionCollector.IsSupportedFrame(transaction, block)) return true;
            foreach (ObjectId definitionId in FrameRegionCollector.DefinitionIds(block))
            {
                try
                {
                    BlockTableRecord definition = transaction.GetObject(definitionId,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    if (definition != null && IsSourceFrameName(definition.Name)) return true;
                }
                catch { }
            }
            return false;
        }

        private static string AttributeText(AttributeReference attribute)
        {
            if (attribute == null) return "";
            if (!attribute.IsMTextAttribute) return attribute.TextString ?? "";
            using (MText text = attribute.MTextAttribute)
                return text?.Contents ?? attribute.TextString ?? "";
        }

        private static string FormatValue(object value)
        {
            if (value == null) return "<null>";
            if (value is double number)
                return number.ToString("G15", CultureInfo.InvariantCulture);
            if (value is Point3d point)
                return FormatValue(point.X) + "," + FormatValue(point.Y) + ","
                    + FormatValue(point.Z);
            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "";
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        private sealed class EntitySnapshot
        {
            private EntitySnapshot(ObjectId id, string handle, string type,
                BoundsSnapshot bounds, BoundsSnapshot frameBoundary, string blockState,
                string styleSignature)
            {
                Id = id;
                Handle = handle;
                Type = type;
                Bounds = bounds;
                FrameBoundary = frameBoundary;
                BlockState = blockState;
                StyleSignature = styleSignature;
            }

            public ObjectId Id { get; }
            public string Handle { get; }
            private string Type { get; }
            private BoundsSnapshot Bounds { get; }
            private BoundsSnapshot FrameBoundary { get; }
            private string BlockState { get; }
            private string StyleSignature { get; }

            public static EntitySnapshot Capture(Transaction transaction, Entity entity)
            {
                BlockReference block = entity as BlockReference;
                string state = block != null && !(entity is Table)
                    ? CaptureBlockState(transaction, block) : "";
                BoundsSnapshot frameBoundary = null;
                if (block != null && !(entity is Table) && IsSourceFrame(transaction, block))
                {
                    try
                    {
                        frameBoundary = BoundsSnapshot.FromFrame(
                            FrameRegionCollector.ReadBoundary(transaction, block));
                    }
                    catch { }
                }
                return new EntitySnapshot(entity.ObjectId, entity.Handle.ToString(),
                    entity.GetType().FullName ?? entity.GetType().Name,
                    BoundsSnapshot.TryCapture(entity),
                    frameBoundary, state,
                    CaptureStyleSignature(transaction, entity));
            }

            public bool Matches(EntitySnapshot other, bool compareEntityBounds,
                out string reason)
            {
                if (other == null || !string.Equals(Type, other.Type,
                    StringComparison.Ordinal))
                {
                    reason = "实体类型变化";
                    return false;
                }
                if (FrameBoundary != null && (other.FrameBoundary == null
                    || !FrameBoundary.NearlyEquals(other.FrameBoundary)))
                {
                    reason = "图框边界变化 " + FrameBoundary + " -> "
                        + (other.FrameBoundary?.ToString() ?? "<none>");
                    return false;
                }
                if (compareEntityBounds && Bounds != null
                    && (other.Bounds == null || !Bounds.NearlyEquals(other.Bounds)))
                {
                    reason = Type + " 几何范围变化";
                    return false;
                }
                if (!string.Equals(BlockState, other.BlockState, StringComparison.Ordinal))
                {
                    reason = "块属性或动态参数变化 "
                        + DescribeStateDifference(BlockState, other.BlockState);
                    return false;
                }
                if (!string.Equals(StyleSignature, other.StyleSignature,
                    StringComparison.Ordinal))
                {
                    reason = "实体线型或文字样式不兼容（源: "
                        + (StyleSignature ?? "<none>") + " -> 实际: "
                        + (other.StyleSignature ?? "<none>") + "）";
                    return false;
                }
                reason = "";
                return true;
            }

            private static string DescribeStateDifference(string expected, string actual)
            {
                string[] before = (expected ?? "").Split(
                    new[] { '\u001d' }, StringSplitOptions.RemoveEmptyEntries);
                string[] after = (actual ?? "").Split(
                    new[] { '\u001d' }, StringSplitOptions.RemoveEmptyEntries);
                string removed = before.Except(after, StringComparer.Ordinal).FirstOrDefault();
                string added = after.Except(before, StringComparer.Ordinal).FirstOrDefault();
                return "[" + DisplayState(removed) + "] -> [" + DisplayState(added) + "]";
            }

            private static string DisplayState(string value)
            {
                string result = (value ?? "<none>").Replace('\u001f', '|')
                    .Replace('\u001e', ',');
                return result.Length <= 240 ? result : result.Substring(0, 240) + "...";
            }

            /// <summary>
            /// Captures the named symbol definitions a cloned entity resolves to in its
            /// own database: the linetype identity and, for text, the text style that
            /// produced the visible appearance.  In Ignore mode WblockCloneObjects reuses
            /// same-name records, so a clone can silently resolve to the target's version;
            /// comparing the two signatures makes that detectable.
            /// </summary>
            private static string CaptureStyleSignature(Transaction transaction,
                Entity entity)
            {
                if (entity == null) return "";
                var parts = new List<string>();
                try
                {
                    ObjectId linetypeId = entity.LinetypeId;
                    string linetypeName = linetypeId.IsNull ? "ByLayer"
                        : ReadSymbolName(transaction, linetypeId);
                    parts.Add("L:" + linetypeName);
                }
                catch { }

                string style = entity is DBText text
                    ? ReadStyleSignature(transaction, text.TextStyleId)
                    : entity is MText mText
                        ? ReadStyleSignature(transaction, mText.TextStyleId)
                        : null;
                if (style != null) parts.Add("T:" + style);

                return parts.Count == 0 ? "" : string.Join("|", parts);
            }

            private static string ReadSymbolName(Transaction transaction, ObjectId id)
            {
                try
                {
                    SymbolTableRecord record = transaction.GetObject(id,
                        OpenMode.ForRead, true) as SymbolTableRecord;
                    return record != null ? LogicalName(record.Name) : id.ToString();
                }
                catch { return id.ToString(); }
            }

            private static string ReadStyleSignature(Transaction transaction,
                ObjectId styleId)
            {
                if (styleId.IsNull) return null;
                try
                {
                    TextStyleTableRecord style = transaction.GetObject(styleId,
                        OpenMode.ForRead, true) as TextStyleTableRecord;
                    if (style == null) return null;
                    return (style.FileName ?? "") + ":" + (style.BigFontFileName ?? "")
                        + ":" + FormatValue(style.XScale);
                }
                catch { return null; }
            }

            private static string CaptureBlockState(Transaction transaction,
                BlockReference block)
            {
                var values = new List<string>
                {
                    "name=" + DefinitionName(transaction, block),
                    "position=" + FormatValue(block.Position),
                    "rotation=" + FormatValue(block.Rotation),
                    "scale=" + FormatValue(block.ScaleFactors.X) + ","
                        + FormatValue(block.ScaleFactors.Y) + ","
                        + FormatValue(block.ScaleFactors.Z)
                };

                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    AttributeReference attribute = transaction.GetObject(attributeId,
                        OpenMode.ForRead, true) as AttributeReference;
                    if (attribute != null)
                        values.Add("attribute=" + (attribute.Tag ?? "") + "\u001f"
                            + AttributeText(attribute));
                }

                foreach (ObjectId definitionId in FrameRegionCollector.DefinitionIds(block))
                {
                    BlockTableRecord definition = transaction.GetObject(definitionId,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    if (definition == null) continue;
                    foreach (ObjectId childId in definition)
                    {
                        AttributeDefinition attribute = transaction.GetObject(childId,
                            OpenMode.ForRead, true) as AttributeDefinition;
                        // Non-constant definitions are schema only; their per-instance
                        // AttributeReference values are compared above. Old and new frames
                        // may legitimately differ by the added FRAMEINFO_JSON definition.
                        if (attribute != null && attribute.Constant)
                            values.Add("definition-attribute=" + (attribute.Tag ?? "")
                                + "\u001f" + attribute.Constant + "\u001f"
                                + (attribute.TextString ?? ""));
                    }
                }

                if (block.IsDynamicBlock)
                {
                    foreach (DynamicBlockReferenceProperty property
                        in block.DynamicBlockReferencePropertyCollection)
                    {
                        string[] allowed;
                        try
                        {
                            allowed = (property.GetAllowedValues() ?? Array.Empty<object>())
                                .Select(FormatValue).OrderBy(value => value,
                                    StringComparer.Ordinal).ToArray();
                        }
                        catch
                        {
                            allowed = Array.Empty<string>();
                        }
                        values.Add("dynamic=" + (property.PropertyName ?? "") + "\u001f"
                            + property.ReadOnly + "\u001f" + FormatValue(property.Value)
                            + "\u001f" + string.Join("\u001e", allowed));
                    }
                }
                values.Sort(StringComparer.Ordinal);
                return string.Join("\u001d", values);
            }

            private static string DefinitionName(Transaction transaction,
                BlockReference block)
            {
                ObjectId id = block.BlockTableRecord;
                try
                {
                    if (block.IsDynamicBlock && !block.DynamicBlockTableRecord.IsNull)
                        id = block.DynamicBlockTableRecord;
                }
                catch { }
                BlockTableRecord definition = transaction.GetObject(id,
                    OpenMode.ForRead, true) as BlockTableRecord;
                return LogicalName(definition?.Name);
            }
        }

        private sealed class BoundsSnapshot
        {
            private BoundsSnapshot(Extents3d extents)
            {
                MinX = extents.MinPoint.X;
                MinY = extents.MinPoint.Y;
                MinZ = extents.MinPoint.Z;
                MaxX = extents.MaxPoint.X;
                MaxY = extents.MaxPoint.Y;
                MaxZ = extents.MaxPoint.Z;
            }

            private double MinX { get; }
            private double MinY { get; }
            private double MinZ { get; }
            private double MaxX { get; }
            private double MaxY { get; }
            private double MaxZ { get; }

            public static BoundsSnapshot TryCapture(Entity entity)
            {
                try { return new BoundsSnapshot(entity.GeometricExtents); }
                catch { return null; }
            }

            public static BoundsSnapshot FromFrame(FrameRectangle frame)
            {
                if (frame == null) return null;
                return new BoundsSnapshot(new Extents3d(
                    new Point3d(frame.MinX, frame.MinY, 0d),
                    new Point3d(frame.MaxX, frame.MaxY, 0d)));
            }

            public bool NearlyEquals(BoundsSnapshot other)
            {
                if (other == null) return false;
                double scale = new[] { MinX, MinY, MinZ, MaxX, MaxY, MaxZ,
                    other.MinX, other.MinY, other.MinZ, other.MaxX, other.MaxY,
                    other.MaxZ }.Select(Math.Abs).Concat(new[] { 1d }).Max();
                double tolerance = scale * 0.00000001d;
                return Math.Abs(MinX - other.MinX) <= tolerance
                    && Math.Abs(MinY - other.MinY) <= tolerance
                    && Math.Abs(MinZ - other.MinZ) <= tolerance
                    && Math.Abs(MaxX - other.MaxX) <= tolerance
                    && Math.Abs(MaxY - other.MaxY) <= tolerance
                    && Math.Abs(MaxZ - other.MaxZ) <= tolerance;
            }

            public override string ToString()
                => "[" + FormatValue(MinX) + "," + FormatValue(MinY) + ","
                    + FormatValue(MinZ) + "]-[" + FormatValue(MaxX) + ","
                    + FormatValue(MaxY) + "," + FormatValue(MaxZ) + "]";
        }

        private sealed class ImportedFrame
        {
            public ImportedFrame(FrameRegionGroup group, string machineId, string deviceName)
            {
                Boundary = group.Boundary;
                MachineId = machineId ?? "";
                DeviceName = deviceName ?? "";
                EntityIds = group.EntityIds.Distinct().ToList();
            }

            public FrameRectangle Boundary { get; }
            public string MachineId { get; }
            public string DeviceName { get; }
            public List<ObjectId> EntityIds { get; }
        }

        private sealed class ImportedDrawing : IDisposable
        {
            public ImportedDrawing(string path, Database database,
                ObjectIdCollection entityIds, List<ImportedFrame> frames,
                List<EntitySnapshot> snapshots, int unplacedEntityCount)
            {
                Path = path;
                Database = database;
                EntityIds = entityIds;
                Frames = frames;
                Snapshots = snapshots;
                UnplacedEntityCount = unplacedEntityCount;
            }

            public string Path { get; }
            public Database Database { get; }
            public ObjectIdCollection EntityIds { get; }
            public List<ImportedFrame> Frames { get; }
            public List<EntitySnapshot> Snapshots { get; }
            public int UnplacedEntityCount { get; }

            public void Dispose()
            {
                try { Database.Dispose(); } catch { }
            }
        }

        private sealed class PreparedDrawing : IDisposable
        {
            public PreparedDrawing(Database database, ObjectIdCollection entityIds,
                List<EntitySnapshot> snapshots)
            {
                Database = database;
                EntityIds = entityIds;
                Snapshots = snapshots;
            }

            public Database Database { get; }
            public ObjectIdCollection EntityIds { get; }
            public List<EntitySnapshot> Snapshots { get; }

            public void Dispose() => Database.Dispose();
        }
    }
}
