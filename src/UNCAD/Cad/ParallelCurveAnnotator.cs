using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.Fill;
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

        /// <summary>
        /// 旧版重复标注的比对键：把标注内容折算成旧版单行写法。
        /// 两行标注必须提供，否则旧图纸上同位置的旧单行标注识别不出来、
        /// 重跑一次就会留下两份。为空时按内容原样比较。
        /// </summary>
        public Func<string, string> LegacyLabelKey { get; set; }

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
            int skippedUnsupported = 0;
            int skippedAlreadyGenerated = 0;
            int skippedGeometry = 0;
            int skippedOffset = 0;
            int skippedLabel = 0;
            ObjectId[] sourceIds = ids.Distinct().ToArray();
            bool metadataReady = !string.IsNullOrWhiteSpace(options.AnnotationKind)
                && ParallelAnnotationMetadata.EnsureApplication(ctx.Db, transaction);
            int resolvedGenerated = 0;
            if (metadataReady)
                sourceIds = ResolveSourceIds(ctx, transaction, sourceIds,
                    options.AnnotationKind, out resolvedGenerated);
            if (sourceIds.Length == 0)
            {
                ctx.Write("\n[UNCAD] 选中的对象不是可处理的独立线段，或原始线段已不存在。");
                return 0;
            }
            HashSet<string> sourceHandles = new HashSet<string>(
                sourceIds.Select(id => id.Handle.ToString()),
                StringComparer.OrdinalIgnoreCase);
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
                Curve source;
                try { source = transaction.GetObject(id, OpenMode.ForRead, true) as Curve; }
                catch
                {
                    skipped++;
                    skippedUnsupported++;
                    continue;
                }
                if (!IsSupported(source))
                {
                    skipped++;
                    skippedUnsupported++;
                    continue;
                }
                // A previous generated offset can be selected by a window. Never use it as
                // a new source, otherwise each run walks one level farther from the design
                // line. ResolveSourceIds normally maps same-kind output back to its source;
                // this guard remains for foreign kinds or stale metadata.
                if (metadataReady && ParallelAnnotationMetadata.TryRead(source,
                    out _, out _))
                {
                    skipped++;
                    skippedAlreadyGenerated++;
                    continue;
                }

                double length;
                Point3d sourceMid;
                double sourceAngle;
                Vector3d desired;
                try
                {
                    length = GetLength(source);
                    if (length <= 0)
                    {
                        skipped++;
                        skippedGeometry++;
                        continue;
                    }
                    sourceMid = source.GetPointAtDist(length / 2.0);
                    Vector3d tangent = source.GetFirstDerivative(sourceMid);
                    if (tangent.Length < 1e-9)
                    {
                        skipped++;
                        skippedGeometry++;
                        continue;
                    }
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
                if (selected == null)
                {
                    skipped++;
                    skippedOffset++;
                    continue;
                }

                Entity text;
                string labelKey;
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
                    string label = options.LabelFactory(length);
                    labelKey = LegacyKey(options, label);
                    text = CreateLabel(ctx, label, textPoint,
                        options.TextHeight, textAngle,
                        options.Above ? AttachmentPoint.BottomCenter
                            : AttachmentPoint.TopCenter,
                        options.ColorIndex, styleId);
                }
                catch (Exception ex)
                {
                    selected.Dispose();
                    skipped++;
                    skippedLabel++;
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
                    RemoveLegacyDuplicates(transaction, legacy, text, labelKey,
                        options, selected.Entities);
                    ErasePrevious(transaction, previous, id.Handle.ToString());
                    selected.Detach();
                    count++;
                }
                finally
                {
                    selected.Dispose();
                }
            }
            if (resolvedGenerated > 0)
                ctx.Write("\n[UNCAD] 已识别已生成的平行标注线，并回溯源线更新 "
                    + resolvedGenerated + " 条。");
            if (skipped > 0)
            {
                var reasons = new List<string>();
                if (skippedUnsupported > 0) reasons.Add("对象类型不支持 "
                    + skippedUnsupported);
                if (skippedAlreadyGenerated > 0) reasons.Add("已有平行标注且未找到可用源线 "
                    + skippedAlreadyGenerated);
                if (skippedGeometry > 0) reasons.Add("退化或无长度 " + skippedGeometry);
                if (skippedOffset > 0) reasons.Add("偏移失败 " + skippedOffset);
                if (skippedLabel > 0) reasons.Add("文字位置失败 " + skippedLabel);
                ctx.Write("\n[UNCAD] 已跳过 " + skipped
                    + " 条曲线" + (reasons.Count == 0
                        ? "。" : "（" + string.Join("、", reasons) + "）。"));
            }
            return count;
        }

        /// <summary>
        /// If the user clicks a red line produced by this command, use its recorded source
        /// line instead of silently treating the output as a new source. This preserves the
        /// one-level/idempotent geometry rule while making ordinary click selection usable.
        /// </summary>
        private static ObjectId[] ResolveSourceIds(CadContext ctx, Transaction transaction,
            ObjectId[] ids, string annotationKind, out int resolvedCount)
        {
            resolvedCount = 0;
            var result = new List<ObjectId>();
            foreach (ObjectId id in ids ?? new ObjectId[0])
            {
                Entity entity;
                try { entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity; }
                catch { continue; }
                if (entity == null || entity.IsErased) continue;

                if (ParallelAnnotationMetadata.TryRead(entity,
                    out string existingKind, out string sourceHandle)
                    && string.Equals(existingKind, annotationKind,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (TryResolveHandle(ctx.Db, sourceHandle, out ObjectId sourceId))
                    {
                        try
                        {
                            Entity source = transaction.GetObject(sourceId,
                                OpenMode.ForRead, true) as Entity;
                            if (source is Curve sourceCurve && IsSupported(sourceCurve))
                            {
                                result.Add(sourceId);
                                resolvedCount++;
                            }
                        }
                        catch { }
                    }
                    // A generated line with a missing/invalid source is not a valid
                    // fallback source; offsetting it would compound the error.
                    continue;
                }
                result.Add(id);
            }
            return result.Distinct().ToArray();
        }

        private static bool TryResolveHandle(Database database, string value,
            out ObjectId id)
        {
            id = ObjectId.Null;
            if (database == null || string.IsNullOrWhiteSpace(value)
                || !long.TryParse(value.Trim(), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long raw)) return false;
            try
            {
                id = database.GetObjectId(false, new Handle(raw), 0);
                return !id.IsNull && id.IsValid;
            }
            catch { return false; }
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
            /// <summary>旧标注落在哪个点上；DBText 的插入点与对齐点都要看。</summary>
            public Point3d[] TextAnchors;
            public Type CurveType;
            public double Length;
            public Point3d ExtentsMin;
            public Point3d ExtentsMax;
        }

        /// <summary>
        /// One pass over the space collecting untagged, same-colour DBText/MText and
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
                        TextAnchors = DbTextAnchors(text.Position, text.AlignmentPoint,
                            text.Justify)
                    });
                    continue;
                }
                // 两行标注是 MTEXT；旧版本可能已经被手工改写成 MTEXT 却没有元数据，
                // 同样要纳入重复判定。
                if (candidate is MText mtext)
                {
                    if (mtext.ColorIndex != colorIndex) continue;
                    result.Add(new LegacyCandidate
                    {
                        Id = id, IsText = true, Text = mtext.Contents,
                        TextAnchors = new[] { mtext.Location }
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
            List<LegacyCandidate> legacy, Entity currentText, string currentKey,
            ParallelAnnotationOptions options, IEnumerable<Entity> currentCurves)
        {
            if (legacy == null || legacy.Count == 0) return;
            Entity[] generated = (currentCurves ?? Enumerable.Empty<Entity>()).ToArray();
            Point3d[] currentAnchors = TextAnchors(currentText);
            foreach (LegacyCandidate candidate in legacy)
            {
                if (candidate.IsText)
                {
                    // 新旧写法不同（旧单行 / 新两行），所以先都折算成旧单行写法再比。
                    if (string.Equals(LegacyKey(options, candidate.Text), currentKey,
                            StringComparison.Ordinal)
                        && SharesAnchor(candidate.TextAnchors, currentAnchors))
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

        /// <summary>
        /// 生成标注实体：两行用 MTEXT（DBText 会把 \P 原样画成反斜杠加 P），
        /// 单行仍用 DBText，保持可直接编辑、与旧读取路径兼容。
        /// </summary>
        private static Entity CreateLabel(CadContext ctx, string label, Point3d anchor,
            double height, double rotation, AttachmentPoint alignment, short colorIndex,
            ObjectId styleId)
        {
            return label.IndexOf(AnnotationLabelPair.Separator,
                StringComparison.Ordinal) >= 0
                ? (Entity)EntityFactory.MText(ctx, label, anchor, height, rotation,
                    alignment, colorIndex, styleId)
                : EntityFactory.DBText(ctx, label, anchor, height, rotation,
                    alignment, colorIndex, styleId);
        }

        /// <summary>把标注内容折算成旧版单行写法；调用方未提供时原样返回。</summary>
        private static string LegacyKey(ParallelAnnotationOptions options, string label)
        {
            if (options.LegacyLabelKey == null) return label ?? "";
            return options.LegacyLabelKey(label) ?? "";
        }

        /// <summary>
        /// DBText 的插入点与对齐点都可能承载实际位置（对齐方式不是 BaseLeft 时
        /// 生效的是对齐点），所以两个都作为锚点候选；任一相同即视为同一位置。
        /// </summary>
        private static Point3d[] DbTextAnchors(Point3d position, Point3d alignmentPoint,
            AttachmentPoint justify)
            => justify == AttachmentPoint.BaseLeft
                ? new[] { position }
                : new[] { position, alignmentPoint };

        private static Point3d[] TextAnchors(Entity text)
        {
            if (text is DBText db)
                return DbTextAnchors(db.Position, db.AlignmentPoint, db.Justify);
            if (text is MText mtext) return new[] { mtext.Location };
            return new Point3d[0];
        }

        private static bool SharesAnchor(Point3d[] first, Point3d[] second)
        {
            if (first == null || second == null) return false;
            foreach (Point3d a in first)
                foreach (Point3d b in second)
                    if (SamePoint(a, b)) return true;
            return false;
        }

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
