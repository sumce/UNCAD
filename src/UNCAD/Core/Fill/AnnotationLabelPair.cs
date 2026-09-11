using System;
using System.Collections.Generic;
using UNCAD.Core.Excel;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// U1Q/U1C 两行标注的编解码。
    ///
    /// 图纸上写两行：第一行 = 固定清单「1.名称」的完整型号，第二行 = 长度。
    /// 统计引擎只认识单行写法（桥架200*100 2500mm、⌀20线管 2000mm），
    /// 所以读取端要把两行还原成单行。
    ///
    /// 还原是严格配对的：只有第一行确实是固定清单里的桥架/线管型号、且第二行确实是
    /// 纯长度写法时才合并，且只在同一个 MTEXT 内部配对。孤立的 2000mm（DBText 或
    /// 不成立的 MTEXT 行）照旧按电缆统计——老图纸的单行标注不会漏。
    /// </summary>
    public static class AnnotationLabelPair
    {
        /// <summary>MTEXT 段落分隔符。</summary>
        public const string Separator = "\\P";

        private static readonly Lazy<AnnotationModelIndex> Embedded =
            new Lazy<AnnotationModelIndex>(
                () => AnnotationModelIndex.Build(ListItemReader.ReadEmbedded()), true);

        /// <summary>两行标注内容：型号 + 段落符 + 长度。</summary>
        public static string Build(string model, string lengthText)
            => (model ?? "").Trim() + Separator + (lengthText ?? "").Trim();

        /// <summary>
        /// 把旧桥架单行/格数标注升级为固定清单型号 + 长度的两行 MTEXT 内容。
        /// 只有升级后的文字仍能还原成同一条可统计桥架记录时才返回 true。
        /// </summary>
        public static bool TryUpgradeBridgeLabel(string text, double mmPerGrid,
            out string upgraded)
        {
            upgraded = "";
            string raw = TextParser.CleanMText(text ?? "").Trim();
            if (!BridgeLabelFormatter.TryNormalize(raw, mmPerGrid,
                    out string normalized)
                || !BridgeLabelFormatter.TryParseMillimetreLabel(normalized,
                    out string spec, out double millimetres)) return false;

            string length = BridgeLabelFormatter.FormatLengthText(millimetres);
            if (!TextParser.IsBareLengthToken(length)) return false;

            ListItem item = ListItemReader.EmbeddedCatalogIndex.FindBridge(
                BoqCatalogIndex.NormalizeBridgeSpec(spec));
            string model = BoqFeatureName.Extract(item?.Feature);
            if (model.Length == 0) return false;

            string candidate = Build(model, length);
            if (!string.Equals(CollapseText(candidate), normalized,
                    StringComparison.Ordinal)) return false;
            upgraded = candidate;
            return true;
        }

        /// <summary>用插件内嵌固定清单还原一个 MTEXT 的各行。</summary>
        public static List<string> CollapseLines(IReadOnlyList<string> lines)
            => CollapseLines(lines, Embedded.Value);

        /// <summary>
        /// 还原一个 MTEXT 的各行：相邻的「清单型号 + 纯长度」合并成一行，
        /// 其余行原样保留（顺序不变）。
        /// </summary>
        public static List<string> CollapseLines(IReadOnlyList<string> lines,
            AnnotationModelIndex index)
        {
            var result = new List<string>();
            if (lines == null) return result;
            for (int i = 0; i < lines.Count; i++)
            {
                if (index != null && i + 1 < lines.Count
                    && index.TryCollapse(lines[i], lines[i + 1], out string collapsed))
                {
                    result.Add(collapsed);
                    i++;
                    continue;
                }
                result.Add(lines[i]);
            }
            return result;
        }

        /// <summary>
        /// 把一个标注实体的文字折算成“旧版单行写法”，用于识别并删除旧命令留下的
        /// 同位置标注。单行文字原样返回；多行且无法配对时也原样返回，
        /// 因此只有真正等价的两行标注才会命中旧标注。
        /// </summary>
        public static string CollapseText(string text)
            => CollapseText(text, Embedded.Value);

        public static string CollapseText(string text, AnnotationModelIndex index)
        {
            string raw = (text ?? "").Trim();
            List<string> lines = TextParser.SplitMTextLines(raw);
            if (lines.Count <= 1) return raw;
            List<string> collapsed = CollapseLines(lines, index);
            return collapsed.Count == 1 ? collapsed[0] : raw;
        }
    }
}
