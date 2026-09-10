using System;
using System.Collections.Generic;
using System.Collections;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Core.Text;

namespace UNCAD.Cad
{
    /// <summary>
    /// 将统计文字来源实体（DBText/MText/对齐标注/转角标注）转换为统计输入行。
    /// 标注仅读取人工文字覆盖；空文字与 &lt;&gt; 是自动测量，几何测量值不能作为
    /// 人工米数标注参与统计。
    /// </summary>
    internal static class StatisticsTextReader
    {
        public static bool IsSupported(Entity entity)
            => entity is DBText || entity is MText
                || entity is AlignedDimension || entity is RotatedDimension;

        public static void AppendLines(Entity entity, bool includeText,
            bool includeMText, bool includeDimension, ICollection<string> lines)
        {
            if (entity == null || entity.IsErased || lines == null) return;
            if (includeText && entity is DBText text)
            {
                lines.Add(text.TextString);
                return;
            }
            if (includeMText && entity is MText mtext)
            {
                AddLines(lines, TextParser.SplitMTextLines(mtext.Contents));
                return;
            }
            if (!includeDimension) return;
            if (entity is AlignedDimension || entity is RotatedDimension)
                AppendDimensionLines((Dimension)entity, lines);
        }

        private static void AppendDimensionLines(Dimension dimension,
            ICollection<string> lines)
        {
            string overrideText;
            try { overrideText = dimension.DimensionText ?? ""; }
            catch { return; }
            // <> 与空文字都表示自动测量：按几何测量值显示，不是人工输入的标注。
            if (string.IsNullOrWhiteSpace(overrideText)
                || overrideText.IndexOf("<>", StringComparison.Ordinal) >= 0) return;
            AddLines(lines, TextParser.SplitMTextLines(overrideText));
        }

        private static void AddLines(ICollection<string> lines, IEnumerable<string> values)
        {
            foreach (string value in values) lines.Add(value);
        }
    }
}
