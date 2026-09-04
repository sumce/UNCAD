using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Cad;
using UNCAD.Core.QuickLine;
using UNCAD.Infra;

namespace UNCAD.Cad.QuickLine
{
    /// <summary>Options controlling discovery of U1L lines and mm labels.</summary>
    public sealed class QuickLineScanOptions
    {
        /// <summary>
        /// Maximum perpendicular distance used when matching a text label to a
        /// line.  A zero value derives a conservative limit from the
        /// text height (the U1L offset is normally no more than two heights).
        /// </summary>
        public double LabelSearchDistance { get; set; }

        /// <summary>
        /// Include lines for which no mm label could be found.  This is useful
        /// for diagnostics; U1LX can choose to stop or report these lines.
        /// </summary>
        public bool IncludeUnlabelled { get; set; } = true;

        /// <summary>
        /// When set, only the endpoint-connected component containing this line is returned.
        /// The initial entity pass stays linear while label matching is limited to the route.
        /// </summary>
        public ObjectId RootLineId { get; set; } = ObjectId.Null;

        public double EndpointTolerance { get; set; } = 1.0;
    }

    /// <summary>One validated distance edit returned by the 3D editor.</summary>
    public sealed class QuickLineLabelUpdate
    {
        public QuickLineLabelUpdate(ObjectId labelId, double millimetres)
        {
            LabelId = labelId;
            Millimetres = millimetres;
        }

        public ObjectId LabelId { get; }
        public double Millimetres { get; }
    }

    /// <summary>
    /// AutoCAD adapter used by U1LX.  It deliberately keeps the scan and each
    /// mutation in a short transaction so a cancelled interaction does not
    /// hold database objects open.
    /// </summary>
    public static class QuickLineCadService
    {
        private const double Epsilon = 1e-9;
        // ObjectClass lookups avoid opening entities that can never be scan
        // candidates.  IsDerivedFrom covers proxy-safe subclasses the same way
        // the "as" casts below would.
        private static readonly Autodesk.AutoCAD.Runtime.RXClass LineClass
            = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Line));
        private static readonly Autodesk.AutoCAD.Runtime.RXClass DbTextClass
            = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(DBText));
        private static readonly Autodesk.AutoCAD.Runtime.RXClass MTextClass
            = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(MText));
        private static readonly Autodesk.AutoCAD.Runtime.RXClass DimensionClass
            = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Dimension));
        private static readonly Autodesk.AutoCAD.Runtime.RXClass BlockClass
            = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference));
        /// <summary>
        /// Registered application used for U1L/U1LX metadata.  Keeping the
        /// association in the drawing makes the scanner reliable after a
        /// label is moved, and the completion flag remains valid when the
        /// measured value happens to equal the placeholder.
        /// </summary>
        public const string MetadataApplicationName = QuickLineMetadataStore.ApplicationName;

        /// <summary>
        /// Scans only the active model/paper space.  Every Line is returned;
        /// text, native linear dimensions and block attributes are matched
        /// one-to-one to a line.  Native dimensions are treated as measured
        /// observations and take precedence over a still-unedited U1L
        /// placeholder label when both are present.
        /// </summary>
        public static IReadOnlyList<QuickLineCadSegment> Scan(CadContext ctx,
            QuickLineScanOptions options = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            options = options ?? new QuickLineScanOptions();
            ValidateScanOptions(options);

            var lines = new List<LineSnapshot>();
            var labels = new List<LabelSnapshot>();
            int order = 0;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                var space = tr.GetObject(ctx.CurrentSpaceId, OpenMode.ForRead, false)
                    as BlockTableRecord;
                if (space == null)
                {
                    tr.Commit();
                    return Array.Empty<QuickLineCadSegment>();
                }

                foreach (ObjectId id in space)
                {
                    int entityOrder = order++;
                    if (id.IsNull || !id.IsValid) continue;
                    // Type-filter on the ObjectClass first: opening every entity
                    // ForRead dominates scan time on dense drawings, and only
                    // lines, labels and blocks can ever become scan candidates.
                    if (!IsCandidateClass(id)) continue;
                    Entity entity;
                    try
                    {
                        entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    }
                    catch
                    {
                        // A stale/erased id should not prevent the remaining
                        // drawing from being scanned.
                        continue;
                    }
                    if (entity == null || entity.IsErased) continue;
                    // U1LX operates on U1L design lines. Ignore U1Q/U1C generated
                    // geometry and labels so a later scan cannot treat an annotation
                    // as a new cable route.
                    if (ParallelAnnotationMetadata.TryRead(entity,
                        out _, out _)) continue;

                    var line = entity as Line;
                    if (line != null)
                    {
                        try
                        {
                            Point3d start = line.StartPoint;
                            Point3d end = line.EndPoint;
                            if (start.DistanceTo(end) > Epsilon)
                                lines.Add(new LineSnapshot(id, start, end,
                                    entityOrder, ReadLayer(line)));
                        }
                        catch
                        {
                            // A damaged line must not hide otherwise usable
                            // U1L segments from the quick-drawing command.
                        }
                        continue;
                    }

                    // AttributeReference derives from DBText.  Check it first
                    // so block attributes retain their own source kind and
                    // can be opened for write through their ObjectId later.
                    var attribute = entity as AttributeReference;
                    if (attribute != null)
                    {
                        TryAddAttributeLabel(attribute, id, entityOrder, labels);
                        continue;
                    }

                    var dbText = entity as DBText;
                    if (dbText != null)
                    {
                        TryAddDbTextLabel(dbText, id, entityOrder, labels);
                        continue;
                    }

                    var mText = entity as MText;
                    if (mText != null)
                    {
                        TryAddMTextLabel(mText, id, entityOrder, labels);
                        continue;
                    }

                    var dimension = entity as Dimension;
                    if (dimension != null)
                    {
                        TryAddDimensionLabel(dimension, id, entityOrder, labels);
                        continue;
                    }

                    var block = entity as BlockReference;
                    if (block != null)
                        TryAddBlockAttributeLabels(block, tr, labels, ref order);
                }
                tr.Commit();
            }

            if (!options.RootLineId.IsNull)
                lines = ConnectedComponent(lines, options.RootLineId,
                    options.EndpointTolerance);
            if (lines.Count == 0) return Array.Empty<QuickLineCadSegment>();

            // A native DIMENSION describes the measured geometry itself.  Keep
            // it separate from ordinary text labels so a dimension can enrich
            // an existing U1L DBText without stealing the writable label id.
            // If a drawing has no U1L text, the dimension remains a valid
            // writable label and is handled by the type-aware update path.
            var textLabels = labels.Where(item => !item.IsDimension).ToList();
            var dimensionLabels = labels.Where(item => item.IsDimension).ToList();

            // Claim explicit U1L associations first.  Older drawings do not
            // have metadata, so the deterministic geometry fallback below is
            // still required.
            var assignedLines = new Dictionary<ObjectId, LabelSnapshot>();
            var assignedLabels = new HashSet<ObjectId>();
            var linesByHandle = new Dictionary<string, LineSnapshot>(
                StringComparer.OrdinalIgnoreCase);
            foreach (LineSnapshot line in lines)
            {
                if (string.IsNullOrEmpty(line.Handle)
                    || linesByHandle.ContainsKey(line.Handle)) continue;
                linesByHandle.Add(line.Handle, line);
            }
            foreach (LabelSnapshot label in textLabels
                .Where(item => !string.IsNullOrEmpty(item.AssociatedLineHandle)))
            {
                if (!linesByHandle.TryGetValue(label.AssociatedLineHandle,
                        out LineSnapshot line))
                    continue;
                if (assignedLines.ContainsKey(line.Id)
                    || assignedLabels.Contains(label.Id)) continue;
                assignedLines[line.Id] = label;
                assignedLabels.Add(label.Id);
            }

            // U1L versions before metadata was introduced commonly append a
            // line and its DBText consecutively, but some legacy drawings put
            // two lines before the corresponding text.  Resolve these local
            // runs before the broad geometry pass.  A valid geometric score
            // wins over a merely adjacent line; an immediate neighbour is a
            // last-resort fallback for labels that were moved far away.
            foreach (LabelSnapshot label in textLabels.OrderBy(item => item.Order))
            {
                if (assignedLabels.Contains(label.Id)) continue;
                var localCandidates = lines
                    .Where(line => !assignedLines.ContainsKey(line.Id)
                        && label.Order > line.Order
                        && label.Order - line.Order <= 2
                        && IsLikelyAdjacentLabel(line, label))
                    .Select(line =>
                    {
                        double score;
                        bool scored = TryScore(line, label, 0.0, out score);
                        return new LegacyCandidate(line, scored, score,
                            label.Order - line.Order);
                    })
                    .OrderBy(candidate => candidate.Scored ? 0 : 1)
                    .ThenBy(candidate => candidate.Scored
                        ? candidate.Score : double.PositiveInfinity)
                    .ThenBy(candidate => candidate.OrderDistance)
                    .ThenBy(candidate => candidate.Line.Order)
                    .ToList();
                if (localCandidates.Count == 0) continue;

                LegacyCandidate chosen = localCandidates[0];
                // Do not let a far, unscored immediate neighbour steal a label
                // from a preceding line that has a real midpoint match.
                if (!chosen.Scored && localCandidates.Any(item => item.Scored))
                    chosen = localCandidates.First(item => item.Scored);
                assignedLines[chosen.Line.Id] = label;
                assignedLabels.Add(label.Id);
            }

            // A drafter may keep the U1L 2000mm DBText as the writable target
            // and add the measured value as another TEXT/MTEXT/block attribute.
            // Match those observations before the broad pass can attach them
            // to a different nearby line, then retain the U1L target id while
            // exposing the observed value to the 3D editor.
            var observationLines = lines.Where(line =>
                    assignedLines.TryGetValue(line.Id, out LabelSnapshot current)
                    && !current.HasCompletionMarker)
                .ToList();
            var observationLabels = textLabels.Where(label =>
                    !assignedLabels.Contains(label.Id)
                    && IsMeasuredObservation(label))
                .ToList();
            if (observationLines.Count > 0 && observationLabels.Count > 0)
            {
                var observations = new Dictionary<ObjectId, LabelSnapshot>();
                var usedObservations = new HashSet<ObjectId>();
                AssignRemainingLabels(observationLines, observationLabels,
                    options.LabelSearchDistance, observations, usedObservations);
                foreach (KeyValuePair<ObjectId, LabelSnapshot> item in observations)
                {
                    assignedLines[item.Key] = assignedLines[item.Key]
                        .WithObservation(item.Value);
                    assignedLabels.Add(item.Value.Id);
                }
            }

            // Match whatever remains with an augmenting-path assignment.  A
            // plain score-sorted greedy pass can consume a shared nearby label
            // and leave its line unmatched; the bipartite walk preserves the
            // maximum number of one-to-one matches while still trying the
            // lowest-cost candidate first.
            AssignRemainingLabels(lines, textLabels, options.LabelSearchDistance,
                assignedLines, assignedLabels);

            // Resolve dimensions independently.  A dimension may point to a
            // line that already has a U1L placeholder; in that case its value
            // is copied onto the DBText/MTEXT/attribute snapshot while the
            // writable target id is retained.  This is what makes an ordinary
            // DIMALIGNED/DIMLINEAR annotation visible in U1LX immediately.
            var assignedDimensions = new Dictionary<ObjectId, LabelSnapshot>();
            AssignRemainingLabels(lines, dimensionLabels,
                options.LabelSearchDistance, assignedDimensions,
                new HashSet<ObjectId>());

            var result = new List<QuickLineCadSegment>(lines.Count);
            foreach (LineSnapshot line in lines)
            {
                assignedLines.TryGetValue(line.Id, out LabelSnapshot label);
                if (assignedDimensions.TryGetValue(line.Id,
                        out LabelSnapshot dimension))
                {
                    label = label == null
                        ? dimension
                        : label.WithObservation(dimension);
                }
                if (label == null && !options.IncludeUnlabelled) continue;
                result.Add(new QuickLineCadSegment(line.Id, line.Start, line.End,
                    label?.Id ?? ObjectId.Null,
                    label?.Text ?? string.Empty,
                    label?.Millimetres,
                    label?.HasCompletionMarker ?? false,
                    label?.SourceKind ?? string.Empty));
            }
            return result;
        }

        /// <summary>
        /// Cheap pre-filter: true when the entity class can be a scan candidate
        /// (a Line, one of the label kinds, or a block with attribute labels).
        /// Avoids opening obviously unrelated entities (polylines, hatches,
        /// xrefs' local geometry, proxy objects) on dense drawings.
        /// </summary>
        private static bool IsCandidateClass(ObjectId id)
        {
            try
            {
                Autodesk.AutoCAD.Runtime.RXClass cls = id.ObjectClass;
                return cls == LineClass
                    || cls == DbTextClass
                    || cls.IsDerivedFrom(DbTextClass)   // includes AttributeReference
                    || cls == MTextClass
                    || cls.IsDerivedFrom(DimensionClass)
                    || cls.IsDerivedFrom(BlockClass);
            }
            catch
            {
                return true; // cannot classify: keep the old open-and-check path
            }
        }

        private static List<LineSnapshot> ConnectedComponent(
            IReadOnlyList<LineSnapshot> lines, ObjectId rootLineId, double tolerance)
        {
            LineSnapshot root = lines.FirstOrDefault(line => line.Id == rootLineId);
            if (root == null) return new List<LineSnapshot>();

            var byHandle = lines.Where(line => !string.IsNullOrEmpty(line.Handle))
                .ToDictionary(line => line.Handle, StringComparer.OrdinalIgnoreCase);
            QuickLineGraph graph = QuickLineGraph.Build(byHandle.Values.Select(line =>
                new UNCAD.Core.QuickLine.QuickLineSegment(line.Handle,
                    new QuickLinePoint(line.Start.X, line.Start.Y),
                    new QuickLinePoint(line.End.X, line.End.Y))), tolerance);
            var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            connected.Add(root.Handle);
            queue.Enqueue(root.Handle);
            while (queue.Count > 0)
            {
                foreach (QuickLineConnection connection in graph.GetConnections(queue.Dequeue()))
                    if (connected.Add(connection.ConnectedSegmentId))
                        queue.Enqueue(connection.ConnectedSegmentId);
            }
            return lines.Where(line => connected.Contains(line.Handle)).ToList();
        }

        /// <summary>
        /// Links a newly-created U1L label to its line inside an existing
        /// transaction.  The method intentionally does not commit or abort
        /// the transaction; the caller retains ownership of that decision.
        /// A metadata failure is reported as false so drawing creation can
        /// still fall back to the legacy geometry scanner.
        /// </summary>
        public static bool TryLinkLineAndLabel(CadContext ctx,
            Transaction transaction, ObjectId lineId, ObjectId labelId)
        {
            if (ctx == null || transaction == null || lineId.IsNull
                || !lineId.IsValid || labelId.IsNull || !labelId.IsValid)
                return false;

            try
            {
                var text = transaction.GetObject(labelId, OpenMode.ForWrite, false)
                    as DBText;
                if (text == null || text.IsErased) return false;

                bool completed = QuickLineMetadataStore.HasCompletionMarker(text);
                if (!QuickLineMetadataStore.EnsureApplication(ctx.Db, transaction)) return false;
                text.XData = QuickLineMetadataStore.Build(text,
                    lineId.Handle.ToString(), completed);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Reads a selected Line without retaining an AutoCAD object.</summary>
        public static bool TryReadLine(CadContext ctx, ObjectId lineId,
            out Point3d startPoint, out Point3d endPoint)
        {
            startPoint = Point3d.Origin;
            endPoint = Point3d.Origin;
            if (ctx == null || lineId.IsNull || !lineId.IsValid) return false;
            try
            {
                using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    var line = tr.GetObject(lineId, OpenMode.ForRead, false) as Line;
                    if (line == null || line.IsErased)
                    {
                        tr.Commit();
                        return false;
                    }
                    startPoint = line.StartPoint;
                    endPoint = line.EndPoint;
                    tr.Commit();
                    return startPoint.DistanceTo(endPoint) > Epsilon;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Parses the strict mm form emitted by U1L (for example 2000mm).</summary>
        public static bool TryParseMillimetres(string value, out double millimetres)
            => QuickLineMillimeterText.TryParse(value, out millimetres);

        /// <summary>
        /// Replaces a supported text or linear dimension label while retaining
        /// its style, layer, placement and rotation.
        /// </summary>
        public static bool TryUpdateMillimetreLabel(CadContext ctx, ObjectId labelId,
            double millimetres)
        {
            if (ctx == null || labelId.IsNull || !labelId.IsValid)
                return false;

            if (double.IsNaN(millimetres) || double.IsInfinity(millimetres)
                || millimetres < 0.0)
                return false;

            try
            {
                using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    var entity = tr.GetObject(labelId, OpenMode.ForWrite, false)
                        as Entity;
                    if (entity == null || entity.IsErased)
                    {
                        tr.Commit();
                        return false;
                    }
                    if (!TryPrepareLabelUpdate(entity, millimetres,
                            out LabelMutation mutation)) return false;
                    mutation.Apply();
                    // The value itself is deliberately not used as the only
                    // completion signal: 2000mm can be a genuine measurement.
                    // If registration/XData is unavailable, the text update is
                    // still committed and the legacy non-placeholder heuristic
                    // remains available to the next scan.
                    if (QuickLineMetadataStore.EnsureApplication(ctx.Db, tr))
                        entity.XData = QuickLineMetadataStore.Build(entity,
                            QuickLineMetadataStore.ReadAssociatedLineHandle(entity), true);
                    tr.Commit();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Updates a segment's associated label, if one was discovered.</summary>
        public static bool TryUpdateMillimetreLabel(CadContext ctx,
            QuickLineCadSegment segment, double millimetres)
            => segment != null
                && TryUpdateMillimetreLabel(ctx, segment.LabelId, millimetres);

        /// <summary>
        /// 快速标注:更新线段已有标注;没有标注时在线段中点新建一个并写
        /// XData 关联。供 U1LX 逐段命令行填写使用。
        /// </summary>
        public static bool TryWriteSegmentMillimetre(CadContext ctx,
            QuickLineCadSegment segment, double millimetres)
        {
            if (segment == null) return false;
            if (!segment.LabelId.IsNull
                && TryUpdateMillimetreLabel(ctx, segment, millimetres)) return true;
            if (double.IsNaN(millimetres)
                || double.IsInfinity(millimetres) || millimetres < 0.0)
                return false;
            try
            {
                using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    ObjectId styleId = StyleManager.GetDrawingStandardStyle(ctx, tr);
                    double height = Settings.GetDouble(ConfigKeys.UnlHeight, 180.0);
                    if (!IsFinite(height) || height <= 0.0) height = 180.0;
                    double offset = Settings.GetDouble(ConfigKeys.UnlOffset, 0.0);
                    if (!IsFinite(offset) || offset < 0.0) offset = 0.0;
                    string pos = Settings.Get(ConfigKeys.UnlPos, "1");
                    Point3d midpoint = segment.MidPoint;
                    double angle = GeoMath.ReadableAngle(segment.StartPoint,
                        segment.EndPoint);
                    AttachmentPoint alignment;
                    bool above = string.Equals(pos, "2",
                        StringComparison.OrdinalIgnoreCase);
                    if (string.Equals(pos, "0", StringComparison.OrdinalIgnoreCase))
                    {
                        alignment = AttachmentPoint.MiddleCenter;
                    }
                    else
                    {
                        alignment = above ? AttachmentPoint.BottomCenter
                            : AttachmentPoint.TopCenter;
                        midpoint = GeoMath.Polar(midpoint,
                            GeoMath.SideDirection(angle, above), offset);
                    }
                    DBText label = EntityFactory.DBText(ctx,
                        QuickLineMillimeterText.Format(millimetres), midpoint,
                        height, angle, alignment, textStyleId: styleId);
                    ctx.AddToCurrentSpace(tr, label);
                    // XData is an acceleration/association hint, not a prerequisite for
                    // drawing the label. Restricted or legacy drawings may reject RegApp
                    // registration; keep the visible measurement and let the next scan
                    // fall back to geometric matching.
                    if (!TryLinkLineAndLabel(ctx, tr, segment.LineId, label.Id))
                        Log.Warn("U1LX 无法写入线段关联元数据，已保留毫米标注: "
                            + segment.LineId.Handle);
                    tr.Commit();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Applies a batch of label edits in a single transaction.
        /// Every object and value is validated before the first text is changed;
        /// disposal without Commit rolls the complete batch back on any failure.
        /// </summary>
        public static bool TryUpdateMillimetreLabels(CadContext ctx,
            IEnumerable<QuickLineLabelUpdate> updates)
        {
            if (ctx == null || updates == null) return false;

            List<QuickLineLabelUpdate> batch;
            try
            {
                batch = updates.ToList();
            }
            catch
            {
                return false;
            }
            if (batch.Count == 0) return true;

            var seen = new HashSet<ObjectId>();
            foreach (QuickLineLabelUpdate update in batch)
            {
                if (update == null || update.LabelId.IsNull
                    || !update.LabelId.IsValid || !seen.Add(update.LabelId)
                    || double.IsNaN(update.Millimetres)
                    || double.IsInfinity(update.Millimetres)
                    || update.Millimetres < 0.0)
                    return false;
            }

            try
            {
                using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    var mutations = new List<LabelMutation>(batch.Count);
                    foreach (QuickLineLabelUpdate update in batch)
                    {
                        var entity = tr.GetObject(update.LabelId,
                            OpenMode.ForWrite, false) as Entity;
                        if (entity == null || entity.IsErased) return false;
                        if (!TryPrepareLabelUpdate(entity, update.Millimetres,
                                out LabelMutation mutation)) return false;
                        mutations.Add(mutation);
                    }

                    bool metadataReady = QuickLineMetadataStore.EnsureApplication(ctx.Db, tr);
                    foreach (LabelMutation mutation in mutations)
                    {
                        string associatedLine = QuickLineMetadataStore.ReadAssociatedLineHandle(
                            mutation.Entity);
                        mutation.Apply();
                        if (metadataReady)
                            mutation.Entity.XData = QuickLineMetadataStore.Build(mutation.Entity,
                                associatedLine, true);
                    }
                    tr.Commit();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryPrepareLabelUpdate(Entity entity,
            double millimetres, out LabelMutation mutation)
        {
            mutation = null;
            var attribute = entity as AttributeReference;
            if (attribute != null)
            {
                if (!TryBuildTextReplacement(attribute.TextString, millimetres,
                        out string replacement)) return false;
                mutation = new LabelMutation(attribute, () =>
                {
                    attribute.TextString = replacement;
                    if (attribute.IsMTextAttribute)
                    {
                        using (MText mtext = attribute.MTextAttribute)
                        {
                            if (mtext != null)
                            {
                                mtext.Contents = replacement;
                                attribute.MTextAttribute = mtext;
                            }
                        }
                        attribute.UpdateMTextAttribute();
                    }
                    try { attribute.AdjustAlignment(entity.Database); }
                    catch { }
                });
                return true;
            }

            var dbText = entity as DBText;
            if (dbText != null)
            {
                if (!TryBuildTextReplacement(dbText.TextString, millimetres,
                        out string replacement)) return false;
                mutation = new LabelMutation(dbText,
                    () => dbText.TextString = replacement);
                return true;
            }

            var mText = entity as MText;
            if (mText != null)
            {
                string original = string.IsNullOrWhiteSpace(mText.Contents)
                    ? mText.Text : mText.Contents;
                if (!TryBuildTextReplacement(original, millimetres,
                        out string replacement)) return false;
                mutation = new LabelMutation(mText,
                    () => mText.Contents = replacement);
                return true;
            }

            var dimension = entity as Dimension;
            if (dimension != null && (dimension is AlignedDimension
                || dimension is RotatedDimension))
            {
                string formatted = QuickLineMillimeterText.Format(millimetres);
                mutation = new LabelMutation(dimension,
                    () => dimension.DimensionText = formatted);
                return true;
            }
            return false;
        }

        private static bool TryBuildTextReplacement(string original,
            double millimetres, out string replacement)
        {
            if (QuickLineMillimeterText.TryReplaceSingleCad(original,
                    millimetres, out replacement)) return true;
            if (QuickLineMillimeterText.TryParseNumber(original, out _))
            {
                replacement = QuickLineMillimeterText.Format(millimetres);
                return true;
            }
            replacement = string.Empty;
            return false;
        }

        public static string FormatMillimetres(double millimetres)
            => QuickLineMillimeterText.Format(millimetres);

        private static bool IsFinite(Point3d point)
            => !double.IsNaN(point.X) && !double.IsInfinity(point.X)
                && !double.IsNaN(point.Y) && !double.IsInfinity(point.Y)
                && !double.IsNaN(point.Z) && !double.IsInfinity(point.Z);

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>
        /// Moves the current AutoCAD view so that the supplied WCS point is at
        /// the viewport centre while preserving zoom, target and twist.
        /// </summary>
        public static bool TryCenterView(CadContext ctx, Point3d center)
        {
            if (ctx == null) return false;
            try
            {
                using (ViewTableRecord view = ctx.Ed.GetCurrentView())
                {
                    if (view == null) return false;
                    // CenterPoint is expressed in DCS, while entity geometry
                    // and the supplied midpoint are WCS.  Target translation
                    // and view twist are both part of the WCS-to-DCS transform;
                    // omitting either pans to the wrong point in a rotated or
                    // oblique view.
                    Point3d dcs = ToDisplayCoordinates(center,
                        view.ViewDirection, view.Target, view.ViewTwist);
                    view.CenterPoint = new Point2d(dcs.X, dcs.Y);
                    ctx.Ed.SetCurrentView(view);
                    return true;
                }
            }
            catch
            {
                // View changes are a convenience; an unavailable viewport
                // must not discard a successfully edited annotation.
                return false;
            }
        }

        public static bool TryCenterView(CadContext ctx, QuickLineCadSegment segment)
            => segment != null && TryCenterView(ctx, segment.MidPoint);

        private static Point3d ToDisplayCoordinates(Point3d point,
            Vector3d viewDirection, Point3d target, double viewTwist)
        {
            // Build DCS-to-WCS in the same order used by AutoCAD's view APIs,
            // then invert it.  In particular, DCS origin is the WCS Target,
            // not the WCS origin.
            Matrix3d displayToWorld = Matrix3d.PlaneToWorld(viewDirection);
            displayToWorld = Matrix3d.Displacement(target - Point3d.Origin)
                * displayToWorld;
            displayToWorld = Matrix3d.Rotation(-viewTwist, viewDirection, target)
                * displayToWorld;
            return point.TransformBy(displayToWorld.Inverse());
        }

        private static void ValidateScanOptions(QuickLineScanOptions options)
        {
            if (double.IsNaN(options.LabelSearchDistance)
                || double.IsInfinity(options.LabelSearchDistance)
                || options.LabelSearchDistance < 0.0)
                throw new ArgumentOutOfRangeException(nameof(options.LabelSearchDistance),
                    "Label search distance must be finite and non-negative.");
            if (double.IsNaN(options.EndpointTolerance)
                || double.IsInfinity(options.EndpointTolerance)
                || options.EndpointTolerance < 0.0)
                throw new ArgumentOutOfRangeException(nameof(options.EndpointTolerance),
                    "Endpoint tolerance must be finite and non-negative.");
        }

        private static void TryAddDbTextLabel(DBText text, ObjectId id,
            int order, ICollection<LabelSnapshot> labels)
        {
            try
            {
                string associatedLineHandle = QuickLineMetadataStore.ReadAssociatedLineHandle(text);
                bool parsed = TryParseTextValue(text.TextString, out double mm);
                // A user may have edited a linked U1L label from "2000mm" to
                // a bare number.  Bare numbers are accepted only with the
                // persisted line association; otherwise machine IDs would be
                // indistinguishable from distances.
                if (!parsed && !string.IsNullOrEmpty(associatedLineHandle))
                    parsed = QuickLineMillimeterText.TryParseBareNumber(
                        text.TextString, out mm);
                if (!parsed) return;
                labels.Add(new LabelSnapshot(id, text.TextString, mm,
                    GetTextReferences(text), Math.Max(0.0, text.Height), order,
                    ReadLayer(text), associatedLineHandle,
                    QuickLineMetadataStore.HasCompletionMarker(text), ReadRotation(text), false,
                    null, null, "DBText"));
            }
            catch
            {
                // Legacy/corrupt DBText can throw while its text or height is
                // regenerated.  Ignore that label only.
            }
        }

        private static void TryAddAttributeLabel(AttributeReference attribute,
            ObjectId id, int order, ICollection<LabelSnapshot> labels)
        {
            try
            {
                string associatedLineHandle = QuickLineMetadataStore.ReadAssociatedLineHandle(attribute);
                bool parsed = TryParseTextValue(attribute.TextString,
                    out double mm);
                if (!parsed && !string.IsNullOrEmpty(associatedLineHandle))
                    parsed = QuickLineMillimeterText.TryParseBareNumber(
                        attribute.TextString, out mm);
                if (!parsed) return;
                labels.Add(new LabelSnapshot(id, attribute.TextString, mm,
                    GetTextReferences(attribute), Math.Max(0.0, attribute.Height),
                    order, ReadLayer(attribute), associatedLineHandle,
                    QuickLineMetadataStore.HasCompletionMarker(attribute), ReadRotation(attribute), false,
                    null, null, "Attribute"));
            }
            catch
            {
                // A malformed block attribute must not abort the complete scan.
            }
        }

        private static void TryAddMTextLabel(MText text, ObjectId id, int order,
            ICollection<LabelSnapshot> labels)
        {
            try
            {
                string raw = text.Contents;
                if (string.IsNullOrWhiteSpace(raw)) raw = text.Text;
                string associatedLineHandle = QuickLineMetadataStore.ReadAssociatedLineHandle(text);
                bool parsed = TryParseTextValue(raw, out double mm);
                if (!parsed && !string.IsNullOrEmpty(associatedLineHandle))
                    parsed = QuickLineMillimeterText.TryParseBareNumber(
                        raw, out mm);
                if (!parsed) return;
                labels.Add(new LabelSnapshot(id, raw, mm,
                    GetTextReferences(text), Math.Max(0.0, text.TextHeight), order,
                    ReadLayer(text), associatedLineHandle,
                    QuickLineMetadataStore.HasCompletionMarker(text), ReadRotation(text), false,
                    null, null, "MText"));
            }
            catch
            {
                // MTEXT formatting/column corruption is isolated to this label.
            }
        }

        private static void TryAddDimensionLabel(Dimension dimension, ObjectId id,
            int order, ICollection<LabelSnapshot> labels)
        {
            // Angular/radial/ordinate dimensions do not represent a linear
            // U1L segment.  Aligned and rotated dimensions expose two witness
            // points that can be matched exactly to a Line's endpoints.
            if (!TryGetLinearDimensionPoints(dimension,
                    out Point3d first, out Point3d second)) return;
            try
            {
                if (!QuickLineMillimeterText.TryResolveDimensionValue(
                        dimension.DimensionText, dimension.Measurement,
                        out double mm)) return;
                var references = new List<Point3d>
                {
                    first,
                    second
                };
                AddReference(references, dimension.TextPosition);
                AddReference(references, GetExtentsCenter(dimension));
                labels.Add(new LabelSnapshot(id, dimension.DimensionText,
                    mm, references, Math.Max(0.0, dimension.Dimtxt), order,
                    ReadLayer(dimension), QuickLineMetadataStore.ReadAssociatedLineHandle(dimension),
                    QuickLineMetadataStore.HasCompletionMarker(dimension), ReadRotation(dimension), true,
                    first, second, "Dimension"));
            }
            catch
            {
                // Proxy/obsolete dimension data may throw on Measurement or
                // witness points; ignore only that object.
            }
        }

        private static void TryAddBlockAttributeLabels(BlockReference block,
            Transaction transaction, ICollection<LabelSnapshot> labels,
            ref int order)
        {
            try
            {
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    int attributeOrder = order++;
                    if (attributeId.IsNull || !attributeId.IsValid) continue;
                    try
                    {
                        var attribute = transaction.GetObject(attributeId,
                            OpenMode.ForRead, false) as AttributeReference;
                        if (attribute != null && !attribute.IsErased)
                            TryAddAttributeLabel(attribute, attributeId,
                                attributeOrder, labels);
                    }
                    catch
                    {
                        // A single stale attribute id must not hide other labels.
                    }
                }
            }
            catch
            {
                // AttributeCollection is unavailable for anonymous/proxy blocks.
            }
        }

        private static bool TryGetLinearDimensionPoints(Dimension dimension,
            out Point3d first, out Point3d second)
        {
            first = Point3d.Origin;
            second = Point3d.Origin;
            var aligned = dimension as AlignedDimension;
            if (aligned != null)
            {
                first = aligned.XLine1Point;
                second = aligned.XLine2Point;
                return first.DistanceTo(second) > Epsilon;
            }
            var rotated = dimension as RotatedDimension;
            if (rotated != null)
            {
                first = rotated.XLine1Point;
                second = rotated.XLine2Point;
                return first.DistanceTo(second) > Epsilon;
            }
            return false;
        }

        private static bool TryParseTextValue(string value, out double mm)
        {
            if (QuickLineMillimeterText.TryParseCad(value, out mm)) return true;
            return QuickLineMillimeterText.TryExtractSingleCad(value, out mm);
        }

        private static bool IsMeasuredObservation(LabelSnapshot label)
        {
            if (label == null || !string.IsNullOrEmpty(
                    label.AssociatedLineHandle)) return false;
            if (!string.Equals(label.SourceKind, "DBText",
                    StringComparison.OrdinalIgnoreCase)) return true;
            return label.HasCompletionMarker
                || Math.Abs(label.Millimetres - 2000.0) > 1e-7;
        }

        private static Point3d GetExtentsCenter(Entity entity)
        {
            try
            {
                Extents3d extents = entity.GeometricExtents;
                return new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) / 2.0);
            }
            catch
            {
                return Point3d.Origin;
            }
        }

        private static string ReadLayer(Entity entity)
        {
            try { return entity.Layer ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static double ReadRotation(Entity entity)
        {
            try
            {
                var dbText = entity as DBText;
                if (dbText != null) return dbText.Rotation;
                var mText = entity as MText;
                if (mText != null) return mText.Rotation;
                var dimension = entity as Dimension;
                if (dimension != null) return dimension.TextRotation;
            }
            catch
            {
                // Fall through to the invalid marker used by adjacency matching.
            }
            return double.NaN;
        }

        private static bool IsLikelyAdjacentLabel(LineSnapshot line,
            LabelSnapshot label)
        {
            if (string.IsNullOrEmpty(line.Layer)
                || string.IsNullOrEmpty(label.Layer)) return false;
            if (!string.Equals(line.Layer, label.Layer,
                    StringComparison.OrdinalIgnoreCase)) return false;
            Vector3d direction = line.End - line.Start;
            if (direction.Length < Epsilon || double.IsNaN(label.Rotation)
                || double.IsInfinity(label.Rotation)) return false;
            double lineAngle = Math.Atan2(direction.Y, direction.X);
            double difference = Math.Abs(NormalizeAngle(lineAngle)
                - NormalizeAngle(label.Rotation));
            difference = Math.Min(difference, Math.PI - difference);
            return difference <= 0.18; // approximately ten degrees
        }

        private static double NormalizeAngle(double angle)
        {
            double normalized = angle % Math.PI;
            if (normalized < 0.0) normalized += Math.PI;
            return normalized;
        }

        private static IReadOnlyList<Point3d> GetTextReferences(Entity entity)
        {
            var references = new List<Point3d>();
            if (entity == null) return references;
            try
            {
                Extents3d extents = entity.GeometricExtents;
                AddReference(references, new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) / 2.0));
            }
            catch
            {
                // Position/alignment are still useful when extents need
                // regeneration, so the fallback below is intentionally broad.
            }

            var dbText = entity as DBText;
            if (dbText != null)
            {
                try
                {
                    if (dbText.Justify != AttachmentPoint.BaseLeft)
                        AddReference(references, dbText.AlignmentPoint);
                }
                catch { }
                try { AddReference(references, dbText.Position); }
                catch { }
            }

            var attribute = entity as AttributeReference;
            if (attribute != null)
            {
                try { AddReference(references, attribute.AlignmentPoint); }
                catch { }
                try { AddReference(references, attribute.Position); }
                catch { }
            }

            var mText = entity as MText;
            if (mText != null)
            {
                try { AddReference(references, mText.Location); }
                catch { }
            }

            var dimension = entity as Dimension;
            if (dimension != null)
            {
                try { AddReference(references, dimension.TextPosition); }
                catch { }
            }
            return references;
        }

        private static void AddReference(List<Point3d> references, Point3d point)
        {
            foreach (Point3d existing in references)
                if (existing.DistanceTo(point) <= 1e-7) return;
            references.Add(point);
        }

        private static void AssignRemainingLabels(
            IReadOnlyList<LineSnapshot> lines,
            IReadOnlyList<LabelSnapshot> labels,
            double configuredDistance,
            IDictionary<ObjectId, LabelSnapshot> assignedLines,
            ISet<ObjectId> assignedLabels)
        {
            var candidates = new Dictionary<ObjectId, List<LabelMatch>>();
            foreach (LineSnapshot line in lines)
            {
                if (assignedLines.ContainsKey(line.Id)) continue;
                var list = new List<LabelMatch>();
                foreach (LabelSnapshot label in labels)
                {
                    if (assignedLabels.Contains(label.Id)) continue;
                    double score;
                    if (TryScore(line, label, configuredDistance, out score))
                        list.Add(new LabelMatch(line, label, score));
                }
                list.Sort(CompareLabelMatches);
                candidates[line.Id] = list;
            }

            var ownerByLabel = new Dictionary<ObjectId, LineSnapshot>();
            var matchByLine = new Dictionary<ObjectId, LabelSnapshot>();
            foreach (KeyValuePair<ObjectId, List<LabelMatch>> candidate in candidates
                .Where(item => item.Value.Count > 0)
                .OrderBy(item => item.Value.Count)
                .ThenBy(item => item.Value[0].Line.Order))
            {
                LineSnapshot line = candidate.Value[0].Line;
                TryAssignLine(line, candidates, ownerByLabel, matchByLine,
                    new HashSet<ObjectId>(), new HashSet<ObjectId>());
            }

            foreach (KeyValuePair<ObjectId, LabelSnapshot> match in matchByLine)
            {
                assignedLines[match.Key] = match.Value;
                assignedLabels.Add(match.Value.Id);
            }
        }

        private static bool TryAssignLine(
            LineSnapshot line,
            IDictionary<ObjectId, List<LabelMatch>> candidates,
            IDictionary<ObjectId, LineSnapshot> ownerByLabel,
            IDictionary<ObjectId, LabelSnapshot> matchByLine,
            ISet<ObjectId> visitedLines,
            ISet<ObjectId> visitedLabels)
        {
            if (!visitedLines.Add(line.Id)) return false;
            List<LabelMatch> choices;
            if (!candidates.TryGetValue(line.Id, out choices)) return false;

            foreach (LabelMatch choice in choices)
            {
                if (!visitedLabels.Add(choice.Label.Id)) continue;
                if (!ownerByLabel.TryGetValue(choice.Label.Id,
                        out LineSnapshot owner)
                    || TryAssignLine(owner, candidates, ownerByLabel,
                        matchByLine, visitedLines, visitedLabels))
                {
                    ownerByLabel[choice.Label.Id] = line;
                    matchByLine[line.Id] = choice.Label;
                    return true;
                }
            }
            return false;
        }

        private static int CompareLabelMatches(LabelMatch left,
            LabelMatch right)
        {
            int score = left.Score.CompareTo(right.Score);
            if (score != 0) return score;
            int distance = Math.Abs(left.Line.Order - left.Label.Order)
                .CompareTo(Math.Abs(right.Line.Order - right.Label.Order));
            if (distance != 0) return distance;
            return string.Compare(left.Label.Id.Handle.ToString(),
                right.Label.Id.Handle.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryScore(LineSnapshot line, LabelSnapshot label,
            double configuredDistance, out double score)
        {
            score = double.PositiveInfinity;
            Vector3d direction = line.End - line.Start;
            double lengthSquared = direction.DotProduct(direction);
            if (lengthSquared < Epsilon) return false;

            if (label.IsDimension && label.WitnessStart.HasValue
                && label.WitnessEnd.HasValue)
            {
                Point3d first = label.WitnessStart.Value;
                Point3d second = label.WitnessEnd.Value;
                double endpointTolerance = Math.Max(1.0,
                    Math.Sqrt(lengthSquared) * 0.005);
                bool forward = line.Start.DistanceTo(first) <= endpointTolerance
                    && line.End.DistanceTo(second) <= endpointTolerance;
                bool reverse = line.Start.DistanceTo(second) <= endpointTolerance
                    && line.End.DistanceTo(first) <= endpointTolerance;
                if (forward || reverse)
                {
                    score = -10.0;
                    return true;
                }

                // Extension lines are commonly offset from the measured LINE.
                // Match their direction, midpoint projection and measurement,
                // but keep a tight tolerance so a nearby unrelated dimension
                // cannot replace a U1L value.
                Vector3d witnessDirection = second - first;
                double witnessLength = witnessDirection.Length;
                double lineLength = Math.Sqrt(lengthSquared);
                if (witnessLength <= Epsilon) return false;
                double parallel = Math.Abs(direction.GetNormal()
                    .DotProduct(witnessDirection.GetNormal()));
                double lengthTolerance = Math.Max(2.0, lineLength * 0.01);
                if (parallel >= Math.Cos(Math.PI / 180.0)
                    && Math.Abs(witnessLength - lineLength) <= lengthTolerance)
                {
                    Point3d lineMid = line.Start + direction * 0.5;
                    Point3d witnessMid = first + witnessDirection * 0.5;
                    double offsetLimit = configuredDistance > 0.0
                        ? configuredDistance
                        : Math.Max(label.Height * 12.0, lineLength * 0.25);
                    double offset = lineMid.DistanceTo(witnessMid);
                    if (offset <= offsetLimit)
                    {
                        score = -5.0 + offset / Math.Max(offsetLimit, Epsilon);
                        return true;
                    }
                }
                return false;
            }

            // U1L positions text at the segment midpoint.  A small along-axis
            // tolerance deals with text extents and old drawings, while the
            // perpendicular tolerance follows the configured offset/height.
            double alongLimit = Math.Max(Math.Sqrt(lengthSquared) * 0.45,
                Math.Max(label.Height * 2.0, 25.0));
            double perpendicularLimit = configuredDistance > 0.0
                ? configuredDistance
                : Math.Max(label.Height * 6.0, 50.0);
            foreach (Point3d reference in label.References)
            {
                Vector3d toLabel = line.Start.GetVectorTo(reference);
                double projection = toLabel.DotProduct(direction) / lengthSquared;
                double alongError = Math.Abs(projection - 0.5) * Math.Sqrt(lengthSquared);
                double perpendicular = reference.DistanceTo(
                    line.Start + direction * Math.Max(0.0, Math.Min(1.0, projection)));
                if (alongError > alongLimit || perpendicular > perpendicularLimit)
                    continue;

                double candidateScore = alongError / Math.Max(alongLimit, Epsilon)
                    + perpendicular / Math.Max(perpendicularLimit, Epsilon);
                if (candidateScore < score) score = candidateScore;
            }
            return !double.IsPositiveInfinity(score);
        }

        private sealed class LineSnapshot
        {
            public LineSnapshot(ObjectId id, Point3d start, Point3d end,
                int order, string layer)
            {
                Id = id;
                Start = start;
                End = end;
                Order = order;
                Layer = layer ?? string.Empty;
                Handle = ReadHandle(id);
            }

            public ObjectId Id { get; }
            public Point3d Start { get; }
            public Point3d End { get; }
            public int Order { get; }
            public string Layer { get; }
            public string Handle { get; }
        }

        private sealed class LabelSnapshot
        {
            public LabelSnapshot(ObjectId id, string text, double millimetres,
                IReadOnlyList<Point3d> references, double height, int order,
                string layer, string associatedLineHandle, bool hasCompletionMarker,
                double rotation, bool isDimension,
                Point3d? witnessStart, Point3d? witnessEnd, string sourceKind)
            {
                Id = id;
                Text = text ?? string.Empty;
                Millimetres = millimetres;
                References = references ?? Array.Empty<Point3d>();
                Height = height;
                Order = order;
                Layer = layer ?? string.Empty;
                AssociatedLineHandle = associatedLineHandle ?? string.Empty;
                HasCompletionMarker = hasCompletionMarker;
                Rotation = rotation;
                IsDimension = isDimension;
                WitnessStart = witnessStart;
                WitnessEnd = witnessEnd;
                SourceKind = sourceKind ?? string.Empty;
            }

            public ObjectId Id { get; }
            public string Text { get; }
            public double Millimetres { get; }
            public IReadOnlyList<Point3d> References { get; }
            public double Height { get; }
            public int Order { get; }
            public string Layer { get; }
            public string AssociatedLineHandle { get; }
            public bool HasCompletionMarker { get; }
            public double Rotation { get; }
            public bool IsDimension { get; }
            public Point3d? WitnessStart { get; }
            public Point3d? WitnessEnd { get; }
            public string SourceKind { get; }

            public LabelSnapshot WithObservation(LabelSnapshot observation)
                => new LabelSnapshot(Id, Text, observation.Millimetres,
                    References, Height,
                    Order, Layer, AssociatedLineHandle, HasCompletionMarker,
                    Rotation, IsDimension, WitnessStart, WitnessEnd,
                    observation.SourceKind);
        }

        private sealed class LabelMutation
        {
            private readonly Action _apply;

            public LabelMutation(Entity entity, Action apply)
            {
                Entity = entity ?? throw new ArgumentNullException(nameof(entity));
                _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            }

            public Entity Entity { get; }
            public void Apply() => _apply();
        }

        private sealed class LegacyCandidate
        {
            public LegacyCandidate(LineSnapshot line, bool scored, double score,
                int orderDistance)
            {
                Line = line;
                Scored = scored;
                Score = score;
                OrderDistance = orderDistance;
            }

            public LineSnapshot Line { get; }
            public bool Scored { get; }
            public double Score { get; }
            public int OrderDistance { get; }
        }

        private sealed class LabelMatch
        {
            public LabelMatch(LineSnapshot line, LabelSnapshot label, double score)
            {
                Line = line;
                Label = label;
                Score = score;
            }

            public LineSnapshot Line { get; }
            public LabelSnapshot Label { get; }
            public double Score { get; }
        }

        private static string ReadHandle(ObjectId id)
        {
            try { return id.Handle.ToString(); }
            catch { return string.Empty; }
        }
    }
}
