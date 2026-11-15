using System;
using System.Collections.Generic;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// 图框可用绘图区（3.2.0.3，D-053 修订续）：图框边界扣除框内清单表/
    /// 标题栏占位后的内部矩形——Xauto 的重排目标。3.2.0.1 只内缩一个洞径，
    /// 框内右侧的 BOQ 表/制图信息表也在目标矩形里，铺满即压表（用户现场
    /// 报告"超了"）。表块按位置归类：右列/左列（高度过半）裁横向，上/下条
    /// （宽度过半）裁纵向；裁完非正返回 null（回退旧行为）。
    /// </summary>
    public static class FrameUsableArea
    {
        /// <param name="inset">四边内缩量（至少一个洞径，给拱桥/标注留边）。</param>
        public static SketchProportionTarget Compute(
            Geometry.FrameRectangle boundary,
            IReadOnlyList<Geometry.FrameRectangle> tables,
            double inset)
        {
            if (boundary == null || double.IsNaN(inset) || double.IsInfinity(inset)
                || inset < 0)
                return null;
            double minX = boundary.MinX + inset;
            double minY = boundary.MinY + inset;
            double maxX = boundary.MaxX - inset;
            double maxY = boundary.MaxY - inset;

            double width = maxX - minX;
            double height = maxY - minY;
            if (width <= 0 || height <= 0) return null;

            foreach (Geometry.FrameRectangle table in tables ?? Array.Empty<Geometry.FrameRectangle>())
            {
                if (table == null) continue;
                // 与内缩后区域无交叠的表不影响。
                if (table.MaxX <= minX || table.MinX >= maxX
                    || table.MaxY <= minY || table.MinY >= maxY) continue;

                bool rightHalf = (table.MinX + table.MaxX) / 2d
                    >= (minX + maxX) / 2d;
                bool topHalf = (table.MinY + table.MaxY) / 2d
                    >= (minY + maxY) / 2d;
                double tableHeight = Math.Min(table.MaxY, maxY)
                    - Math.Max(table.MinY, minY);
                double tableWidth = Math.Min(table.MaxX, maxX)
                    - Math.Max(table.MinX, minX);
                if (tableHeight >= height * 0.5d)
                {
                    // 整列（右列或左列）：按整列裁横向——半高的表也按列处理，
                    // 否则只裁掉表高的条带会把可用区切成 L 形，重排没法用。
                    if (rightHalf) maxX = Math.Min(maxX, table.MinX - inset);
                    else minX = Math.Max(minX, table.MaxX + inset);
                }
                else if (tableWidth >= width * 0.5d)
                {
                    if (topHalf) maxY = Math.Min(maxY, table.MinY - inset);
                    else minY = Math.Max(minY, table.MaxY + inset);
                }
                // 四角小块（既不够高也不够宽）：不裁——裁了会把可用区削成小角。

                width = maxX - minX;
                height = maxY - minY;
                if (width <= 0 || height <= 0) return null;
            }
            return new SketchProportionTarget(
                (minX + maxX) / 2d, (minY + maxY) / 2d, width, height);
        }
    }
}
