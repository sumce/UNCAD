using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.Fill;
using UNCAD.Core.Geometry;

namespace UNCAD.Cad
{
    /// <summary>Objects assigned to one selected legacy or xframe boundary.</summary>
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
    /// Uses selected frame blocks as containers, then scans the current space once to
    /// assign tables, text and blocks. Selection membership is not trusted because a wide window
    /// can include unrelated upstream blocks outside the frame boundary.
    /// </summary>
    internal static class FrameRegionCollector
    {
        public const string SupportedFrameName = "frame_20260812";
        public const string LegacyFrameName = "frame";
        public const string XFrameName = "xframe";
        public const string SupportedFrameDescription = "frame_20260812/frame/xframe";

        internal static bool IsSupportedFrameName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string normalized = BlockNameNormalizer.RemoveMangledSuffix(name);
            return string.Equals(normalized, SupportedFrameName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, LegacyFrameName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, XFrameName, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsLegacyFrameName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string normalized = BlockNameNormalizer.RemoveMangledSuffix(name);
            return string.Equals(normalized, SupportedFrameName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, LegacyFrameName, StringComparison.OrdinalIgnoreCase);
        }

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
            private const int MaxIndexedCells = 256;
            private readonly double _cellWidth;
            private readonly double _cellHeight;
            private readonly Dictionary<GridKey, List<FrameRegionGroup>> _buckets =
                new Dictionary<GridKey, List<FrameRegionGroup>>();
            private readonly List<FrameRegionGroup> _overflow =
                new List<FrameRegionGroup>();
            private readonly FrameRegionGroup[] _all;

            public FrameBoundaryIndex(IReadOnlyList<FrameRegionGroup> groups)
            {
                _all = (groups ?? Array.Empty<FrameRegionGroup>())
                    .Where(group => group?.Boundary != null).ToArray();
                _cellWidth = CellSize(_all.Select(group => group.Boundary.Width));
                _cellHeight = CellSize(_all.Select(group => group.Boundary.Height));
                foreach (FrameRegionGroup group in _all)
                {
                    if (!TryCellRange(group.Boundary.MinX, group.Boundary.MaxX,
                            _cellWidth, out long firstX, out long lastX)
                        || !TryCellRange(group.Boundary.MinY, group.Boundary.MaxY,
                            _cellHeight, out long firstY, out long lastY)
                        || !WithinCellLimit(firstX, lastX, firstY, lastY))
                    {
                        _overflow.Add(group);
                        continue;
                    }
                    for (long x = firstX; ; x++)
                    {
                        for (long y = firstY; ; y++)
                        {
                            AddBucket(new GridKey(x, y), group);
                            if (y == lastY) break;
                        }
                        if (x == lastX) break;
                    }
                }
            }

            public List<FrameRegionGroup> QueryPoint(double x, double y)
            {
                var candidates = new List<FrameRegionGroup>();
                var seen = new HashSet<FrameRegionGroup>();
                long centerX = CellX(x);
                long centerY = CellY(y);
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                        if (TryOffset(centerX, dx, out long xCell)
                            && TryOffset(centerY, dy, out long yCell)
                            && _buckets.TryGetValue(new GridKey(xCell, yCell),
                                out List<FrameRegionGroup> bucket))
                            foreach (FrameRegionGroup group in bucket)
                                if (seen.Add(group)) candidates.Add(group);
                foreach (FrameRegionGroup group in _overflow)
                    if (seen.Add(group)) candidates.Add(group);
                return candidates;
            }

            public List<FrameRegionGroup> QueryBounds(double minX, double minY,
                double maxX, double maxY)
            {
                if (!IsFinite(minX) || !IsFinite(minY) || !IsFinite(maxX)
                    || !IsFinite(maxY) || minX > maxX || minY > maxY)
                    return _all.ToList();
                long firstX = CellX(minX);
                long lastX = CellX(maxX);
                long firstY = CellY(minY);
                long lastY = CellY(maxY);
                if (!TryOffset(firstX, -1, out firstX)
                    || !TryOffset(lastX, 1, out lastX)
                    || !TryOffset(firstY, -1, out firstY)
                    || !TryOffset(lastY, 1, out lastY)
                    || !WithinCellLimit(firstX, lastX, firstY, lastY))
                    return _all.ToList();

                var candidates = new List<FrameRegionGroup>();
                var seen = new HashSet<FrameRegionGroup>();
                for (long x = firstX; ; x++)
                {
                    for (long y = firstY; ; y++)
                    {
                        if (_buckets.TryGetValue(new GridKey(x, y),
                            out List<FrameRegionGroup> bucket))
                            foreach (FrameRegionGroup group in bucket)
                                if (seen.Add(group)) candidates.Add(group);
                        if (y == lastY) break;
                    }
                    if (x == lastX) break;
                }
                foreach (FrameRegionGroup group in _overflow)
                    if (seen.Add(group)) candidates.Add(group);
                return candidates;
            }

            private long CellX(double value) => ToCell(value, _cellWidth);
            private long CellY(double value) => ToCell(value, _cellHeight);

            private void AddBucket(GridKey key, FrameRegionGroup group)
            {
                if (!_buckets.TryGetValue(key, out List<FrameRegionGroup> bucket))
                {
                    bucket = new List<FrameRegionGroup>();
                    _buckets.Add(key, bucket);
                }
                bucket.Add(group);
            }

            private static bool TryCellRange(double min, double max, double size,
                out long first, out long last)
            {
                first = last = 0;
                if (!IsFinite(min) || !IsFinite(max) || !IsFinite(size) || size <= 0)
                    return false;
                if (min > max) { double swap = min; min = max; max = swap; }
                double firstValue = Math.Floor(min / size);
                double lastValue = Math.Floor(max / size);
                if (firstValue < long.MinValue || firstValue > long.MaxValue
                    || lastValue < long.MinValue || lastValue > long.MaxValue)
                    return false;
                first = (long)firstValue;
                last = (long)lastValue;
                return first <= last;
            }

            private static bool WithinCellLimit(long firstX, long lastX,
                long firstY, long lastY)
            {
                if (firstX > lastX || firstY > lastY) return false;
                // Decimal subtraction is exact for Int64 values and avoids the unsigned
                // wraparound that made a MinValue..MaxValue range look like zero cells.
                decimal width = (decimal)lastX - (decimal)firstX + 1m;
                decimal height = (decimal)lastY - (decimal)firstY + 1m;
                return width <= MaxIndexedCells && height <= MaxIndexedCells
                    && width <= MaxIndexedCells / height;
            }

            private static bool TryOffset(long value, long offset, out long result)
            {
                result = 0;
                if (offset > 0 && value > long.MaxValue - offset) return false;
                if (offset < 0 && value < long.MinValue - offset) return false;
                result = value + offset;
                return true;
            }

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
                if (double.IsNaN(cell) || double.IsInfinity(cell)) return 0;
                if (cell <= long.MinValue) return long.MinValue;
                if (cell >= long.MaxValue) return long.MaxValue;
                return (long)cell;
            }

            private static bool IsFinite(double value)
                => !double.IsNaN(value) && !double.IsInfinity(value);

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

        /// <summary>Runs XLAYOUT collection against a detached, in-memory DWG database.</summary>
        internal static FrameRegionCollection CollectForLayout(Database database,
            ObjectId spaceId, ObjectId[] selectedIds)
            => CollectCore(database, spaceId, selectedIds, true, true, true);

        private static FrameRegionCollection CollectCore(CadContext ctx, ObjectId[] selectedIds,
            bool includeAllEntities, bool useAnchorOwnership)
        {
            if (ctx == null) return new FrameRegionCollection();
            return CollectCore(ctx.Db, ctx.Db.CurrentSpaceId, selectedIds,
                includeAllEntities, useAnchorOwnership);
        }

        private static FrameRegionCollection CollectCore(Database database, ObjectId spaceId,
            ObjectId[] selectedIds, bool includeAllEntities, bool useAnchorOwnership,
            bool selectedIdsAreFrames = false)
        {
            var result = new FrameRegionCollection();
            if (database == null || spaceId.IsNull || selectedIds == null
                || selectedIds.Length == 0) return result;

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var selectedFrames = new HashSet<ObjectId>();
                var boundaryCache = new Dictionary<ObjectId, FrameBorderBounds>();
                foreach (ObjectId id in selectedIds.Distinct())
                {
                    BlockReference block;
                    try
                    {
                        block = transaction.GetObject(id, OpenMode.ForRead, true)
                            as BlockReference;
                    }
                    catch
                    {
                        // A selection can contain an erased/proxy object. It is not a frame;
                        // ignore it so one stale pick does not abort a batch operation.
                        continue;
                    }
                    if (block == null || block is Table
                        || block.OwnerId != spaceId
                        || (!selectedIdsAreFrames
                            && !IsSupportedFrame(transaction, block))) continue;
                    selectedFrames.Add(id);
                    result.SelectedFrameCount++;
                    AddFrame(result, transaction, block, boundaryCache);
                }

                if (result.Groups.Count == 0) return result;
                if (result.Errors.Count > 0) return result;

                BlockTableRecord space = transaction.GetObject(spaceId,
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
                    Entity entity;
                    try
                    {
                        entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                    }
                    catch
                    {
                        // Proxy/erased entities have no reliable spatial anchor.
                        continue;
                    }
                    if (entity == null || entity.IsErased) continue;
                    if (useAnchorOwnership)
                    {
                        // A block reference is owned by the frame containing its insert point.
                        // Its visible geometry can be deliberately displaced (long labels,
                        // dynamic grips, or an exploded-looking legacy symbol), so using the
                        // geometric center first can assign an identity block to a neighbour.
                        // Keep the geometric center as a fallback for blocks whose base point
                        // was authored outside the frame.
                        // Table derives from BlockReference, but its insertion point is the
                        // lower-left corner. Use the visible center so a table on an adjacent
                        // border stays with the frame containing its cells.
                        if (entity is BlockReference block && !(entity is Table)
                            && IsFinite(block.Position)
                            && TryAssignAtAnchor(boundaryIndex, block.Position, id))
                            continue;

                        if (TryAnchor(entity, out Point3d anchor, includeAllEntities)
                            && TryAssignAtAnchor(boundaryIndex, anchor, id)) continue;

                        // Long labels and helper geometry can have their center outside a frame.
                        // Use bounds only when the entity intersects exactly one frame; crossing
                        // geometry remains unassigned instead of being duplicated.
                        TryAssignByBounds(boundaryIndex, entity, id, includeAllEntities);
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
            // Current templates use four outer Line entities; the oldest frame template uses a
            // closed rectangular Polyline. Read that explicit border instead of aggregate block
            // extents, which also include labels and nested symbols.
            FrameBorderBounds best = null;
            foreach (ObjectId definitionId in DefinitionIds(block))
            {
                if (boundaryCache != null && boundaryCache.TryGetValue(definitionId,
                    out FrameBorderBounds cached))
                {
                    if (best == null || Area(cached) > Area(best)) best = cached;
                    continue;
                }

                BlockTableRecord definition;
                try
                {
                    definition = transaction.GetObject(definitionId,
                        OpenMode.ForRead, true) as BlockTableRecord;
                }
                catch
                {
                    continue;
                }
                if (definition == null) continue;
                if (!TryReadDefinitionBorder(transaction, definition,
                    out FrameBorderBounds bounds)) continue;
                if (boundaryCache != null) boundaryCache[definitionId] = bounds;
                if (best == null || Area(bounds) > Area(best)) best = bounds;
            }
            if (best != null) return TransformBorder(block, best);
            throw new InvalidOperationException("图框定义未找到闭合的最外矩形边线或多段线。");
        }

        /// <summary>
        /// Reads all direct border entities in one pass.  Dynamic block references can expose
        /// an anonymous definition whose border is different from the named definition, so the
        /// caller tries both definitions and keeps the largest valid rectangle.
        /// </summary>
        private static bool TryReadDefinitionBorder(Transaction transaction,
            BlockTableRecord definition, out FrameBorderBounds bounds)
        {
            bounds = null;
            if (transaction == null || definition == null) return false;

            var lines = new List<BorderLineGeometry>();
            FrameBorderBounds polylineBounds = null;
            foreach (ObjectId id in definition)
            {
                Entity entity;
                try
                {
                    entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                }
                catch
                {
                    // A damaged/proxy child must not hide a usable border in the same block.
                    continue;
                }
                if (entity == null || entity.IsErased) continue;

                if (entity is Line line)
                {
                    Point3d start = line.StartPoint;
                    Point3d end = line.EndPoint;
                    if (IsFinite(start) && IsFinite(end))
                        lines.Add(new BorderLineGeometry(start, end));
                    continue;
                }

                if (entity is Polyline polyline)
                {
                    // A closed rectangular polyline is the legacy frame representation.  Keep
                    // the largest one because title blocks may contain smaller rectangles too.
                    if (TryReadRectangle(polyline, out FrameBorderBounds candidate)
                        && (polylineBounds == null || Area(candidate) > Area(polylineBounds)))
                        polylineBounds = candidate;

                    // Some exported frames contain a straight, open polyline for one or more
                    // outer sides.  Treat its zero-bulge segments like independent lines.
                    AddStraightPolylineSegments(polyline, lines);
                }
            }

            if (TryReadLineBorder(lines, out FrameBorderBounds lineBounds)
                && (bounds == null || Area(lineBounds) > Area(bounds)))
                bounds = lineBounds;
            if (polylineBounds != null
                && (bounds == null || Area(polylineBounds) > Area(bounds)))
                bounds = polylineBounds;
            return bounds != null;
        }

        private struct BorderLineGeometry
        {
            public BorderLineGeometry(Point3d start, Point3d end)
            {
                Start = start;
                End = end;
            }

            public Point3d Start;
            public Point3d End;
        }

        private static void AddStraightPolylineSegments(Polyline polyline,
            ICollection<BorderLineGeometry> lines)
        {
            if (polyline == null || lines == null) return;
            int count;
            bool closed;
            try
            {
                count = polyline.NumberOfVertices;
                closed = polyline.Closed;
            }
            catch { return; }
            if (count < 2) return;

            for (int index = 0; index < count; index++)
            {
                int next = index + 1;
                if (next >= count)
                {
                    if (!closed) break;
                    next = 0;
                }

                try
                {
                    if (Math.Abs(polyline.GetBulgeAt(index)) > 0.000000001) continue;
                    Point2d start = polyline.GetPoint2dAt(index);
                    Point2d end = polyline.GetPoint2dAt(next);
                    var start3d = new Point3d(start.X, start.Y, 0d);
                    var end3d = new Point3d(end.X, end.Y, 0d);
                    if (IsFinite(start3d) && IsFinite(end3d))
                        lines.Add(new BorderLineGeometry(start3d, end3d));
                }
                catch
                {
                    // Continue with the remaining segments when one vertex is invalid.
                }
            }
        }

        private struct BorderSegment
        {
            public BorderSegment(double coordinate, double start, double end)
            {
                Coordinate = coordinate;
                Start = Math.Min(start, end);
                End = Math.Max(start, end);
            }

            public double Coordinate;
            public double Start;
            public double End;
        }

        private sealed class BorderLineGroup
        {
            public double Coordinate;
            public List<BorderSegment> Segments { get; } = new List<BorderSegment>();
        }

        private static bool TryReadLineBorder(IReadOnlyList<BorderLineGeometry> lines,
            out FrameBorderBounds bounds)
        {
            bounds = null;
            if (lines == null || lines.Count == 0) return false;

            var horizontal = new List<BorderSegment>();
            var vertical = new List<BorderSegment>();
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            foreach (BorderLineGeometry geometry in lines)
            {
                Point3d start = geometry.Start;
                Point3d end = geometry.End;
                if (!IsFinite(start) || !IsFinite(end)) continue;
                double x1 = start.X;
                double y1 = start.Y;
                double x2 = end.X;
                double y2 = end.Y;
                minX = Math.Min(minX, Math.Min(x1, x2));
                minY = Math.Min(minY, Math.Min(y1, y2));
                maxX = Math.Max(maxX, Math.Max(x1, x2));
                maxY = Math.Max(maxY, Math.Max(y1, y2));
            }
            if (!IsFinite(minX) || !IsFinite(minY) || !IsFinite(maxX) || !IsFinite(maxY))
                return false;
            double span = Math.Max(maxX - minX, maxY - minY);
            if (!IsFinite(span) || span <= 0) return false;
            double tolerance = Math.Max(0.000001, span * 0.000001);

            // Classify after the scale is known.  This keeps the tolerance proportional to the
            // frame while avoiding a second database traversal.
            horizontal.Clear();
            vertical.Clear();
            foreach (BorderLineGeometry geometry in lines)
            {
                Point3d start = geometry.Start;
                Point3d end = geometry.End;
                if (!IsFinite(start) || !IsFinite(end)) continue;
                double dx = Math.Abs(end.X - start.X);
                double dy = Math.Abs(end.Y - start.Y);
                if (dx > tolerance && dy <= tolerance)
                    horizontal.Add(new BorderSegment((start.Y + end.Y) / 2d,
                        start.X, end.X));
                else if (dy > tolerance && dx <= tolerance)
                    vertical.Add(new BorderSegment((start.X + end.X) / 2d,
                        start.Y, end.Y));
            }
            if (horizontal.Count < 2 || vertical.Count < 2) return false;

            List<BorderLineGroup> horizontalGroups = GroupBorderSegments(horizontal, tolerance);
            List<BorderLineGroup> verticalGroups = GroupBorderSegments(vertical, tolerance);
            horizontalGroups = LimitBorderGroups(horizontalGroups, 32)
                .OrderBy(group => group.Coordinate).ToList();
            verticalGroups = LimitBorderGroups(verticalGroups, 32)
                .OrderBy(group => group.Coordinate).ToList();

            // Normal frame definitions have the outer sides at the coordinate extremes.  This
            // fast path avoids the expensive candidate-product search for every ordinary frame.
            if (TryReadExtremeLineBorder(horizontalGroups, verticalGroups, tolerance,
                out bounds)) return true;

            double bestArea = 0d;
            for (int bottomIndex = 0; bottomIndex < horizontalGroups.Count - 1;
                bottomIndex++)
                for (int topIndex = bottomIndex + 1; topIndex < horizontalGroups.Count;
                    topIndex++)
                {
                    BorderLineGroup bottom = horizontalGroups[bottomIndex];
                    BorderLineGroup top = horizontalGroups[topIndex];
                    double minYCandidate = Math.Min(bottom.Coordinate, top.Coordinate);
                    double maxYCandidate = Math.Max(bottom.Coordinate, top.Coordinate);
                    if (maxYCandidate - minYCandidate <= tolerance) continue;
                    for (int leftIndex = 0; leftIndex < verticalGroups.Count - 1;
                        leftIndex++)
                        for (int rightIndex = leftIndex + 1;
                            rightIndex < verticalGroups.Count; rightIndex++)
                        {
                            BorderLineGroup left = verticalGroups[leftIndex];
                            BorderLineGroup right = verticalGroups[rightIndex];
                            double minXCandidate = Math.Min(left.Coordinate, right.Coordinate);
                            double maxXCandidate = Math.Max(left.Coordinate, right.Coordinate);
                            if (maxXCandidate - minXCandidate <= tolerance) continue;
                            if (!Covers(bottom, minXCandidate, maxXCandidate, tolerance)
                                || !Covers(top, minXCandidate, maxXCandidate, tolerance)
                                || !Covers(left, minYCandidate, maxYCandidate, tolerance)
                                || !Covers(right, minYCandidate, maxYCandidate, tolerance))
                                continue;

                            double area = (maxXCandidate - minXCandidate)
                                * (maxYCandidate - minYCandidate);
                            if (area <= bestArea) continue;
                            bestArea = area;
                            bounds = new FrameBorderBounds
                            {
                                MinX = minXCandidate,
                                MinY = minYCandidate,
                                MaxX = maxXCandidate,
                                MaxY = maxYCandidate
                            };
                        }
                }
            return bounds != null;
        }

        private static bool TryReadExtremeLineBorder(
            IReadOnlyList<BorderLineGroup> horizontal,
            IReadOnlyList<BorderLineGroup> vertical,
            double tolerance, out FrameBorderBounds bounds)
        {
            bounds = null;
            if (horizontal == null || vertical == null
                || horizontal.Count < 2 || vertical.Count < 2) return false;
            BorderLineGroup bottom = horizontal.OrderBy(group => group.Coordinate).First();
            BorderLineGroup top = horizontal.OrderByDescending(group => group.Coordinate).First();
            BorderLineGroup left = vertical.OrderBy(group => group.Coordinate).First();
            BorderLineGroup right = vertical.OrderByDescending(group => group.Coordinate).First();
            double minX = left.Coordinate;
            double maxX = right.Coordinate;
            double minY = bottom.Coordinate;
            double maxY = top.Coordinate;
            if (maxX - minX <= tolerance || maxY - minY <= tolerance
                || !Covers(bottom, minX, maxX, tolerance)
                || !Covers(top, minX, maxX, tolerance)
                || !Covers(left, minY, maxY, tolerance)
                || !Covers(right, minY, maxY, tolerance)) return false;
            bounds = new FrameBorderBounds
            {
                MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY
            };
            return true;
        }

        private static List<BorderLineGroup> GroupBorderSegments(
            IEnumerable<BorderSegment> segments, double tolerance)
        {
            var groups = new List<BorderLineGroup>();
            foreach (BorderSegment segment in (segments ?? Enumerable.Empty<BorderSegment>())
                .OrderBy(item => item.Coordinate))
            {
                BorderLineGroup group = groups.LastOrDefault();
                if (group == null
                    || Math.Abs(group.Coordinate - segment.Coordinate) > tolerance)
                {
                    group = new BorderLineGroup { Coordinate = segment.Coordinate };
                    groups.Add(group);
                }
                else
                {
                    group.Coordinate = (group.Coordinate * group.Segments.Count
                        + segment.Coordinate) / (group.Segments.Count + 1d);
                }
                group.Segments.Add(segment);
            }
            foreach (BorderLineGroup group in groups)
                group.Segments.Sort((left, right) =>
                {
                    int start = left.Start.CompareTo(right.Start);
                    return start != 0 ? start : left.End.CompareTo(right.End);
                });
            return groups;
        }

        private static List<BorderLineGroup> LimitBorderGroups(
            List<BorderLineGroup> groups, int maxCount)
        {
            if (groups == null || groups.Count <= maxCount) return groups
                ?? new List<BorderLineGroup>();
            // Keep the longest covered sides and both extremes. Decorative grid lines are
            // usually short; this bounds candidate combinations for large templates.
            var selected = groups.OrderByDescending(Coverage).Take(maxCount).ToList();
            BorderLineGroup low = groups.OrderBy(group => group.Coordinate).First();
            BorderLineGroup high = groups.OrderByDescending(group => group.Coordinate).First();
            if (!selected.Contains(low)) selected[0] = low;
            if (!selected.Contains(high)) selected[selected.Count - 1] = high;
            return selected;
        }

        private static double Coverage(BorderLineGroup group)
        {
            if (group == null || group.Segments.Count == 0) return 0d;
            double start = group.Segments[0].Start;
            double end = group.Segments[0].End;
            double total = 0d;
            for (int index = 1; index < group.Segments.Count; index++)
            {
                BorderSegment range = group.Segments[index];
                if (range.Start <= end)
                {
                    end = Math.Max(end, range.End);
                    continue;
                }
                total += end - start;
                start = range.Start;
                end = range.End;
            }
            return total + end - start;
        }

        private static bool Covers(BorderLineGroup group, double start, double end,
            double tolerance)
        {
            if (group == null || end < start) return false;
            double cursor = start;
            foreach (BorderSegment range in group.Segments)
            {
                if (range.End < cursor - tolerance) continue;
                if (range.Start > cursor + tolerance) return false;
                cursor = Math.Max(cursor, range.End);
                if (cursor >= end - tolerance) return true;
            }
            return cursor >= end - tolerance;
        }

        private static bool TryReadRectangle(Polyline polyline,
            out FrameBorderBounds bounds)
        {
            bounds = null;
            if (polyline == null) return false;

            int vertexCount;
            bool closed;
            try
            {
                vertexCount = polyline.NumberOfVertices;
                closed = polyline.Closed;
            }
            catch
            {
                return false;
            }
            if (!closed || vertexCount < 4) return false;

            var points = new List<Point2d>();
            try
            {
                Vector3d normal = polyline.Normal;
                if (Math.Abs(normal.X) > 0.000001 || Math.Abs(normal.Y) > 0.000001
                    || Math.Abs(Math.Abs(normal.Z) - 1d) > 0.000001) return false;
                for (int index = 0; index < vertexCount; index++)
                {
                    if (Math.Abs(polyline.GetBulgeAt(index)) > 0.000000001) return false;
                    Point2d point = polyline.GetPoint2dAt(index);
                    if (!IsFinite(point.X) || !IsFinite(point.Y)) return false;
                    points.Add(point);
                }
            }
            catch
            {
                return false;
            }

            double minX = points.Min(point => point.X);
            double minY = points.Min(point => point.Y);
            double maxX = points.Max(point => point.X);
            double maxY = points.Max(point => point.Y);
            double width = maxX - minX;
            double height = maxY - minY;
            if (!IsFinite(width) || !IsFinite(height)) return false;
            double tolerance = Math.Max(0.000001,
                Math.Max(width, height) * 0.000000001);
            if (width <= tolerance || height <= tolerance) return false;

            double perimeter = 0d;
            for (int index = 0; index < points.Count; index++)
            {
                Point2d start = points[index];
                Point2d end = points[(index + 1) % points.Count];
                double dx = Math.Abs(end.X - start.X);
                double dy = Math.Abs(end.Y - start.Y);
                if (dx > tolerance && dy > tolerance) return false;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (!IsFinite(length) || length <= tolerance) return false;
                perimeter += length;
                if (Math.Abs(start.X - minX) > tolerance
                    && Math.Abs(start.X - maxX) > tolerance
                    && Math.Abs(start.Y - minY) > tolerance
                    && Math.Abs(start.Y - maxY) > tolerance) return false;
            }
            if (!IsFinite(perimeter)) return false;
            if (Math.Abs(perimeter - 2d * (width + height))
                > Math.Max(tolerance * 4d, perimeter * 0.00000001)) return false;

            Point2d[] corners =
            {
                new Point2d(minX, minY), new Point2d(minX, maxY),
                new Point2d(maxX, minY), new Point2d(maxX, maxY)
            };
            if (corners.Any(corner => !points.Any(point =>
                Math.Abs(point.X - corner.X) <= tolerance
                && Math.Abs(point.Y - corner.Y) <= tolerance))) return false;

            bounds = new FrameBorderBounds
            {
                MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY
            };
            return true;
        }

        private static double Area(FrameBorderBounds bounds)
            => bounds == null ? 0d : Math.Max(0d, bounds.MaxX - bounds.MinX)
                * Math.Max(0d, bounds.MaxY - bounds.MinY);

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

        private static bool HasKnownFillTag(Transaction transaction, BlockReference block)
        {
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                AttributeReference attribute;
                try
                {
                    attribute = transaction.GetObject(attributeId,
                        OpenMode.ForRead, true) as AttributeReference;
                }
                catch
                {
                    continue;
                }
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
            foreach (ObjectId definitionId in DefinitionIds(block))
            {
                BlockTableRecord record;
                try
                {
                    record = transaction.GetObject(definitionId, OpenMode.ForRead, true)
                        as BlockTableRecord;
                }
                catch
                {
                    continue;
                }
                string name = BlockNameNormalizer.RemoveMangledSuffix(record?.Name);
                if (string.Equals(name, "frameinfo_json",
                    StringComparison.OrdinalIgnoreCase)) return true;
                if (record != null
                    && (DynamicBlockStatePolicy.IsUpstreamBlock(name)
                        || name.StartsWith("upstream_info",
                            StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }

        internal static bool IsSupportedFrame(Transaction transaction, BlockReference block)
        {
            if (block == null || block is Table) return false;
            foreach (ObjectId definitionId in DefinitionIds(block))
            {
                try
                {
                    BlockTableRecord record = transaction.GetObject(definitionId,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    if (record != null && IsSupportedFrameName(record.Name)) return true;
                }
                catch
                {
                    // A broken anonymous definition does not invalidate the other definition.
                }
            }
            return false;
        }

        internal static bool IsLegacyFrame(Transaction transaction, BlockReference block)
        {
            if (block == null || block is Table) return false;
            foreach (ObjectId definitionId in DefinitionIds(block))
            {
                try
                {
                    BlockTableRecord record = transaction.GetObject(definitionId,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    if (record != null && IsLegacyFrameName(record.Name)) return true;
                }
                catch
                {
                    // Continue with the other dynamic/static definition.
                }
            }
            return false;
        }

        /// <summary>Reads the same explicit outer border used by XLAYOUT.</summary>
        internal static FrameRectangle ReadBoundary(Transaction transaction,
            BlockReference block)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (block == null) throw new ArgumentNullException(nameof(block));
            return ReadBorderBoundary(transaction, block,
                new Dictionary<ObjectId, FrameBorderBounds>());
        }

        /// <summary>
        /// Returns the concrete insert definition first, then the original dynamic definition.
        /// AutoCAD dynamic blocks commonly expose the former as an anonymous *U record; using
        /// only the latter makes U1U and XLAYOUT disagree about the same frame.
        /// </summary>
        internal static IEnumerable<ObjectId> DefinitionIds(BlockReference block)
        {
            if (block == null) yield break;
            ObjectId instance = ObjectId.Null;
            try { instance = block.BlockTableRecord; }
            catch { }
            if (!instance.IsNull && instance.IsValid) yield return instance;

            bool dynamic = false;
            try { dynamic = block.IsDynamicBlock; }
            catch { }
            if (dynamic)
            {
                ObjectId original = ObjectId.Null;
                try { original = block.DynamicBlockTableRecord; }
                catch { }
                if (!original.IsNull && original.IsValid && original != instance)
                    yield return original;
            }
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

        private static FrameRegionGroup FindAnchorOwner(
            FrameBoundaryIndex boundaryIndex, Point3d anchor,
            out List<FrameRegionGroup> owners)
        {
            owners = null;
            if (boundaryIndex == null || !IsFinite(anchor)) return null;
            FrameRegionGroup owner = null;
            foreach (FrameRegionGroup group in boundaryIndex.QueryPoint(anchor.X, anchor.Y))
            {
                if (!group.Boundary.Contains(anchor.X, anchor.Y)) continue;
                if (owner == null) owner = group;
                else
                {
                    if (owners == null) owners = new List<FrameRegionGroup> { owner };
                    owners.Add(group);
                }
            }
            return owner;
        }

        private static bool TryAssignAtAnchor(FrameBoundaryIndex boundaryIndex,
            Point3d anchor, ObjectId entityId)
        {
            if (!IsFinite(anchor)) return false;
            List<FrameRegionGroup> owners;
            FrameRegionGroup owner = FindAnchorOwner(boundaryIndex, anchor, out owners);
            FrameRegionGroup selected = owners == null
                ? owner : SelectAnchorOwner(owners, anchor);
            if (selected == null) return false;
            selected.EntityIds.Add(entityId);
            return true;
        }

        private static bool TryAssignByBounds(FrameBoundaryIndex boundaryIndex,
            Entity entity, ObjectId entityId, bool includeAllEntities)
        {
            if (boundaryIndex == null
                || !TryBounds(entity, out double minX, out double minY,
                    out double maxX, out double maxY, includeAllEntities)) return false;
            List<FrameRegionGroup> owners = boundaryIndex.QueryBounds(minX, minY,
                    maxX, maxY)
                .Where(group => group.Boundary.Intersects(minX, minY, maxX, maxY))
                .ToList();
            if (owners.Count != 1) return false;
            owners[0].EntityIds.Add(entityId);
            return true;
        }

        private static bool IsFinite(Point3d point)
            => !double.IsNaN(point.X) && !double.IsInfinity(point.X)
                && !double.IsNaN(point.Y) && !double.IsInfinity(point.Y)
                && !double.IsNaN(point.Z) && !double.IsInfinity(point.Z);

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool TryAnchor(Entity entity, out Point3d anchor, bool includeAllEntities)
        {
            anchor = Point3d.Origin;
            if (entity is Table table)
            {
                if (TryExtentsCenter(table, out anchor)) return true;
                anchor = ((BlockReference)table).Position;
                return IsFinite(anchor);
            }
            if (entity is BlockReference block)
            {
                // 块的基点可能远离可见几何(动态块/异形基点),用外包框中心
                // 归属才不会漏检或错归;取不到外包框时回退插入点。
                if (TryExtentsCenter(entity, out anchor)) return true;
                anchor = block.Position;
                return true;
            }
            if (!includeAllEntities && !(entity is Table)
                && !StatisticsTextReader.IsSupported(entity)) return false;
            if (TryAnnotationAnchor(entity, out anchor)) return true;
            return TryExtentsCenter(entity, out anchor);
        }

        /// <summary>
        /// Long BOQ-model labels can extend beyond their owning frame. Their authored
        /// insertion/alignment point is the ownership contract; geometric centre can
        /// land in an adjacent frame and make U1U miss the conduit or bridge entirely.
        /// </summary>
        internal static bool TryAnnotationAnchor(Entity entity, out Point3d anchor)
        {
            anchor = Point3d.Origin;
            if (entity is MText mtext && IsFinite(mtext.Location))
            {
                anchor = mtext.Location;
                return true;
            }
            if (!(entity is DBText text)) return false;
            Point3d textAnchor = text.Justify == AttachmentPoint.BaseLeft
                ? text.Position : text.AlignmentPoint;
            if (!IsFinite(textAnchor)) return false;
            anchor = textAnchor;
            return true;
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
                return IsFinite(center);
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
                && !(entity is BlockReference)
                && !StatisticsTextReader.IsSupported(entity)))
                return false;
            try
            {
                Extents3d extents = entity.GeometricExtents;
                minX = extents.MinPoint.X;
                minY = extents.MinPoint.Y;
                maxX = extents.MaxPoint.X;
                maxY = extents.MaxPoint.Y;
                return IsFinite(minX) && IsFinite(minY)
                    && IsFinite(maxX) && IsFinite(maxY)
                    && minX <= maxX && minY <= maxY;
            }
            catch
            {
                // An entity without valid extents cannot be assigned safely to a frame.
                return false;
            }
        }
    }
}
