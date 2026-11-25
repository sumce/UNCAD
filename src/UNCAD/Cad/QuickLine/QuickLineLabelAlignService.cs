using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.QuickLine;

namespace UNCAD.Cad.QuickLine
{
    /// <summary>U1UL 一次归位的计数。</summary>
    public sealed class QuickLineLabelAlignResult
    {
        internal QuickLineLabelAlignResult(int moved, int unchanged,
            int withoutAssociatedLabel, int withoutMetadata,
            IReadOnlyList<string> skippedHandles)
        {
            Moved = moved;
            Unchanged = unchanged;
            WithoutAssociatedLabel = withoutAssociatedLabel;
            WithoutMetadata = withoutMetadata;
            SkippedHandles = skippedHandles ?? Array.Empty<string>();
        }

        /// <summary>归位的毫米标数。</summary>
        public int Moved { get; }
        /// <summary>已在位（距离、角度、对齐都一样），没有改动的毫米标数。</summary>
        public int Unchanged { get; }
        /// <summary>线段没有绑定到任何毫米标。</summary>
        public int WithoutAssociatedLabel { get; }
        /// <summary>线段的毫米标只靠位置匹配、没有关联元数据（旧图纸）——不猜，未动。</summary>
        public int WithoutMetadata { get; }
        /// <summary>未处理的线段句柄（上面两类）。</summary>
        public IReadOnlyList<string> SkippedHandles { get; }
    }

    /// <summary>
    /// D-058（U1UL）：毫米标归位。只动「离线的距离」和「角度」：
    ///  - 距离＝U1SET 的毫米标偏移；角度＝与线段平行（保持文字原有的阅读方向）；
    ///  - 所在侧沿用毫米标现在的位置——用户把它挪到另一侧就留在那一侧；
    ///  - 沿线方向的位置也保持（投影到线上，不拉回中点）。
    /// 只处理带 U1L/U1LX 关联元数据的毫米标（扫描器只靠位置匹配到的不动，不猜）。
    /// 两遍：先对每个目标算好新位置（任一个算不出来就整体放弃，图纸不变），
    /// 再在一个事务里统一改——一次 U 整体撤销。
    /// </summary>
    internal static class QuickLineLabelAlignService
    {
        /// <summary>返回 null＝有目标算不出位置，图纸未改动。</summary>
        internal static QuickLineLabelAlignResult Apply(CadContext ctx,
            IReadOnlyList<QuickLineCadSegment> segments, double offset,
            bool centered, bool defaultAbove)
        {
            if (ctx == null || segments == null) return null;
            if (double.IsNaN(offset) || double.IsInfinity(offset) || offset < 0.0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var edits = new List<PlannedEdit>();
            var skipped = new List<string>();
            int unchanged = 0;
            int withoutLabel = 0;
            int withoutMetadata = 0;

            try
            {
                using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    foreach (QuickLineCadSegment segment in segments)
                    {
                        if (segment == null || segment.LineId.IsNull
                            || !segment.LineId.IsValid) continue;
                        string handle = segment.LineId.Handle.ToString();
                        var labelIds = new List<ObjectId>();
                        if (!segment.LabelId.IsNull) labelIds.Add(segment.LabelId);
                        foreach (ObjectId id in segment.RestoredLabelIds
                            ?? Array.Empty<ObjectId>())
                            if (!id.IsNull && !labelIds.Contains(id)) labelIds.Add(id);

                        bool hasLabel = false;
                        bool owned = false;
                        foreach (ObjectId labelId in labelIds)
                        {
                            Entity entity;
                            try
                            {
                                entity = tr.GetObject(labelId, OpenMode.ForRead, false)
                                    as Entity;
                            }
                            catch
                            {
                                continue;
                            }
                            if (entity == null || entity.IsErased
                                || entity is AttributeReference) continue;
                            hasLabel = true;
                            string owner = QuickLineMetadataStore
                                .ReadAssociatedLineHandle(entity);
                            if (string.IsNullOrWhiteSpace(owner)
                                || !string.Equals(owner, handle,
                                    StringComparison.OrdinalIgnoreCase))
                                continue;
                            owned = true;
                            // 只归位单行/多行文字；尺寸标注类的毫米标不动。
                            if (!(entity is DBText) && !(entity is MText)) continue;
                            PlannedEdit edit = Plan(tr, segment.LineId, entity,
                                offset, centered, defaultAbove);
                            if (edit == null) return null;
                            if (edit.Unchanged) unchanged++;
                            else edits.Add(edit);
                        }
                        if (owned) continue;
                        skipped.Add(handle);
                        if (hasLabel) withoutMetadata++;
                        else withoutLabel++;
                    }

                    // 第二遍：全部算得出来才动图。
                    foreach (PlannedEdit edit in edits)
                    {
                        edit.Entity.UpgradeOpen();
                        if (edit.Entity is DBText text)
                        {
                            text.Justify = edit.Alignment;
                            text.Rotation = edit.Rotation;
                            text.Position = edit.Point;
                            if (edit.Alignment != AttachmentPoint.BaseLeft)
                            {
                                text.AlignmentPoint = edit.Point;
                                text.AdjustAlignment(ctx.Db);
                            }
                        }
                        else if (edit.Entity is MText mText)
                        {
                            mText.Attachment = edit.Alignment;
                            mText.Rotation = edit.Rotation;
                            mText.Location = edit.Point;
                        }
                    }
                    tr.Commit();
                }
            }
            catch
            {
                // 事务没有 Commit：已做的改动随 using 一起回滚。
                return null;
            }
            return new QuickLineLabelAlignResult(edits.Count, unchanged,
                withoutLabel, withoutMetadata, skipped);
        }

        private static PlannedEdit Plan(Transaction tr, ObjectId lineId,
            Entity label, double offset, bool centered, bool defaultAbove)
        {
            Point3d anchor;
            double currentRotation;
            AttachmentPoint currentAlignment;
            if (label is DBText text)
            {
                anchor = text.Justify == AttachmentPoint.BaseLeft
                    ? text.Position : text.AlignmentPoint;
                currentRotation = text.Rotation;
                currentAlignment = text.Justify;
            }
            else if (label is MText mText)
            {
                anchor = mText.Location;
                currentRotation = mText.Rotation;
                currentAlignment = mText.Attachment;
            }
            else
            {
                return null;
            }

            Curve curve = tr.GetObject(lineId, OpenMode.ForRead, false) as Curve;
            if (curve == null || curve.IsErased) return null;
            QuickLinePoint lineStart;
            QuickLinePoint lineEnd;
            try
            {
                if (curve is Line line)
                {
                    lineStart = new QuickLinePoint(line.StartPoint.X, line.StartPoint.Y);
                    lineEnd = new QuickLinePoint(line.EndPoint.X, line.EndPoint.Y);
                }
                else if (curve is Polyline polyline && !polyline.Closed)
                {
                    // 开洞后的多段线：取离毫米标最近的点及其切线方向作为「线段」。
                    Point3d closest = polyline.GetClosestPointTo(anchor, false);
                    double parameter = polyline.GetParameterAtPoint(closest);
                    Vector3d tangent = polyline.GetFirstDerivative(parameter);
                    if (tangent.Length <= 1e-9) return null;
                    lineStart = new QuickLinePoint(closest.X - tangent.X,
                        closest.Y - tangent.Y);
                    lineEnd = new QuickLinePoint(closest.X + tangent.X,
                        closest.Y + tangent.Y);
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }

            QuickLineLabelPlacement placement;
            try
            {
                placement = QuickLineLabelPlacementPlanner.Plan(lineStart, lineEnd,
                    new QuickLinePoint(anchor.X, anchor.Y), currentRotation,
                    centered ? 0.0 : offset, defaultAbove);
            }
            catch (ArgumentException)
            {
                return null;
            }
            AttachmentPoint alignment = AttachmentFor(placement, centered, defaultAbove);
            var point = new Point3d(placement.AnchorX, placement.AnchorY, anchor.Z);
            bool unchanged = anchor.DistanceTo(point) <= 1e-6
                && Math.Abs(NormalizeAngle(currentRotation - placement.Rotation)) <= 1e-9
                && currentAlignment == alignment;
            return new PlannedEdit(label, point, placement.Rotation, alignment,
                unchanged);
        }

        private static AttachmentPoint AttachmentFor(QuickLineLabelPlacement placement,
            bool centered, bool defaultAbove)
        {
            if (centered) return AttachmentPoint.MiddleCenter;
            switch (placement.Side)
            {
                case QuickLineLabelSide.Left:
                    return AttachmentPoint.BottomCenter;
                case QuickLineLabelSide.Right:
                    return AttachmentPoint.TopCenter;
                default:
                    return defaultAbove ? AttachmentPoint.BottomCenter
                        : AttachmentPoint.TopCenter;
            }
        }

        private static double NormalizeAngle(double value)
        {
            value %= Math.PI * 2;
            if (value > Math.PI) value -= Math.PI * 2;
            if (value < -Math.PI) value += Math.PI * 2;
            return value;
        }

        private sealed class PlannedEdit
        {
            internal PlannedEdit(Entity entity, Point3d point, double rotation,
                AttachmentPoint alignment, bool unchanged)
            {
                Entity = entity;
                Point = point;
                Rotation = rotation;
                Alignment = alignment;
                Unchanged = unchanged;
            }

            internal Entity Entity { get; }
            internal Point3d Point { get; }
            internal double Rotation { get; }
            internal AttachmentPoint Alignment { get; }
            internal bool Unchanged { get; }
        }
    }
}
