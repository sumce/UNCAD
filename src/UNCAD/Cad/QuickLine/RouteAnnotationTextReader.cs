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
                    result.Add(new RouteAnnotationTextEntity(id,
                        new RouteAnnotationTextStamp(id.Handle.ToString(), text,
                            new QuickLinePoint(anchor.X, anchor.Y), rotation,
                            ParallelAnnotationMetadata.TryRead(entity, out _, out _),
                            kind)));
                }
                catch
                {
                    // 一段坏文字不能挡住其余标注的收集。
                }
            }
            return result;
        }
    }
}
