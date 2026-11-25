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
        internal QuickLineLabelAlignResult(int moved, int movedByPosition, int unchanged,
            int withoutLabel, int positionRejected,
            IReadOnlyList<string> skippedHandles)
        {
            Moved = moved;
            MovedByPosition = movedByPosition;
            Unchanged = unchanged;
            WithoutLabel = withoutLabel;
            PositionRejected = positionRejected;
            SkippedHandles = skippedHandles ?? Array.Empty<string>();
        }

        /// <summary>归位的毫米标数（含只靠位置匹配的）。</summary>
        public int Moved { get; }
        /// <summary>其中没有关联元数据、扫描器按位置匹配上的（旧图纸/被合并过的线）。</summary>
        public int MovedByPosition { get; }
        /// <summary>已在位（距离、角度、对齐都一样），没有改动的毫米标数。</summary>
        public int Unchanged { get; }
        /// <summary>线段没有绑定到任何毫米标。</summary>
        public int WithoutLabel { get; }
        /// <summary>毫米标只靠位置匹配、而且离线太远或角度差太多——拿不准，未动。</summary>
        public int PositionRejected { get; }
        /// <summary>没有毫米标的线段句柄。</summary>
        public IReadOnlyList<string> SkippedHandles { get; }
    }

    /// <summary>
    /// D-058/D-059（U1UL）：毫米标归位。只动「离线的距离」和「角度」：
    ///  - 距离＝U1SET 的毫米标偏移；角度＝与线段平行（保持文字原有的阅读方向）；
    ///  - 所在侧沿用毫米标现在的位置——用户把它挪到另一侧就留在那一侧；
    ///  - 沿线方向的位置也保持（投影到线上，不拉回中点）。
    /// 毫米标怎么认（D-059）：带 U1L/U1LX 关联元数据的直接认；没有元数据的按位置认
    /// ——毫米标会被用户沿线挪、换侧、角度差个几度都算正常，所以只要求它还贴在
    /// 这条线段旁（沿线范围之内、离线不超过 4 倍字高、与线段夹角 10° 以内）。
    /// 贴得太离谱的不动（拿不准），也不给它补写关联。
    /// 两遍：先对每个目标算好新位置（任一个算不出来就整体放弃，图纸不变），
    /// 再在一个事务里统一改——一次 U 整体撤销。
    /// </summary>
    internal static class QuickLineLabelAlignService
    {
        /// <summary>只靠位置匹配时允许的角度差（用户口径：差 5° 是正常的）。</summary>
        internal const double PositionMatchAngleToleranceRadians = 10.0 * Math.PI / 180.0;
        private const double PositionMatchHeightFactor = 4.0;
        private const double PositionMatchMinimumDistance = 300.0;

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
            int movedByPosition = 0;
            int unchanged = 0;
            int withoutLabel = 0;
            int positionRejected = 0;

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
                            // 只归位单行/多行文字；尺寸标注类的毫米标不动。
                            if (!(entity is DBText) && !(entity is MText)) continue;
                            string owner = QuickLineMetadataStore
                                .ReadAssociatedLineHandle(entity);
                            bool associated = string.Equals(owner, handle,
                                StringComparison.OrdinalIgnoreCase);
                            PlannedEdit edit = Plan(tr, segment.LineId, entity,
                                offset, centered, defaultAbove, !associated);
                            if (edit == null) return null;
                            if (edit.Rejected)
                            {
                                positionRejected++;
                                continue;
                            }
                            if (edit.Unchanged) unchanged++;
                            else
                            {
                                edits.Add(edit);
                                if (!associated) movedByPosition++;
                            }
                        }
                        if (!hasLabel)
                        {
                            skipped.Add(handle);
                            withoutLabel++;
                        }
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
            return new QuickLineLabelAlignResult(edits.Count, movedByPosition, unchanged,
                withoutLabel, positionRejected, skipped);
        }

        /// <summary>
        /// requireProof：这个毫米标没有指向本线段的关联，要它确实贴在这条线段旁
        /// 才认（否则返回 Rejected）。
        /// </summary>
        private static PlannedEdit Plan(Transaction tr, ObjectId lineId,
            Entity label, double offset, bool centered, bool defaultAbove,
            bool requireProof)
        {
            Point3d anchor;
            double currentRotation;
            AttachmentPoint currentAlignment;
            double height;
            if (label is DBText text)
            {
                anchor = text.Justify == AttachmentPoint.BaseLeft
                    ? text.Position : text.AlignmentPoint;
                currentRotation = text.Rotation;
                currentAlignment = text.Justify;
                height = text.Height;
            }
            else if (label is MText mText)
            {
                anchor = mText.Location;
                currentRotation = mText.Rotation;
                currentAlignment = mText.Attachment;
                height = mText.TextHeight;
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

            if (requireProof && !SitsBesideLine(anchor, currentRotation, height,
                lineStart, lineEnd))
                return PlannedEdit.Reject(label);

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
                unchanged, false);
        }

        /// <summary>
        /// 毫米标是不是贴在这条线段旁（只靠位置匹配时的认领条件）：规则在
        /// Core 的 QuickLineLabelPlacementPlanner.SitsBesideLine（纯几何、可单测）。
        /// </summary>
        internal static bool SitsBesideLine(Point3d anchor, double rotation,
            double height, QuickLinePoint lineStart, QuickLinePoint lineEnd)
            => QuickLineLabelPlacementPlanner.SitsBesideLine(anchor.X, anchor.Y,
                height, rotation, lineStart, lineEnd);

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
                AttachmentPoint alignment, bool unchanged, bool rejected)
            {
                Entity = entity;
                Point = point;
                Rotation = rotation;
                Alignment = alignment;
                Unchanged = unchanged;
                Rejected = rejected;
            }

            internal static PlannedEdit Reject(Entity entity)
                => new PlannedEdit(entity, Point3d.Origin, 0.0,
                    AttachmentPoint.BaseLeft, false, true);

            internal Entity Entity { get; }
            internal Point3d Point { get; }
            internal double Rotation { get; }
            internal AttachmentPoint Alignment { get; }
            internal bool Unchanged { get; }
            /// <summary>只靠位置匹配、而且贴得不够近——不动它。</summary>
            internal bool Rejected { get; }
        }
    }
}
