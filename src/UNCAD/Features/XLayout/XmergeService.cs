using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Cad;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.Infra;

namespace UNCAD.Features.XLayout
{
    internal sealed class XmergeResult
    {
        public int FileCount { get; set; }
        public int FrameCount { get; set; }
        public int EntityCount { get; set; }
        public string[] Files { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Imports DWG model-space entities and arranges every discovered frame with the
    /// same machine-row/spacing contract as XLAYOUT. WblockCloneObjects keeps dependent
    /// block definitions, layers and styles intact in the active database.
    /// </summary>
    internal static class XmergeService
    {
        public static XmergeResult Merge(CadContext ctx, IEnumerable<string> filePaths)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            string activeDrawing = GetFullPathOrEmpty(ctx.Db.Filename);
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

            var imported = new List<ImportedDrawing>();
            try
            {
                foreach (string file in files) imported.Add(ReadSource(file));
                var frameInputs = imported.SelectMany(drawing => drawing.Frames).ToList();
                var itemByFrame = new Dictionary<ImportedFrame, XLayoutFrameItem>();
                var items = frameInputs.Select((frame, index) =>
                {
                    var item = new XLayoutFrameItem(frame.MachineId, frame.DeviceName,
                        frame.Boundary, index.ToString());
                    itemByFrame[frame] = item;
                    return item;
                }).ToList();
                IReadOnlyList<XLayoutPlacement> placements = XLayoutLayout.Arrange(items);
                var placementByItem = placements.ToDictionary(item => item.Item);

                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord target = transaction.GetObject(ctx.CurrentSpaceId,
                        OpenMode.ForWrite) as BlockTableRecord;
                    if (target == null) throw new InvalidOperationException("无法打开当前图纸空间。");
                    int entities = 0;
                    foreach (ImportedDrawing drawing in imported)
                    {
                        var mapping = new IdMapping();
                        drawing.Database.WblockCloneObjects(drawing.EntityIds, target.ObjectId,
                            mapping, DuplicateRecordCloning.MangleName, false);
                        foreach (ImportedFrame frame in drawing.Frames)
                        {
                            XLayoutPlacement placement = placementByItem[itemByFrame[frame]];
                            Vector3d displacement = new Vector3d(placement.TranslationX,
                                placement.TranslationY, 0d);
                            foreach (ObjectId sourceId in frame.EntityIds)
                            {
                                IdPair pair = mapping[sourceId];
                                if (!pair.IsCloned || pair.Value.IsNull || !pair.Value.IsValid) continue;
                                Entity entity = transaction.GetObject(pair.Value, OpenMode.ForWrite,
                                    true) as Entity;
                                if (entity == null || entity.IsErased) continue;
                                entity.TransformBy(Matrix3d.Displacement(displacement));
                                entities++;
                            }
                        }
                    }
                    transaction.Commit();
                    return new XmergeResult { FileCount = files.Length,
                        FrameCount = frameInputs.Count, EntityCount = entities, Files = files };
                }
            }
            finally
            {
                foreach (ImportedDrawing drawing in imported)
                    try { drawing.Database.Dispose(); } catch { }
            }
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

        private static ImportedDrawing ReadSource(string path)
        {
            var database = new Database(false, true);
            try
            {
                database.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, false, "");
                database.CloseInput(true);
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    var blockTable = transaction.GetObject(database.BlockTableId,
                        OpenMode.ForRead) as BlockTable;
                    ObjectId modelSpaceId = blockTable?[BlockTableRecord.ModelSpace]
                        ?? ObjectId.Null;
                    BlockTableRecord space = modelSpaceId.IsNull ? null
                        : transaction.GetObject(modelSpaceId,
                            OpenMode.ForRead) as BlockTableRecord;
                    var ids = new ObjectIdCollection();
                    var entities = new List<SourceEntity>();
                    if (space != null)
                    {
                        foreach (ObjectId id in space)
                        {
                            Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                            if (entity == null || entity.IsErased) continue;
                            ids.Add(id);
                            if (TryExtents(entity, out FrameRectangle bounds))
                                entities.Add(new SourceEntity(id, bounds));
                        }
                    }
                    if (ids.Count == 0) throw new InvalidDataException(
                        "DWG 没有可导入的模型空间实体: " + path);

                    var frames = entities.Where(item => IsFrame(transaction, item.Id))
                        .Select(item => new ImportedFrame(item.Id, item.Bounds,
                            ReadMachineId(transaction, item.Id, path),
                            Path.GetFileNameWithoutExtension(path))).ToList();
                    if (frames.Count == 0)
                    {
                        FrameRectangle all = Union(entities.Select(item => item.Bounds), path);
                        frames.Add(new ImportedFrame(ObjectId.Null, all,
                            Path.GetFileNameWithoutExtension(path),
                            Path.GetFileNameWithoutExtension(path)));
                    }
                    foreach (SourceEntity entity in entities)
                    {
                        double x = entity.Bounds.MinX + entity.Bounds.Width / 2d;
                        double y = entity.Bounds.MinY + entity.Bounds.Height / 2d;
                        ImportedFrame owner = frames.FirstOrDefault(frame =>
                            frame.Boundary.Contains(x, y));
                        if (owner == null) owner = frames.OrderBy(frame => Distance(
                            frame.Boundary, entity.Bounds)).First();
                        owner.EntityIds.Add(entity.Id);
                    }
                    transaction.Commit();
                    return new ImportedDrawing(path, database, ids, frames);
                }
            }
            catch { database.Dispose(); throw; }
        }

        private static bool IsFrame(Transaction transaction, ObjectId id)
        {
            var block = transaction.GetObject(id, OpenMode.ForRead, true) as BlockReference;
            if (block == null) return false;
            try
            {
                ObjectId definitionId = block.IsDynamicBlock
                    ? block.DynamicBlockTableRecord : block.BlockTableRecord;
                BlockTableRecord definition = transaction.GetObject(definitionId,
                    OpenMode.ForRead, true) as BlockTableRecord;
                return FrameRegionCollector.IsSupportedFrameName(definition?.Name);
            }
            catch { return false; }
        }

        private static string ReadMachineId(Transaction transaction, ObjectId id, string path)
        {
            var block = transaction.GetObject(id, OpenMode.ForRead, true) as BlockReference;
            if (block != null)
            {
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    var attribute = transaction.GetObject(attributeId, OpenMode.ForRead, true)
                        as AttributeReference;
                    if (attribute == null) continue;
                    string tag = attribute.Tag ?? "";
                    string value = (attribute.TextString ?? "").Trim();
                    if (tag.Equals("MACHINEID-POWER", StringComparison.OrdinalIgnoreCase)
                        && value.EndsWith("-POWER", StringComparison.OrdinalIgnoreCase))
                        return value.Substring(0, value.Length - 6).TrimEnd('-').Trim();
                    if (tag.Equals("MACHINEID-DEVICE", StringComparison.OrdinalIgnoreCase))
                    {
                        int dash = value.IndexOf('-');
                        if (dash > 0) return value.Substring(0, dash).Trim();
                    }
                }
            }
            return Path.GetFileNameWithoutExtension(path);
        }

        private static bool TryExtents(Entity entity, out FrameRectangle bounds)
        {
            bounds = null;
            try
            {
                Extents3d extents = entity.GeometricExtents;
                bounds = new FrameRectangle(entity.Handle.ToString(), extents.MinPoint.X,
                    extents.MinPoint.Y, extents.MaxPoint.X, extents.MaxPoint.Y);
                return true;
            }
            catch { return false; }
        }

        private static FrameRectangle Union(IEnumerable<FrameRectangle> values, string key)
        {
            var list = (values ?? Enumerable.Empty<FrameRectangle>()).ToList();
            if (list.Count == 0) return new FrameRectangle(key, 0, 0, 1, 1);
            return new FrameRectangle(key, list.Min(value => value.MinX), list.Min(value => value.MinY),
                list.Max(value => value.MaxX), list.Max(value => value.MaxY));
        }

        private static double Distance(FrameRectangle a, FrameRectangle b)
        {
            double dx = (a.MinX + a.MaxX - b.MinX - b.MaxX) / 2d;
            double dy = (a.MinY + a.MaxY - b.MinY - b.MaxY) / 2d;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class SourceEntity
        {
            public SourceEntity(ObjectId id, FrameRectangle bounds) { Id = id; Bounds = bounds; }
            public ObjectId Id { get; }
            public FrameRectangle Bounds { get; }
        }

        private sealed class ImportedFrame
        {
            public ImportedFrame(ObjectId frameId, FrameRectangle boundary,
                string machineId, string deviceName)
            {
                FrameId = frameId; Boundary = boundary; MachineId = machineId ?? "";
                DeviceName = deviceName ?? "";
            }
            public ObjectId FrameId { get; }
            public FrameRectangle Boundary { get; }
            public string MachineId { get; }
            public string DeviceName { get; }
            public List<ObjectId> EntityIds { get; } = new List<ObjectId>();
        }

        private sealed class ImportedDrawing
        {
            public ImportedDrawing(string path, Database database, ObjectIdCollection entityIds,
                List<ImportedFrame> frames)
            {
                Path = path; Database = database; EntityIds = entityIds; Frames = frames;
            }
            public string Path { get; }
            public Database Database { get; }
            public ObjectIdCollection EntityIds { get; }
            public List<ImportedFrame> Frames { get; }
        }
    }
}
