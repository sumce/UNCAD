using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Cad;
using UNCAD.Core.Geometry;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// Writes the two-segment visual connector between each upstream_info block and
    /// its nearest upstream block.  The horizontal stub is drawn below the info
    /// block and the diagonal starts at the side facing the upstream block.
    /// Metadata makes repeated U1F/U1U runs idempotent.
    /// </summary>
    internal static class UpstreamConnectionLineWriter
    {
        private const string RegAppName = "UNCAD_UPSTREAM_LINK";
        private const string Marker = "UPSTREAM_INFO_LINK";

        private sealed class BlockAnchor
        {
            public ObjectId Id { get; set; }
            public Point3d Position { get; set; }
            public string Handle { get; set; }
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }
            public bool HasExtents { get; set; }

            public double CenterX => HasExtents ? (MinX + MaxX) / 2d : Position.X;
        }

        private sealed class ExistingLine
        {
            public Line Entity { get; set; }
            public string Key { get; set; }
        }

        internal static FillWriteResult Ensure(CadContext ctx, Transaction transaction,
            ObjectId[] upstreamInfoIds, ObjectId[] upstreamIds)
        {
            if (ctx == null || transaction == null || upstreamInfoIds == null
                || upstreamInfoIds.Length == 0 || upstreamIds == null
                || upstreamIds.Length == 0)
                return FillWriteResult.Empty;

            List<BlockAnchor> infos = ReadAnchors(transaction, upstreamInfoIds);
            List<BlockAnchor> upstreams = ReadAnchors(transaction, upstreamIds);
            if (infos.Count == 0 || upstreams.Count == 0) return FillWriteResult.Empty;

            List<ExistingLine> existing = ReadExistingLines(ctx, transaction);
            bool metadataAvailable = EnsureRegApp(ctx.Db, transaction);
            var used = new HashSet<ObjectId>();
            int changed = 0;
            int pairs = 0;

            // Pair each label with the nearest unused upstream symbol. This remains stable
            // for the normal one-to-one layout while handling legacy frames with ordering
            // differences in their entity collections.
            foreach (BlockAnchor info in infos.OrderBy(anchor => anchor.Position.X)
                .ThenBy(anchor => anchor.Position.Y))
            {
                BlockAnchor upstream = upstreams
                    .Where(anchor => !used.Contains(anchor.Id))
                    .OrderBy(anchor => info.Position.DistanceTo(anchor.Position))
                    .FirstOrDefault();
                if (upstream == null) break;
                used.Add(upstream.Id);

                Point3d upstreamPoint = FacingPoint(info, upstream);
                double infoLeft = info.HasExtents ? info.MinX : info.Position.X;
                double infoRight = info.HasExtents ? info.MaxX : info.Position.X;
                double infoBottom = info.HasExtents ? info.MinY : info.Position.Y;
                if (!UpstreamConnectionGeometry.TryCreatePath(infoLeft, infoBottom,
                    infoRight, info.Position.Z, upstreamPoint.X, upstreamPoint.Y,
                    upstreamPoint.Z, out ConnectionPath path,
                    horizontalLength: UpstreamConnectionGeometry.DefaultHorizontalLength)) continue;
                pairs++;

                string baseKey = BuildKey(info, upstream);
                string stubKey = baseKey + ":stub";
                string connectorKey = baseKey + ":connector";
                changed += EnsureSegment(ctx, transaction, existing, path.Horizontal,
                    stubKey, baseKey, metadataAvailable);
                changed += EnsureSegment(ctx, transaction, existing, path.Diagonal,
                    connectorKey, baseKey, metadataAvailable);
            }

            return new FillWriteResult(changed, pairs);
        }

        private static List<BlockAnchor> ReadAnchors(Transaction transaction, ObjectId[] ids)
        {
            var anchors = new List<BlockAnchor>();
            var seen = new HashSet<ObjectId>();
            foreach (ObjectId id in ids.Distinct())
            {
                if (id.IsNull || !seen.Add(id)) continue;
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null) continue;
                anchors.Add(new BlockAnchor
                {
                    Id = id,
                    Position = block.Position,
                    Handle = block.Handle.ToString(),
                    HasExtents = TryReadExtents(block, out double minX, out double minY,
                        out double maxX, out double maxY),
                    MinX = minX,
                    MinY = minY,
                    MaxX = maxX,
                    MaxY = maxY
                });
            }
            return anchors;
        }

        private static List<ExistingLine> ReadExistingLines(CadContext ctx,
            Transaction transaction)
        {
            var lines = new List<ExistingLine>();
            BlockTableRecord space = transaction.GetObject(ctx.CurrentSpaceId,
                OpenMode.ForRead, false) as BlockTableRecord;
            if (space == null) return lines;
            foreach (ObjectId id in space)
            {
                Line line = transaction.GetObject(id, OpenMode.ForRead, true) as Line;
                if (line == null) continue;
                lines.Add(new ExistingLine { Entity = line, Key = ReadLinkKey(line) });
            }
            return lines;
        }

        private static string BuildKey(BlockAnchor info, BlockAnchor upstream)
            => info.Handle + ":" + upstream.Handle;

        private static int EnsureSegment(CadContext ctx, Transaction transaction,
            List<ExistingLine> existing, ConnectionSegment segment, string key,
            string legacyKey, bool metadataAvailable)
        {
            // A line written by the old build carried only the pair key.  Reuse it
            // as the diagonal when upgrading a drawing, then create the new stub.
            List<ExistingLine> marked = existing.Where(line =>
                string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase)).ToList();
            ExistingLine target = marked.FirstOrDefault();
            if (target == null && !string.IsNullOrEmpty(legacyKey)
                && key.EndsWith(":connector", StringComparison.OrdinalIgnoreCase))
            {
                target = existing.FirstOrDefault(line =>
                    string.Equals(line.Key, legacyKey, StringComparison.OrdinalIgnoreCase));
                if (target != null) marked.Add(target);
            }
            if (target == null)
                target = existing.FirstOrDefault(line => SameLine(line.Entity, segment));

            int changed = 0;
            if (target == null)
            {
                Line line = EntityFactory.Line(ctx,
                    new Point3d(segment.StartX, segment.StartY, segment.StartZ),
                    new Point3d(segment.EndX, segment.EndY, segment.EndZ));
                ctx.AddToCurrentSpace(transaction, line);
                if (metadataAvailable) SetLinkData(line, key);
                existing.Add(new ExistingLine { Entity = line, Key = key });
                target = existing[existing.Count - 1];
                changed++;
            }
            else
            {
                if (!SameLine(target.Entity, segment))
                {
                    target.Entity.UpgradeOpen();
                    target.Entity.StartPoint = new Point3d(segment.StartX,
                        segment.StartY, segment.StartZ);
                    target.Entity.EndPoint = new Point3d(segment.EndX,
                        segment.EndY, segment.EndZ);
                    changed++;
                }
                if (!string.Equals(target.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (metadataAvailable && SetLinkData(target.Entity, key))
                        target.Key = key;
                    else if (string.IsNullOrEmpty(target.Key))
                        target.Key = key;
                }
            }

            // Remove only marked duplicates for this exact pair/segment.  Ordinary
            // user-drawn route lines are intentionally never erased.
            foreach (ExistingLine duplicate in marked.Where(line =>
                !ReferenceEquals(line, target)).ToList())
            {
                duplicate.Entity.UpgradeOpen();
                duplicate.Entity.Erase();
                existing.Remove(duplicate);
                changed++;
            }
            return changed;
        }

        private static Point3d FacingPoint(BlockAnchor info, BlockAnchor upstream)
        {
            if (!upstream.HasExtents) return upstream.Position;
            bool upstreamLeft = upstream.CenterX < info.CenterX;
            double x = upstreamLeft ? upstream.MaxX : upstream.MinX;
            // Keep the connection on the side of the symbol nearest to the stub,
            // while preserving the symbol's actual elevation.
            double y = Math.Max(upstream.MinY, Math.Min(upstream.MaxY,
                info.HasExtents ? info.MinY : info.Position.Y));
            return new Point3d(x, y, upstream.Position.Z);
        }

        private static bool TryReadExtents(BlockReference block, out double minX,
            out double minY, out double maxX, out double maxY)
        {
            minX = minY = maxX = maxY = 0d;
            try
            {
                Extents3d extents = block.GeometricExtents;
                minX = extents.MinPoint.X;
                minY = extents.MinPoint.Y;
                maxX = extents.MaxPoint.X;
                maxY = extents.MaxPoint.Y;
                return maxX >= minX && maxY >= minY;
            }
            catch
            {
                return false;
            }
        }

        private static bool SameLine(Line line, ConnectionSegment segment)
            => UpstreamConnectionGeometry.SameSegment(
                new ConnectionSegment(line.StartPoint.X, line.StartPoint.Y,
                    line.StartPoint.Z, line.EndPoint.X, line.EndPoint.Y,
                    line.EndPoint.Z), segment);

        private static bool EnsureRegApp(Database database, Transaction transaction)
        {
            try
            {
                RegAppTable table = transaction.GetObject(database.RegAppTableId,
                    OpenMode.ForRead, false) as RegAppTable;
                if (table == null) return false;
                if (!table.Has(RegAppName))
                {
                    table.UpgradeOpen();
                    var record = new RegAppTableRecord { Name = RegAppName };
                    table.Add(record);
                    transaction.AddNewlyCreatedDBObject(record, true);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetLinkData(Line line, string key)
        {
            try
            {
                line.UpgradeOpen();
                var values = new List<TypedValue>();
                try
                {
                    using (ResultBuffer existing = line.XData)
                    {
                        if (existing != null)
                        {
                            bool skipOwn = false;
                            foreach (TypedValue value in existing)
                            {
                                if (value.TypeCode == 1001)
                                {
                                    string app = Convert.ToString(value.Value,
                                        CultureInfo.InvariantCulture) ?? "";
                                    skipOwn = string.Equals(app, RegAppName,
                                        StringComparison.OrdinalIgnoreCase);
                                    if (skipOwn) continue;
                                }
                                if (!skipOwn) values.Add(value);
                            }
                        }
                    }
                }
                catch
                {
                    values.Clear();
                }
                values.Add(new TypedValue(1001, RegAppName));
                values.Add(new TypedValue(1000, Marker));
                values.Add(new TypedValue(1000, key ?? ""));
                line.XData = new ResultBuffer(values.ToArray());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadLinkKey(Line line)
        {
            try
            {
                using (ResultBuffer data = line.GetXDataForApplication(RegAppName))
                {
                    if (data == null) return "";
                    TypedValue[] values = data.AsArray();
                    for (int i = 0; i < values.Length; i++)
                    {
                        if (values[i].TypeCode != 1000
                            || !string.Equals(Convert.ToString(values[i].Value), Marker,
                                StringComparison.OrdinalIgnoreCase)) continue;
                        return i + 1 < values.Length && values[i + 1].TypeCode == 1000
                            ? Convert.ToString(values[i + 1].Value) ?? "" : "";
                    }
                }
            }
            catch
            {
                // Invalid or unavailable XData must not prevent the CAD fill from completing.
            }
            return "";
        }
    }
}
