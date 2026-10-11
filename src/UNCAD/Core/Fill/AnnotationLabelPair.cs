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
        /// 还原一个 MTEXT 的两行：完整「清单型号 + 纯长度」合并成一行。
        /// 多一行说明、前缀或后缀都不是 U1Q/U1C 标注，必须原样保留。
        /// </summary>
        public static List<string> CollapseLines(IReadOnlyList<string> lines,
            AnnotationModelIndex index)
        {
            var result = lines == null ? new List<string>() : new List<string>(lines);
            if (index != null && result.Count == 2)
            {
                // 两行在 MTEXT 编辑器里被换序（长度在前）时也必须还原成"型号 + 长度"：
                // 否则长度行会漏进电缆合计，桥架/线管本身被静默丢弃（D-018）。
                if (index.TryCollapse(result[0], result[1], out string collapsed)
                    || index.TryCollapse(result[1], result[0], out collapsed))
                {
                    result.Clear();
                    result.Add(collapsed);
                }
            }
            return result;
        }

        /// <summary>
        /// 将一个 MTEXT 转为统计输入。精确的清单型号仍按 <see cref="CollapseLines"/>
        /// 还原；如果未能精确配对，但同一 MTEXT 明显包含桥架/线管标题和裸长度，
        /// 则保持为一个整体，避免长度行被误算进电缆。
        /// </summary>
        public static List<string> CollapseStatisticsLines(IReadOnlyList<string> lines)
            => CollapseStatisticsLines(lines, Embedded.Value);

        public static List<string> CollapseStatisticsLines(IReadOnlyList<string> lines,
            AnnotationModelIndex index)
        {
            List<string> result = CollapseLines(lines, index);
            if (result.Count <= 1 || !ContainsAnnotationLength(result)) return result;

            // 统计引擎只认“标题在前、长度在后”。用户在 MTEXT 编辑器里把两行换序
            // （或复制粘贴）后，合并结果必须仍能被读成桥架/线管，否则裸长度会漏进
            // 电缆合计、而桥架/线管本身被静默丢弃。
            List<string> ordered = new List<string>(result);
            int heading = ordered.FindIndex(IsBridgeOrConduitText);
            int length = ordered.FindIndex(TextParser.IsBareLengthToken);
            if (heading > 0 && length >= 0 && length < heading)
            {
                string headingLine = ordered[heading];
                ordered.RemoveAt(heading);
                ordered.Insert(0, headingLine);
            }
            return new List<string> { string.Join(" ", ordered).Trim() };
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

        private static bool IsBridgeOrConduitText(string value)
        {
            string text = value ?? "";
            return text.IndexOf("桥架", StringComparison.Ordinal) >= 0
                || text.IndexOf("线管", StringComparison.Ordinal) >= 0;
        }

        private static bool ContainsAnnotationLength(IReadOnlyList<string> lines)
        {
            // 与顺序无关：同一 MTEXT 里只要有桥架/线管标题，又有独立的裸长度，
            // 就必须当成一个整体（D-018），不能要求长度恰好排在标题之后。
            bool foundHeading = false;
            bool foundLength = false;
            foreach (string line in lines)
            {
                if (IsBridgeOrConduitText(line)) foundHeading = true;
                else if (TextParser.IsBareLengthToken(line)) foundLength = true;
            }
            return foundHeading && foundLength;
        }
    }
}
