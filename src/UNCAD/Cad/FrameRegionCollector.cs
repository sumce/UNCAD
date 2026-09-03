using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.Fill;
using UNCAD.Core.Geometry;

namespace UNCAD.Cad
{
    /// <summary>Objects assigned to one selected frame_20260812 boundary.</summary>
    internal sealed class FrameRegionGroup
    {
        public ObjectId FrameId { get; set; }
        public string Handle { get; set; } = "";
        public FrameRectangle Boundary { get; set; }
        public List<ObjectId> EntityIds { get; } = new List<ObjectId>();
    }

    /// <summary>Spatial collection result shared by batch update and batch submission.</summary>
    internal sealed class FrameRegionCollection
    {
        public int SelectedFrameCount { get; set; }
        public List<FrameRegionGroup> Groups { get; } = new List<FrameRegionGroup>();
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Uses selected frame_20260812 blocks as containers, then scans the current space once to
    /// assign tables, text and blocks. Selection membership is not trusted because a wide window
    /// can include unrelated upstream blocks outside the frame boundary.
    /// </summary>
    internal static class FrameRegionCollector
    {
        public const string SupportedFrameName = "frame_20260812";
        public const string XFrameName = "xframe";

        internal static bool IsSupportedFrameName(string name)
            => string.Equals(name, SupportedFrameName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, XFrameName, StringComparison.OrdinalIgnoreCase);

        private sealed class FrameBorderBounds
        {
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
        }

        /// <summary>
        /// Small uniform grid used while assigning model-space entities to frames.
        /// The previous implementation compared every entity with every frame.  A frame
        /// normally occupies only one or two cells, so the common path now checks a tiny
        /// candidate set while preserving exact boundary checks below.
        /// </summary>
        private sealed class FrameBoundaryIndex
        {
            private const int MaxIndexedCells = 64;
            private readonly double _cellWidth;
            private readonly double _cellHeight;
            private readonly Dictionary<GridKey, List<FrameRegionGroup>> _buckets =
                new Dictionary<GridKey, List<FrameRegionGroup>>();
            private readonly List<FrameRegionGroup> _overflow =
                new List<FrameRegionGroup>();
            private readonly FrameRegionGroup[] _all;

            public FrameBoundaryIndex(IReadOnlyList<FrameRegionGroup> groups)
            {
                _all = (groups ?? Array.Empty<FrameRegionGroup>()).ToArray();
                _cellWidth = CellSize(_all.Select(group => group.Boundary.Width));
                _cellHeight = CellSize(_all.Select(group => group.Boundary.Height));
                foreach (FrameRegionGroup group in _all)
                {
                    if (group.Boundary.Width > _cellWidth
                        || group.Boundary.Height > _cellHeight)
                    {
                        _overflow.Add(group);
                        continue;
                    }
                    var key = new GridKey(CellX((group.Boundary.MinX
                            + group.Boundary.MaxX) / 2d),
                        CellY((group.Boundary.MinY + group.Boundary.MaxY) / 2d));
                    if (!_buckets.TryGetValue(key, out List<FrameRegionGroup> bucket))
                    {
                        bucket = new List<FrameRegionGroup>();
                        _buckets.Add(key, bucket);
                    }
                    bucket.Add(group);
                }
            }

            public List<FrameRegionGroup> QueryPoint(double x, double y)
            {
                var candidates = new List<FrameRegionGroup>();
                long centerX = CellX(x);
                long centerY = CellY(y);
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                        if (_buckets.TryGetValue(new GridKey(centerX + dx, centerY + dy),
                            out List<FrameRegionGroup> bucket))
                            candidates.AddRange(bucket);
                foreach (FrameRegionGroup group in _overflow)
                    candidates.Add(group);
                return candidates;
            }

            public List<FrameRegionGroup> QueryBounds(double minX, double minY,
                double maxX, double maxY)
            {
                long firstX = CellX(minX);
                long lastX = CellX(maxX);
                long firstY = CellY(minY);
                long lastY = CellY(maxY);
                if (firstX > long.MinValue) firstX--;
                if (lastX < long.MaxValue) lastX++;
                if (firstY > long.MinValue) firstY--;
                if (lastY < long.MaxValue) lastY++;
                long cellCount = (lastX - firstX + 1L) * (lastY - firstY + 1L);
                if (cellCount <= 0 || cellCount > MaxIndexedCells)
                    return _all.ToList();

                var candidates = new List<FrameRegionGroup>();
                var seen = new HashSet<FrameRegionGroup>();
                for (long x = firstX; x <= lastX; x++)
                    for (long y = firstY; y <= lastY; y++)
                        if (_buckets.TryGetValue(new GridKey(x, y),
                            out List<FrameRegionGroup> bucket))
                            foreach (FrameRegionGroup group in bucket)
                                if (seen.Add(group)) candidates.Add(group);
                foreach (FrameRegionGroup group in _overflow)
                    if (seen.Add(group)) candidates.Add(group);
                return candidates;
            }

            private long CellX(double value) => ToCell(value, _cellWidth);
            private long CellY(double value) => ToCell(value, _cellHeight);

            private static double CellSize(IEnumerable<double> values)
            {
                double[] valid = (values ?? Enumerable.Empty<double>())
                    .Where(item => item > 0 && !double.IsNaN(item)
                        && !double.IsInfinity(item))
                    .OrderBy(item => item).ToArray();
                return valid.Length == 0 ? 1d : valid[valid.Length / 2];
            }

            private static long ToCell(double value, double size)
            {
                double cell = Math.Floor(value / size);
                if (cell <= long.MinValue) return long.MinValue;
                if (cell >= long.MaxValue) return long.MaxValue;
                return (long)cell;
            }

            private struct GridKey : IEquatable<GridKey>
            {
                private readonly long _x;
                private readonly long _y;

                public GridKey(long x, long y) { _x = x; _y = y; }
                public bool Equals(GridKey other) => _x == other._x && _y == other._y;
                public override bool Equals(object obj)
                    => obj is GridKey other && Equals(other);
                public override int GetHashCode()
                    => (_x.GetHashCode() * 397) ^ _y.GetHashCode();
            }
        }

        public static FrameRegionCollection Collect(CadContext ctx, ObjectId[] selectedIds)
            => CollectCore(ctx, selectedIds, false, true);

        /// <summary>Collects every model-space entity for style-preserving DWG export.</summary>
        public static FrameRegionCollection CollectForExport(CadContext ctx, ObjectId[] selectedIds)
            => CollectCore(ctx, selectedIds, true, true);

        /// <summary>Collects all entities needed to move selected frames in XLAYOUT.</summary>
        public static FrameRegionCollection CollectForLayout(CadContext ctx, ObjectId[] selectedIds)
            => CollectCore(ctx, selectedIds, true, true);

        private static FrameRegionCollection CollectCore(CadContext ctx, ObjectId[] selectedIds,
            bool includeAllEntities, bool useAnchorOwnership)
        {
            var result = new FrameRegionCollection();
            if (ctx == null || selectedIds == null || selectedIds.Length == 0) return result;

            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var selectedFrames = new HashSet<ObjectId>();
                var boundaryCache = new Dictionary<ObjectId, FrameBorderBounds>();
                foreach (ObjectId id in selectedIds.Distinct())
                {
                    BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                        as BlockReference;
                    if (block == null || block.OwnerId != ctx.Db.CurrentSpaceId
                        || !IsSupportedFrame(transaction, block)) continue;
                    selectedFrames.Add(id);
                    result.SelectedFrameCount++;
                    AddFrame(result, transaction, block, boundaryCache);
                }

                if (result.Groups.Count == 0) return result;
                if (result.Errors.Count > 0) return result;

                BlockTableRecord space = transaction.GetObject(ctx.Db.CurrentSpaceId,
                    OpenMode.ForRead) as BlockTableRecord;
                if (space == null)
                {
                    result.Errors.Add("无法读取当前空间，未执行图框分区。");
                    return result;
                }

                FrameRegionGroup[] groups = result.Groups.ToArray();
                var boundaryIndex = new FrameBoundaryIndex(groups);
                foreach (ObjectId id in space)
                {
                    if (selectedFrames.Contains(id)) continue;
                    Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                    if (useAnchorOwnership)
                    {
                        if (!TryAnchor(entity, out Point3d anchor, includeAllEntities)) continue;
                        FrameRegionGroup owner = null;
                        List<FrameRegionGroup> owners = null;
                        foreach (FrameRegionGroup group in boundaryIndex.QueryPoint(
                            anchor.X, anchor.Y))
                        {
                            if (!group.Boundary.Contains(anchor.X, anchor.Y)) continue;
                            if (owner == null) owner = group;
                            else
                            {
                                if (owners == null)
                                    owners = new List<FrameRegionGroup> { owner };
                                owners.Add(group);
                            }
                        }
                        if (owners == null) owner?.EntityIds.Add(id);
                        else SelectAnchorOwner(owners, anchor).EntityIds.Add(id);
                        continue;
                    }

                    if (!TryBounds(entity, out double minX, out double minY,
                        out double maxX, out double maxY, includeAllEntities)) continue;

                    List<FrameRegionGroup> strictOwners = boundaryIndex.QueryBounds(
                        minX, minY, maxX, maxY).Where(group => group.Boundary.Intersects(minX, minY, maxX, maxY)).ToList();
                    if (strictOwners.Count == 1)
                    {
                        strictOwners[0].EntityIds.Add(id);
                    }
                    else if (strictOwners.Count > 1)
                    {
                        // Unrelated inserts do not affect fill or submission and must not make
                        // otherwise valid adjacent frames fail. Tables, text and tagged blocks
                        // remain hard conflicts because assigning them by guess would corrupt data.
                        if (entity is BlockReference candidate
                            && !HasKnownFillTag(transaction, candidate)) continue;
                        result.Errors.Add("对象 " + entity.Handle + " 同时位于图框 "
                            + string.Join("、", strictOwners.Select(owner => owner.Handle)) + " 内。");
                    }
                }
            }

            result.Groups.Sort((left, right) => FrameRectangle.CompareReadingOrder(
                left.Boundary, right.Boundary));
            return result;
        }

        private static void AddFrame(FrameRegionCollection result, Transaction transaction,
            BlockReference block, Dictionary<ObjectId, FrameBorderBounds> boundaryCache)
        {
            if (Math.Abs(block.Rotation) > 0.0000001)
            {
                result.Errors.Add("图框 " + block.Handle + " 已旋转，不能使用轴对齐边界批量处理。");
                return;
            }

            try
            {
                FrameRectangle boundary = ReadBorderBoundary(transaction, block, boundaryCache);
                var group = new FrameRegionGroup
                {
                    FrameId = block.ObjectId,
                    Handle = block.Handle.ToString(),
                    Boundary = boundary
                };
                // The owning frame must be visible to existing identity and submission readers.
                group.EntityIds.Add(block.ObjectId);
                result.Groups.Add(group);
            }
            catch (System.Exception ex)
            {
                result.Errors.Add("无法读取图框 " + block.Handle + " 的当前边界：" + ex.Message);
            }
        }

        private static FrameRectangle ReadBorderBoundary(Transaction transaction,
            BlockReference block, Dictionary<ObjectId, FrameBorderBounds> boundaryCache)
        {
            // frame_20260812 owns an explicit four-line outer rectangle. Reading the line border
            // avoids attributes or annotation geometry expanding BlockReference.GeometricExtents.
            var lines = new List<Line>();
            ObjectId definitionId = block.BlockTableRecord;
            if (boundaryCache != null && boundaryCache.TryGetValue(definitionId,
                out FrameBorderBounds cached))
            {
                return TransformBorder(block, cached);
            }
            BlockTableRecord definition = transaction.GetObject(definitionId,
                OpenMode.ForRead, true) as BlockTableRecord;
            if (definition == null) throw new InvalidOperationException("找不到图框块定义。");
            foreach (ObjectId id in definition)
            {
                Line line = transaction.GetObject(id, OpenMode.ForRead, true) as Line;
                if (line != null) lines.Add(line);
            }
            if (lines.Count < 4) throw new InvalidOperationException("图框定义没有完整外框直线。");

            double minX = lines.Min(line => Math.Min(line.StartPoint.X, line.EndPoint.X));
            double minY = lines.Min(line => Math.Min(line.StartPoint.Y, line.EndPoint.Y));
            double maxX = lines.Max(line => Math.Max(line.StartPoint.X, line.EndPoint.X));
            double maxY = lines.Max(line => Math.Max(line.StartPoint.Y, line.EndPoint.Y));
            double tolerance = Math.Max(0.000001,
                Math.Max(maxX - minX, maxY - minY) * 0.00000001);
            bool top = lines.Any(line => IsHorizontalBorder(line, maxY, minX, maxX, tolerance));
            bool bottom = lines.Any(line => IsHorizontalBorder(line, minY, minX, maxX, tolerance));
            bool left = lines.Any(line => IsVerticalBorder(line, minX, minY, maxY, tolerance));
            bool right = lines.Any(line => IsVerticalBorder(line, maxX, minY, maxY, tolerance));
            if (!top || !bottom || !left || !right)
                throw new InvalidOperationException("图框定义未找到闭合的最外矩形边线。");

            var bounds = new FrameBorderBounds
            {
                MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY
            };
            if (boundaryCache != null) boundaryCache[definitionId] = bounds;
            return TransformBorder(block, bounds);
        }

        private static FrameRectangle TransformBorder(BlockReference block,
            FrameBorderBounds bounds)
        {
            Point3d[] corners =
            {
                new Point3d(bounds.MinX, bounds.MinY, 0).TransformBy(block.BlockTransform),
                new Point3d(bounds.MinX, bounds.MaxY, 0).TransformBy(block.BlockTransform),
                new Point3d(bounds.MaxX, bounds.MinY, 0).TransformBy(block.BlockTransform),
                new Point3d(bounds.MaxX, bounds.MaxY, 0).TransformBy(block.BlockTransform)
            };
            return new FrameRectangle(block.Handle.ToString(),
                corners.Min(point => point.X), corners.Min(point => point.Y),
                corners.Max(point => point.X), corners.Max(point => point.Y));
        }

        private static bool IsHorizontalBorder(Line line, double y, double minX,
            double maxX, double tolerance)
            => Math.Abs(line.StartPoint.Y - y) <= tolerance
                && Math.Abs(line.EndPoint.Y - y) <= tolerance
                && Math.Abs(Math.Min(line.StartPoint.X, line.EndPoint.X) - minX) <= tolerance
                && Math.Abs(Math.Max(line.StartPoint.X, line.EndPoint.X) - maxX) <= tolerance;

        private static bool IsVerticalBorder(Line line, double x, double minY,
            double maxY, double tolerance)
            => Math.Abs(line.StartPoint.X - x) <= tolerance
                && Math.Abs(line.EndPoint.X - x) <= tolerance
                && Math.Abs(Math.Min(line.StartPoint.Y, line.EndPoint.Y) - minY) <= tolerance
                && Math.Abs(Math.Max(line.StartPoint.Y, line.EndPoint.Y) - maxY) <= tolerance;

        private static bool HasKnownFillTag(Transaction transaction, BlockReference block)
        {
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                AttributeReference attribute = transaction.GetObject(attributeId,
                    OpenMode.ForRead, true) as AttributeReference;
                if (attribute == null) continue;
                if (FrameBlockFiller.IsKnownTag(attribute.Tag)
                    || string.Equals(attribute.Tag, DeviceBlockFiller.TagDeviceName,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(attribute.Tag, ConnectionBlockFiller.TagUpstreamInfo,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(attribute.Tag, ConnectionBlockFiller.TagUpstreamAxis,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(attribute.Tag, ConnectionBlockFiller.TagDownstreamAxis,
                        StringComparison.OrdinalIgnoreCase)) return true;
            }
            ObjectId recordId = block.IsDynamicBlock
                ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            BlockTableRecord record = transaction.GetObject(recordId, OpenMode.ForRead, true)
                as BlockTableRecord;
            if (record != null && string.Equals(record.Name, "frameinfo_json",
                StringComparison.OrdinalIgnoreCase)) return true;
            return record != null
                && (DynamicBlockStatePolicy.IsUpstreamBlock(record.Name)
                    || record.Name.StartsWith("upstream_info",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSupportedFrame(Transaction transaction, BlockReference block)
        {
            ObjectId recordId = block.IsDynamicBlock
                ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            BlockTableRecord record = transaction.GetObject(recordId, OpenMode.ForRead, true)
                as BlockTableRecord;
            return record != null && IsSupportedFrameName(record.Name);
        }

        private static FrameRegionGroup SelectAnchorOwner(
            IReadOnlyList<FrameRegionGroup> owners, Point3d anchor)
        {
            // Adjacent frame borders share coordinates. For export/layout, the anchor is the
            // ownership contract; nearest frame center makes a border tie deterministic.
            FrameRegionGroup selected = null;
            double bestDistance = double.MaxValue;
            foreach (FrameRegionGroup owner in owners ?? Array.Empty<FrameRegionGroup>())
            {
                double distance = Math.Abs((owner.Boundary.MinX + owner.Boundary.MaxX)
                        / 2d - anchor.X)
                    + Math.Abs((owner.Boundary.MinY + owner.Boundary.MaxY)
                        / 2d - anchor.Y);
                if (selected == null || distance < bestDistance
                    || (Math.Abs(distance - bestDistance) < 1e-9
                        && string.Compare(owner.Handle, selected.Handle,
                            StringComparison.OrdinalIgnoreCase) < 0))
                {
                    selected = owner;
                    bestDistance = distance;
                }
            }
            return selected;
        }

        private static bool TryAnchor(Entity entity, out Point3d anchor, bool includeAllEntities)
        {
            anchor = Point3d.Origin;
            if (entity is BlockReference)
            {
                // 块的基点可能远离可见几何(动态块/异形基点),用外包框中心
                // 归属才不会漏检或错归;取不到外包框时回退插入点。
                if (TryExtentsCenter(entity, out anchor)) return true;
                anchor = ((BlockReference)entity).Position;
                return true;
            }
            if (!includeAllEntities && !(entity is Table) && !(entity is DBText)
                && !(entity is MText)) return false;
            return TryExtentsCenter(entity, out anchor);
        }

        private static bool TryExtentsCenter(Entity entity, out Point3d center)
        {
            center = Point3d.Origin;
            try
            {
                Extents3d extents = entity.GeometricExtents;
                center = new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2d,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2d,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) / 2d);
                return true;
            }
            catch
            {
                // An entity without valid extents cannot be assigned safely to a frame.
                return false;
            }
        }

        private static bool TryBounds(Entity entity, out double minX, out double minY,
            out double maxX, out double maxY, bool includeAllEntities)
        {
            minX = minY = maxX = maxY = 0;
            if (entity == null || (!includeAllEntities && !(entity is Table)
                && !(entity is DBText) && !(entity is MText) && !(entity is BlockReference)))
                return false;
            try
            {
                Extents3d extents = entity.GeometricExtents;
                minX = extents.MinPoint.X;
                minY = extents.MinPoint.Y;
                maxX = extents.MaxPoint.X;
                maxY = extents.MaxPoint.Y;
                return true;
            }
            catch
            {
                // An entity without valid extents cannot be assigned safely to a frame.
                return false;
            }
        }
    }
}
