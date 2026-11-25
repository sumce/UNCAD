using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Core.Text;

namespace UNCAD.Cad.QuickLine
{
    /// <summary>
    /// D-058：图纸里的一个桥架/线管标注文字。实体 id 留给事务里复读，文字、
    /// 位置和种类是值拷贝——事务关闭后仍可用。
    /// </summary>
    public sealed class RouteAnnotationTextEntity
    {
        public RouteAnnotationTextEntity(ObjectId entityId,
            RouteAnnotationTextStamp stamp)
        {
            EntityId = entityId;
            Stamp = stamp ?? throw new ArgumentNullException(nameof(stamp));
        }

        public ObjectId EntityId { get; }
        public RouteAnnotationTextStamp Stamp { get; }
    }

    /// <summary>
    /// D-058：收集当前空间里认得出的桥架/线管标注文字（U1U 3D 预览用，只读）。
    /// 整个空间走一遍、只开文字类实体；批量 U1U 整图只收一次，逐框按图框
    /// 边界取用（不在每框里各收一遍）。
    /// </summary>
    public static class RouteAnnotationTextReader
    {
        public static List<RouteAnnotationTextEntity> Collect(CadContext ctx,
            Transaction transaction)
        {
            var result = new List<RouteAnnotationTextEntity>();
            if (ctx == null || transaction == null) return result;
            BlockTableRecord space;
            try
            {
                space = transaction.GetObject(ctx.Db.CurrentSpaceId,
                    OpenMode.ForRead, false) as BlockTableRecord;
            }
            catch
            {
                return result;
            }
            if (space == null) return result;

            foreach (ObjectId id in space)
            {
                if (id.IsNull || !id.IsValid) continue;
                if (!QuickLineCadService.IsTextClass(id)) continue;
                Entity entity;
                try
                {
                    entity = transaction.GetObject(id, OpenMode.ForRead, false)
                        as Entity;
                }
                catch
                {
                    continue;
                }
                // 块属性里的软管/清单文字不是平行标注；只认空间里的独立文字。
                if (entity == null || entity.IsErased
                    || entity is AttributeReference) continue;
                try
                {
                    string text;
                    Point3d anchor;
                    double rotation;
                    var dbText = entity as DBText;
                    if (dbText != null)
                    {
                        text = dbText.TextString;
                        anchor = dbText.Justify == AttachmentPoint.BaseLeft
                            ? dbText.Position : dbText.AlignmentPoint;
                        rotation = dbText.Rotation;
                    }
                    else if (entity is MText mText)
                    {
                        text = TextParser.CleanMText(mText.Contents);
                        if (string.IsNullOrWhiteSpace(text)) text = mText.Text;
                        anchor = mText.Location;
                        rotation = mText.Rotation;
                    }
                    else
                    {
                        continue;
                    }
                    RouteAnnotationTextKind kind = RouteAnnotationTextPlanner
                        .Classify(text);
                    if (kind == RouteAnnotationTextKind.None) continue;
                    double height = (entity as DBText)?.Height
                        ?? (entity as MText)?.TextHeight ?? 0.0;
                    // 3.8.0.1：带出文字外框的中心和半对角线——旧图框的桥架文字
                    // 可能竖排/斜排（和红线不平行），按定位点找红线会漏；按外框
                    // 中心找才找得到。
                    QuickLinePoint anchorPoint = new QuickLinePoint(anchor.X, anchor.Y);
                    QuickLinePoint centerPoint = anchorPoint;
                    double extentRadius = 0.0;
                    try
                    {
                        Extents3d extents = entity.GeometricExtents;
                        centerPoint = new QuickLinePoint(
                            (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                            (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0);
                        double width = extents.MaxPoint.X - extents.MinPoint.X;
                        double depth = extents.MaxPoint.Y - extents.MinPoint.Y;
                        extentRadius = 0.5 * Math.Sqrt(width * width + depth * depth);
                    }
                    catch
                    {
                        // 外框读不出来就退回定位点，不影响文字本身的识别。
                    }
                    result.Add(new RouteAnnotationTextEntity(id,
                        new RouteAnnotationTextStamp(id.Handle.ToString(), text,
                            anchorPoint, rotation,
                            ParallelAnnotationMetadata.TryRead(entity, out _, out _),
                            kind, height, centerPoint, extentRadius)));
                }
                catch
                {
                    // 一段坏文字不能挡住其余标注的收集。
                }
            }
            return result;
        }

        /// <summary>
        /// D-059：读一条输出标注文字的定位点（DBText 对齐点 / MText 位置），用来判
        /// 它落在哪个图框里。不是文字或读不出来时返回 null。
        /// </summary>
        public static Point3d? TryReadTextAnchor(Transaction transaction,
            ObjectId entityId)
        {
            try
            {
                if (transaction == null || entityId.IsNull || !entityId.IsValid)
                    return null;
                if (!(transaction.GetObject(entityId, OpenMode.ForRead, true)
                    is Entity entity) || entity.IsErased) return null;
                if (entity is DBText text)
                    return text.Justify == AttachmentPoint.BaseLeft
                        ? text.Position : text.AlignmentPoint;
                return (entity as MText)?.Location;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// D-059：读一条输出的红线几何——直线=两点，多段线=各顶点（弧按弦）。
        /// 不是这两类（文字、块、代理对象）或读不出来时返回 null。
        /// </summary>
        public static IReadOnlyList<QuickLinePoint> ReadCurvePoints(
            Transaction transaction, ObjectId entityId)
        {
            try
            {
                if (transaction == null || entityId.IsNull || !entityId.IsValid)
                    return null;
                if (!(transaction.GetObject(entityId, OpenMode.ForRead, true)
                    is Entity entity) || entity.IsErased) return null;
                if (entity is Line line)
                    return new[]
                    {
                        new QuickLinePoint(line.StartPoint.X, line.StartPoint.Y),
                        new QuickLinePoint(line.EndPoint.X, line.EndPoint.Y)
                    };
                if (entity is Polyline polyline)
                {
                    var points = new List<QuickLinePoint>(polyline.NumberOfVertices);
                    for (int vertex = 0; vertex < polyline.NumberOfVertices; vertex++)
                    {
                        Point3d point = polyline.GetPoint3dAt(vertex);
                        points.Add(new QuickLinePoint(point.X, point.Y));
                    }
                    return points;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
