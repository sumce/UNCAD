using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace UNCAD.Cad
{
    /// <summary>
    /// 实体工厂：统一创建实体并预设当前图层（等价 LISP entmake 的 (8 . CLAYER)）。
    /// 未来扩展（样式默认值、颜色方案）集中在这里改。
    /// 单行文字用 DBText（无 MTEXT 控制码、可直接编辑、与旧 UNADD 读取兼容）；
    /// 需要多行的标注用 <see cref="MText"/>，段落以 \P 分隔。
    /// </summary>
    public static class EntityFactory
    {
        /// <summary>
        /// 多行标注的行距系数。AutoCAD 的 MTEXT 单倍行距是字高的 1.66 倍，
        /// 0.65 约为 1.08 倍字高，让型号和长度紧凑成组且不重叠。
        /// </summary>
        public const double LabelLineSpacingFactor = 0.65;

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

        /// <summary>
        /// 生成多行文字：anchor 是整个文字块的附着点（Attachment），
        /// 与 DBText 的区别是它对齐的是整块而不是单行——两行标注用 BottomCenter
        /// 附着时，anchor 就是文字块底边的中点，与单行 DBText 的 BottomCenter 同点。
        /// Width 固定为 0（不自动换行，段落只由调用方给的 \P 决定）；
        /// 行距固定为 Exactly + <see cref="LabelLineSpacingFactor"/>，见该常量说明。
        /// </summary>
        public static MText MText(CadContext ctx, string contents, Point3d anchor, double height,
            double rotation, AttachmentPoint attach, short colorIndex = 0,
            ObjectId? textStyleId = null)
        {
            var t = new MText
            {
                Contents = contents,
                TextHeight = height,
                Rotation = rotation,
                Attachment = attach,
                Location = anchor,
                Width = 0.0,
                // Exactly 才能保证系数真的生效；AtLeast 下 AutoCAD 可能按字体自身
                // 行高取更大值，行距就压不下来。
                LineSpacingStyle = LineSpacingStyle.Exactly,
                LineSpacingFactor = LabelLineSpacingFactor,
                LayerId = ctx.CurrentLayerId
            };
            if (colorIndex != 0) t.ColorIndex = colorIndex;
            if (textStyleId.HasValue) t.TextStyleId = textStyleId.Value;
            return t;
        }
    }
}
