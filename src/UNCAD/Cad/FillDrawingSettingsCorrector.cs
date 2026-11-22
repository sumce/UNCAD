using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Infra;

namespace UNCAD.Cad
{
    internal sealed class FillDrawingSettingsCorrectionResult
    {
        public int CableLabels { get; set; }
        public int TrayRuns { get; set; }
        public int ConduitRuns { get; set; }
        public int HoseLabels { get; set; }

        public int Total => CableLabels + TrayRuns + ConduitRuns + HoseLabels;
    }

    /// <summary>
    /// Applies current U1SET styles only to entities carrying UNCAD ownership metadata.
    /// Call inside the fill write transaction so cancellation and rollback stay atomic.
    /// </summary>
    internal static class FillDrawingSettingsCorrector
    {
        internal static FillDrawingSettingsCorrectionResult Apply(CadContext ctx,
            Transaction transaction, IReadOnlyList<QuickLineCadSegment> segments,
            IReadOnlyList<ParallelAnnotationOwnershipEntry> parallelOutputs,
            FillDrawingStyleSettings settings, bool governLayers,
            bool correctCableLabelPlacement,
            bool correctCableTrayConduitStyles)
        {
            var result = new FillDrawingSettingsCorrectionResult();
            if (ctx == null || transaction == null
                || settings == null) return result;

            if (correctCableLabelPlacement || correctCableTrayConduitStyles)
            {
                var segmentsByHandle = new Dictionary<string, QuickLineCadSegment>(
                    StringComparer.OrdinalIgnoreCase);
                var u1lMillimetres = new Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (QuickLineCadSegment segment in segments
                    ?? Array.Empty<QuickLineCadSegment>())
                {
                    string handle = segment.LineId.Handle.ToString();
                    if (!segmentsByHandle.ContainsKey(handle))
                        segmentsByHandle.Add(handle, segment);
                    if (segment.LabelMillimetres.HasValue)
                        u1lMillimetres[handle] = segment.LabelMillimetres.Value;
                }

                result.CableLabels = CorrectCableLabels(ctx, transaction,
                    segmentsByHandle.Values, settings,
                    correctCableTrayConduitStyles);
                if (correctCableTrayConduitStyles)
                {
                    result.TrayRuns = CorrectParallelRuns(ctx, transaction,
                        parallelOutputs, segmentsByHandle,
                        u1lMillimetres, settings, governLayers,
                        UncadLayerStandard.TrayAnnotationKind);
                    result.ConduitRuns = CorrectParallelRuns(ctx, transaction,
                        parallelOutputs, segmentsByHandle,
                        u1lMillimetres, settings, governLayers,
                        UncadLayerStandard.ConduitAnnotationKind);
                }
            }

            result.HoseLabels = CorrectHoseLabels(transaction,
                parallelOutputs, settings.CableTextHeight);
            return result;
        }

        private static int CorrectCableLabels(CadContext ctx,
            Transaction transaction, IEnumerable<QuickLineCadSegment> segments,
            FillDrawingStyleSettings settings, bool correctHeight)
        {
            int changed = 0;
            foreach (QuickLineCadSegment segment in segments
                ?? Enumerable.Empty<QuickLineCadSegment>())
            {
                var labelIds = new HashSet<ObjectId>();
                if (!segment.LabelId.IsNull) labelIds.Add(segment.LabelId);
                foreach (ObjectId id in segment.RestoredLabelIds
                    ?? Array.Empty<ObjectId>())
                    if (!id.IsNull) labelIds.Add(id);

                foreach (ObjectId labelId in labelIds)
                {
                    Entity entity;
                    try
                    {
                        entity = transaction.GetObject(labelId, OpenMode.ForRead, false)
                            as Entity;
                    }
                    catch { continue; }
                    if (entity == null || entity.IsErased
                        || entity is AttributeReference
                        || string.IsNullOrWhiteSpace(
                            QuickLineMetadataStore.ReadAssociatedLineHandle(entity)))
                        continue;

                    Point3d currentAnchor;
                    if (entity is DBText text)
                        currentAnchor = text.Justify == AttachmentPoint.BaseLeft
                            ? text.Position : text.AlignmentPoint;
                    else if (entity is MText mtext)
                        currentAnchor = mtext.Location;
                    else
                        continue;
                    if (!TryLabelPlacement(transaction, segment.LineId,
                            currentAnchor, settings, out Point3d point,
                            out double angle, out AttachmentPoint alignment))
                        continue;

                    bool entityChanged = false;
                    if (entity is DBText dbText)
                    {
                        entityChanged = (correctHeight && Math.Abs(dbText.Height
                                - settings.CableTextHeight) > 1e-6)
                            || Math.Abs(NormalizeAngle(dbText.Rotation - angle)) > 1e-6
                            || currentAnchor.DistanceTo(point) > 1e-4
                            || dbText.Justify != alignment;
                        if (!entityChanged) continue;
                        dbText.UpgradeOpen();
                        if (correctHeight) dbText.Height = settings.CableTextHeight;
                        dbText.Rotation = angle;
                        dbText.Justify = alignment;
                        if (alignment == AttachmentPoint.BaseLeft)
                            dbText.Position = point;
                        else
                        {
                            dbText.Position = point;
                            dbText.AlignmentPoint = point;
                            dbText.AdjustAlignment(ctx.Db);
                        }
                    }
                    else if (entity is MText multiText)
                    {
                        entityChanged = (correctHeight && Math.Abs(multiText.TextHeight
                                - settings.CableTextHeight) > 1e-6)
                            || Math.Abs(NormalizeAngle(multiText.Rotation - angle)) > 1e-6
                            || currentAnchor.DistanceTo(point) > 1e-4
                            || multiText.Attachment != alignment;
                        if (!entityChanged) continue;
                        multiText.UpgradeOpen();
                        if (correctHeight)
                            multiText.TextHeight = settings.CableTextHeight;
                        multiText.Rotation = angle;
                        multiText.Attachment = alignment;
                        multiText.Location = point;
                    }
                    if (entityChanged) changed++;
                }
            }
            return changed;
        }

        private static bool TryLabelPlacement(Transaction transaction,
            ObjectId lineId, Point3d currentLabelAnchor,
            FillDrawingStyleSettings settings, out Point3d labelPoint,
            out double angle, out AttachmentPoint alignment)
        {
            labelPoint = Point3d.Origin;
            angle = 0;
            string position = settings.CableTextPosition;
            bool above = position == "2";
            alignment = position == "0" ? AttachmentPoint.MiddleCenter
                : above ? AttachmentPoint.BottomCenter
                : AttachmentPoint.TopCenter;

            Curve curve;
            try
            {
                curve = transaction.GetObject(lineId, OpenMode.ForRead, false) as Curve;
            }
            catch { return false; }
            if (curve == null || curve.IsErased) return false;

            Point3d anchor;
            Vector3d tangent;
            try
            {
                if (curve is Line line)
                {
                    tangent = line.StartPoint.GetVectorTo(line.EndPoint);
                    anchor = line.StartPoint + tangent / 2.0;
                }
                else if (curve is Polyline polyline && !polyline.Closed)
                {
                    Point3d closest = polyline.GetClosestPointTo(
                        currentLabelAnchor, false);
                    double parameter = polyline.GetParameterAtPoint(closest);
                    int segmentIndex = Math.Max(0, Math.Min(
                        polyline.NumberOfVertices - 2,
                        (int)Math.Floor(parameter)));
                    double middleParameter = segmentIndex + 0.5;
                    anchor = polyline.GetPointAtParameter(middleParameter);
                    tangent = polyline.GetFirstDerivative(middleParameter);
                }
                else
                {
                    return false;
                }
                if (tangent.Length <= 1e-9) return false;
                angle = GeoMath.ReadableAngle(anchor, anchor + tangent);
                labelPoint = GeoMath.Polar(anchor,
                    GeoMath.SideDirection(angle, above),
                    position == "0" ? 0.0 : settings.CableTextOffset);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int CorrectHoseLabels(Transaction transaction,
            IReadOnlyList<ParallelAnnotationOwnershipEntry> outputs,
            double textHeight)
        {
            int changed = 0;
            foreach (ParallelAnnotationOwnershipEntry entry in outputs
                ?? Array.Empty<ParallelAnnotationOwnershipEntry>())
            {
                if (!string.Equals(entry?.Kind,
                        UncadLayerStandard.HoseAnnotationKind,
                        StringComparison.OrdinalIgnoreCase)) continue;
                Entity entity;
                try
                {
                    entity = transaction.GetObject(entry.EntityId,
                        OpenMode.ForRead, false) as Entity;
                }
                catch { continue; }
                if (entity == null || entity.IsErased
                    || entity is AttributeReference) continue;
                if (entity is DBText dbText
                    && Math.Abs(dbText.Height - textHeight) > 1e-6)
                {
                    dbText.UpgradeOpen();
                    dbText.Height = textHeight;
                    changed++;
                }
                else if (entity is MText mtext
                    && Math.Abs(mtext.TextHeight - textHeight) > 1e-6)
                {
                    mtext.UpgradeOpen();
                    mtext.TextHeight = textHeight;
                    changed++;
                }
            }
            return changed;
        }

        private static int CorrectParallelRuns(CadContext ctx,
            Transaction transaction,
            IReadOnlyList<ParallelAnnotationOwnershipEntry> outputs,
            IReadOnlyDictionary<string, QuickLineCadSegment> segmentsByHandle,
            Dictionary<string, double> u1lMillimetres,
            FillDrawingStyleSettings settings, bool governLayers,
            string kind)
        {
            List<ParallelAnnotationOwnershipEntry> kindOutputs = (outputs
                ?? Array.Empty<ParallelAnnotationOwnershipEntry>())
                .Where(entry => string.Equals(entry?.Kind, kind,
                    StringComparison.OrdinalIgnoreCase)
                    && entry.HostHandles.Count > 0
                    && entry.HostHandles.All(segmentsByHandle.ContainsKey))
                .ToList();
            if (kindOutputs.Count == 0) return 0;

            var labelByHost = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            var layersByHost = new Dictionary<string, HashSet<ObjectId>>(
                StringComparer.OrdinalIgnoreCase);
            var sources = new Dictionary<string, ObjectId>(
                StringComparer.OrdinalIgnoreCase);
            foreach (ParallelAnnotationOwnershipEntry entry in kindOutputs)
            {
                Entity entity;
                try
                {
                    entity = transaction.GetObject(entry.EntityId,
                        OpenMode.ForRead, false) as Entity;
                }
                catch { continue; }
                if (entity == null || entity.IsErased) continue;
                foreach (string host in entry.HostHandles)
                {
                    if (!segmentsByHandle.TryGetValue(host,
                            out QuickLineCadSegment segment)) continue;
                    sources[host] = segment.LineId;
                    if (entity is Curve)
                    {
                        if (!layersByHost.TryGetValue(host,
                                out HashSet<ObjectId> layers))
                        {
                            layers = new HashSet<ObjectId>();
                            layersByHost.Add(host, layers);
                        }
                        layers.Add(entity.LayerId);
                    }
                    string text = ReadText(entity);
                    if (text.Length > 0 && !labelByHost.ContainsKey(host))
                        labelByHost.Add(host, text);
                }
            }
            if (sources.Count == 0 || labelByHost.Count == 0) return 0;

            ObjectId governedLayer = ObjectId.Null;
            if (governLayers)
            {
                string layerName = kind == UncadLayerStandard.TrayAnnotationKind
                    ? UncadLayerStandard.TrayLayerName
                    : UncadLayerStandard.ConduitLayerName;
                governedLayer = LayerGovernorService.EnsureLayer(ctx, transaction,
                    layerName);
                if (governedLayer.IsNull) return 0;
            }

            double textHeight;
            double lineOffset;
            double textOffset;
            bool above;
            short color;
            if (kind == UncadLayerStandard.TrayAnnotationKind)
            {
                textHeight = settings.TrayTextHeight;
                lineOffset = settings.TrayLineOffset;
                textOffset = settings.TrayTextOffset;
                above = settings.TrayAbove;
                color = UncadLayerStandard.TrayColorIndex;
            }
            else
            {
                textHeight = settings.ConduitTextHeight;
                lineOffset = settings.ConduitLineOffset;
                textOffset = settings.ConduitTextOffset;
                above = settings.ConduitAbove;
                color = 6;
            }

            string expectedStyle = ParallelAnnotationMetadata.BuildStyleStamp(
                lineOffset, textOffset, textHeight, above, color);
            if (HasCurrentStyle(transaction, kindOutputs, expectedStyle)) return 0;

            var options = new ParallelAnnotationOptions
            {
                CurveOffset = lineOffset,
                TextOffset = textOffset,
                TextHeight = textHeight,
                Above = above,
                ColorIndex = color,
                AnnotationKind = kind,
                LegacyLabelKey = UNCAD.Core.Fill.AnnotationLabelPair.CollapseText,
                TargetLayerIdFactory = run =>
                {
                    if (governLayers) return governedLayer;
                    HashSet<ObjectId> layers = new HashSet<ObjectId>();
                    foreach (Core.Geometry.ParallelRunMember member in run.Members)
                        if (layersByHost.TryGetValue(member.Handle,
                                out HashSet<ObjectId> memberLayers))
                            layers.UnionWith(memberLayers);
                    return layers.Count == 1 ? layers.Single() : ObjectId.Null;
                }
            };

            ObjectId[] sourceIds = sources.OrderBy(pair => pair.Key,
                StringComparer.OrdinalIgnoreCase).Select(pair => pair.Value)
                .Distinct().ToArray();
            return ParallelCurveAnnotator.AddRunsForSettingsCorrection(ctx,
                transaction, sourceIds, options, run =>
                {
                    foreach (Core.Geometry.ParallelRunMember member in run.Members)
                        if (labelByHost.TryGetValue(member.Handle,
                                out string text)) return text;
                    return string.Empty;
                }, kindOutputs, u1lMillimetres);
        }

        private static bool HasCurrentStyle(Transaction transaction,
            IEnumerable<ParallelAnnotationOwnershipEntry> outputs,
            string expectedStyle)
        {
            bool found = false;
            foreach (ParallelAnnotationOwnershipEntry entry in outputs
                ?? Enumerable.Empty<ParallelAnnotationOwnershipEntry>())
            {
                Entity entity;
                try
                {
                    entity = transaction.GetObject(entry.EntityId,
                        OpenMode.ForRead, false) as Entity;
                }
                catch { return false; }
                if (entity == null || entity.IsErased
                    || !ParallelAnnotationMetadata.TryRead(entity,
                        out _, out _, out _, out string styleStamp)
                    || !string.Equals(styleStamp, expectedStyle,
                        StringComparison.Ordinal))
                    return false;
                found = true;
            }
            return found;
        }

        private static string ReadText(Entity entity)
        {
            try
            {
                if (entity is DBText dbText) return dbText.TextString ?? "";
                if (entity is MText mtext) return mtext.Contents ?? "";
            }
            catch { }
            return "";
        }

        private static double NormalizeAngle(double value)
        {
            value %= Math.PI * 2;
            if (value > Math.PI) value -= Math.PI * 2;
            if (value < -Math.PI) value += Math.PI * 2;
            return value;
        }
    }
}
