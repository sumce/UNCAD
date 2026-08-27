using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace UNCAD.Cad
{
    /// <summary>
    /// 实体工厂：统一创建实体并预设当前图层（等价 LISP entmake 的 (8 . CLAYER)）。
    /// 未来扩展（样式默认值、颜色方案）集中在这里改。
    /// 所有生成的文字均为单行文字（DBText）：无 MTEXT 控制码、可直接编辑、
    /// 与旧 UNADD 读取兼容。
    /// </summary>
    public static class EntityFactory
    {
        public static Line Line(CadContext ctx, Point3d p1, Point3d p2, short colorIndex = 0)
        {
            var l = new Line(p1, p2) { LayerId = ctx.CurrentLayerId };
            if (colorIndex != 0) l.ColorIndex = colorIndex;
            return l;
        }

        /// <summary>
        /// 生成单行文字：anchor 为对齐锚点（对齐方式由 justify 指定，AutoCAD 2022 的
        /// DBText 用 Justify/AlignmentPoint，与 MText 共用 AttachmentPoint 枚举）。
        /// 居中/贴线等位置由调用方算好后传入。
        /// </summary>
        public static DBText DBText(CadContext ctx, string contents, Point3d anchor, double height,
            double rotation, AttachmentPoint justify, short colorIndex = 0, ObjectId? textStyleId = null)
        {
            var t = new DBText
            {
                TextString = contents,
                Height = height,
                Rotation = rotation,
                LayerId = ctx.CurrentLayerId
            };
            if (colorIndex != 0) t.ColorIndex = colorIndex;
            if (textStyleId.HasValue) t.TextStyleId = textStyleId.Value;

            if (justify == AttachmentPoint.BaseLeft)
            {
                t.Position = anchor;
            }
            else
            {
                t.Justify = justify;
                t.AlignmentPoint = anchor;
            }
            return t;
        }
    }
}
