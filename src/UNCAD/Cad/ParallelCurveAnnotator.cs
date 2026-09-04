using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Infra;

namespace UNCAD.Cad
{
    public sealed class ParallelAnnotationOptions
    {
        public double CurveOffset { get; set; }
        public double TextOffset { get; set; }
        public double TextHeight { get; set; }
        public bool Above { get; set; }
        public short ColorIndex { get; set; }
        public Func<double, string> LabelFactory { get; set; }
        /// <summary>U1Q or U1C; repeated runs replace prior output for each source.</summary>
        public string AnnotationKind { get; set; }
    }

    /// <summary>Creates an offset curve and its label in one CAD transaction.</summary>
    public static class ParallelCurveAnnotator
    {
        public static int Add(CadContext ctx, ObjectId[] ids,
            ParallelAnnotationOptions options)
        {
            if (ctx == null || ids == null || options == null
                || options.LabelFactory == null) return 0;
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                int count = Add(ctx, transaction, ids, options);
                transaction.Commit();
                return count;
            }
        }

        /// <summary>Uses a caller-owned transaction so U1C can update all outputs atomically.</summary>
        public static int Add(CadContext ctx, Transaction transaction, ObjectId[] ids,
            ParallelAnnotationOptions options)
        {
            if (ctx == null || transaction == null || ids == null || options == null
                || options.LabelFactory == null) return 0;

            int count = 0;
            int skipped = 0;
            ObjectId[] sourceIds = ids.Distinct().ToArray();
            HashSet<string> sourceHandles = new HashSet<string>(
                sourceIds.Select(id => id.Handle.ToString()),
                StringComparer.OrdinalIgnoreCase);
            bool metadataReady = !string.IsNullOrWhiteSpace(options.AnnotationKind)
                && ParallelAnnotationMetadata.EnsureApplication(ctx.Db, transaction);
            Dictionary<string, List<ObjectId>> previous = metadataReady
                ? CollectPrevious(ctx, transaction, options.AnnotationKind, sourceHandles)
                : new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
            // Legacy (pre-metadata) duplicates are matched against the whole
            // space. Collect a snapshot once instead of re-scanning the space
            // for every source curve (O(sources × entities) before).
            List<LegacyCandidate> legacy = CollectLegacyCandidates(ctx, transaction,
                options.ColorIndex);

            ObjectId styleId = StyleManager.GetDrawingStandardStyle(ctx, transaction);
            foreach (ObjectId id in sourceIds)
            {
                var source = transaction.GetObject(id, OpenMode.ForRead, true) as Curve;
                if (!IsSupported(source)) continue;
                // A previous generated offset can be selected by a window. Never use it as
                // a new source, otherwise each run walks one level farther from the design line.
                if (metadataReady && ParallelAnnotationMetadata.TryRead(source,
                    out _, out _)) continue;

                double length;
                Point3d sourceMid;
                double sourceAngle;
                Vector3d desired;
                try
                {
                    length = GetLength(source);
                    if (length <= 0) { skipped++; continue; }
                    sourceMid = source.GetPointAtDist(length / 2.0);
                    Vector3d tangent = source.GetFirstDerivative(sourceMid);
                    if (tangent.Length < 1e-9) { skipped++; continue; }
                    sourceAngle = GeoMath.ReadableAngle(sourceMid,
                        sourceMid + tangent);
                    double sideAngle = GeoMath.SideDirection(sourceAngle, options.Above);
                    desired = new Vector3d(Math.Cos(sideAngle), Math.Sin(sideAngle), 0);
                }
                catch (Exception ex)
                {
                    skipped++;
                    Log.Warn("Parallel curve preflight failed for " + id + ": "
                        + ex.Message);
                    continue;
                }

                OffsetCandidate positive = CreateCandidate(source, options.CurveOffset,
                    sourceMid, desired);
                OffsetCandidate negative = CreateCandidate(source, -options.CurveOffset,
                    sourceMid, desired);
                OffsetCandidate selected = SelectCandidate(positive, negative);
                OffsetCandidate rejected = ReferenceEquals(selected, positive)
                    ? negative : positive;
                rejected?.Dispose();
                if (selected == null) { skipped++; continue; }

                DBText text;
                try
                {
                    Curve labelCurve = selected.LabelCurve;
                    Point3d curvePoint = labelCurve.GetClosestPointTo(sourceMid, false);
                    Vector3d labelTangent = labelCurve.GetFirstDerivative(curvePoint);
                    double textAngle = labelTangent.Length < 1e-9
                        ? sourceAngle
                        : GeoMath.ReadableAngle(curvePoint,
                            curvePoint + labelTangent);
                    Point3d textPoint = GeoMath.Polar(curvePoint,
                        GeoMath.SideDirection(textAngle, options.Above),
                        options.TextOffset);
                    text = EntityFactory.DBText(ctx, options.LabelFactory(length), textPoint,
                        options.TextHeight, textAngle,
                        options.Above ? AttachmentPoint.BottomCenter
                            : AttachmentPoint.TopCenter,
                        options.ColorIndex, styleId);
                }
                catch (Exception ex)
                {
                    selected.Dispose();
                    skipped++;
                    Log.Warn("Parallel curve label preflight failed for " + id + ": "
                        + ex.Message);
                    continue;
                }

                try
                {
                    foreach (Entity entity in selected.Entities)
                    {
                        entity.LayerId = ctx.CurrentLayerId;
                        if (options.ColorIndex != 0) entity.ColorIndex = options.ColorIndex;
                        ctx.AddToCurrentSpace(transaction, entity);
                        if (metadataReady)
                            ParallelAnnotationMetadata.Set(entity, options.AnnotationKind,
                                id.Handle.ToString());
                    }
                    ctx.AddToCurrentSpace(transaction, text);
                    if (metadataReady)
                        ParallelAnnotationMetadata.Set(text, options.AnnotationKind,
                            id.Handle.ToString());
                    // Remove output produced by pre-metadata versions as well. The
                    // generated ObjectIds are excluded so a metadata registration
                    // failure cannot erase the result we just added.
                    RemoveLegacyDuplicates(transaction, legacy, text, selected.Entities);
                    ErasePrevious(transaction, previous, id.Handle.ToString());
                    selected.Detach();
                    count++;
                }
                finally
                {
                    selected.Dispose();
                }
            }
            if (skipped > 0)
                ctx.Write("\n[UNCAD] 已跳过 " + skipped
                    + " 条无法偏移或无法标注的曲线。");
            return count;
        }

        private static Dictionary<string, List<ObjectId>> CollectPrevious(CadContext ctx,
            Transaction transaction,
            string kind, HashSet<string> sourceHandles)
        {
            var result = new Dictionary<string, List<ObjectId>>(
                StringComparer.OrdinalIgnoreCase);
            BlockTableRecord space = transaction.GetObject(ctx.Db.CurrentSpaceId,
                OpenMode.ForRead, false) as BlockTableRecord;
            if (space == null) return result;
            foreach (ObjectId id in space)
            {
                Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                if (entity == null || !ParallelAnnotationMetadata.TryRead(entity,
                    out string existingKind, out string sourceHandle)
                    || !string.Equals(existingKind, kind,
                        StringComparison.OrdinalIgnoreCase)
                    || !sourceHandles.Contains(sourceHandle)) continue;
                if (!result.TryGetValue(sourceHandle, out List<ObjectId> ids))
                {
                    ids = new List<ObjectId>();
                    result[sourceHandle] = ids;
                }
                ids.Add(id);
            }
            return result;
        }

        private static void ErasePrevious(Transaction transaction,
            Dictionary<string, List<ObjectId>> previous, string sourceHandle)
        {
            if (previous == null || !previous.TryGetValue(sourceHandle,
                out List<ObjectId> ids)) return;
            foreach (ObjectId id in ids)
            {
                Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true) as Entity;
                if (entity != null && !entity.IsErased) entity.Erase();
            }
            previous.Remove(sourceHandle);
        }

        /// <summary>Snapshot of one untagged entity that may be a legacy duplicate.</summary>
        private sealed class LegacyCandidate
        {
            public ObjectId Id;
            public bool IsText;
            public string Text;
            public Point3d Position;
            public Type CurveType;
            public double Length;
            public Point3d ExtentsMin;
            public Point3d ExtentsMax;
        }

        /// <summary>
        /// One pass over the space collecting untagged, same-colour DBText and
        /// Curve snapshots. Older versions did not write metadata, so their
        /// exact-overlap output cannot be associated by source handle.
        /// </summary>
        private static List<LegacyCandidate> CollectLegacyCandidates(CadContext ctx,
            Transaction transaction, short colorIndex)
        {
            var result = new List<LegacyCandidate>();
            BlockTableRecord space = transaction.GetObject(ctx.Db.CurrentSpaceId,
                OpenMode.ForRead, false) as BlockTableRecord;
            if (space == null) return result;
            foreach (ObjectId id in space)
            {
                Entity candidate = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                if (candidate == null
                    || ParallelAnnotationMetadata.TryRead(candidate, out _, out _))
                    continue;
                if (candidate is DBText text)
                {
                    if (text.ColorIndex != colorIndex) continue;
                    result.Add(new LegacyCandidate
                    {
                        Id = id, IsText = true, Text = text.TextString,
                        Position = text.Position
                    });
                    continue;
                }
                if (!(candidate is Curve curve) || curve.ColorIndex != colorIndex)
                    continue;
                try
                {
                    Extents3d extents = curve.GeometricExtents;
                    result.Add(new LegacyCandidate
                    {
                        Id = id, IsText = false, CurveType = curve.GetType(),
                        Length = GetLength(curve),
                        ExtentsMin = extents.MinPoint, ExtentsMax = extents.MaxPoint
                    });
                }
                catch { }
            }
            return result;
        }

        /// <summary>
        /// Erases snapshot candidates whose geometry/text is identical to the
        /// newly generated result. The snapshot was taken before this run, so
        /// freshly generated ObjectIds can never be erased.
        /// </summary>
        private static void RemoveLegacyDuplicates(Transaction transaction,
            List<LegacyCandidate> legacy, DBText currentText,
            IEnumerable<Entity> currentCurves)
        {
            if (legacy == null || legacy.Count == 0) return;
            Entity[] generated = (currentCurves ?? Enumerable.Empty<Entity>()).ToArray();
            foreach (LegacyCandidate candidate in legacy)
            {
                if (candidate.IsText)
                {
                    if (string.Equals(candidate.Text, currentText.TextString,
                            StringComparison.Ordinal)
                        && SamePoint(candidate.Position, currentText.Position))
                        EraseLegacy(transaction, candidate.Id);
                    continue;
                }
                foreach (Entity created in generated)
                {
                    if (created is Curve newCurve
                        && candidate.CurveType == newCurve.GetType())
                    {
                        try
                        {
                            Extents3d b = newCurve.GeometricExtents;
                            if (SamePoint(candidate.ExtentsMin, b.MinPoint)
                                && SamePoint(candidate.ExtentsMax, b.MaxPoint)
                                && Math.Abs(candidate.Length - GetLength(newCurve)) <= 1e-6)
                            {
                                EraseLegacy(transaction, candidate.Id);
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        private static void EraseLegacy(Transaction transaction, ObjectId id)
        {
            Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true) as Entity;
            if (entity != null && !entity.IsErased) entity.Erase();
        }

        private static bool SamePoint(Point3d first, Point3d second)
            => first.DistanceTo(second) <= 1e-6;

        private static bool IsSupported(Curve curve)
            => curve is Line || curve is Polyline || curve is Polyline2d;

        private static double GetLength(Curve curve)
        {
            try
            {
                return Math.Abs(curve.GetDistanceAtParameter(curve.EndParam)
                    - curve.GetDistanceAtParameter(curve.StartParam));
            }
            catch { return 0; }
        }

        private static OffsetCandidate CreateCandidate(Curve source, double distance,
            Point3d sourceMid, Vector3d desired)
        {
            DBObjectCollection objects = null;
            try
            {
                objects = source.GetOffsetCurves(distance);
                var entities = new List<Entity>();
                Curve labelCurve = null;
                double score = double.NegativeInfinity;
                foreach (DBObject obj in objects)
                {
                    if (!(obj is Entity entity) || !(obj is Curve curve))
                    {
                        obj.Dispose();
                        continue;
                    }
                    entities.Add(entity);
                    Point3d nearest = curve.GetClosestPointTo(sourceMid, false);
                    double candidateScore = sourceMid.GetVectorTo(nearest)
                        .DotProduct(desired);
                    if (candidateScore > score)
                    {
                        score = candidateScore;
                        labelCurve = curve;
                    }
                }
                if (entities.Count == 0 || labelCurve == null)
                {
                    foreach (Entity entity in entities) entity.Dispose();
                    return null;
                }
                return new OffsetCandidate(entities, labelCurve, score);
            }
            catch
            {
                if (objects != null)
                    foreach (DBObject obj in objects) obj.Dispose();
                return null;
            }
        }

        private static OffsetCandidate SelectCandidate(OffsetCandidate first,
            OffsetCandidate second)
        {
            if (first == null) return second;
            if (second == null) return first;
            return first.Score >= second.Score ? first : second;
        }

        private sealed class OffsetCandidate : IDisposable
        {
            private bool _attached;

            public OffsetCandidate(List<Entity> entities, Curve labelCurve, double score)
            {
                Entities = entities;
                LabelCurve = labelCurve;
                Score = score;
            }

            public List<Entity> Entities { get; }
            public Curve LabelCurve { get; }
            public double Score { get; }

            public void Detach() => _attached = true;

            public void Dispose()
            {
                if (_attached) return;
                foreach (Entity entity in Entities) entity.Dispose();
            }
        }
    }
}
