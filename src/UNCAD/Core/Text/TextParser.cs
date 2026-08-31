using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UNCAD.Core.Text
{
    /// <summary>
    /// 文本解析（纯 C#，无 AutoCAD 依赖，可单元测试）。
    /// 统计文字的严格整行解析规则；后续规则调整集中在这里。
    /// </summary>
    public static class TextParser
    {
        // 清理 MTEXT 控制符（与 LISP UNADD-CleanMText 的 Pattern 逐字对应）：
        // \f...; 字体代码、\A0-2; 对齐代码、\[a-zA-HJ-Z0-9]+ 其他格式代码、{} 花括号
        private static readonly Regex MTextCodeRegex = new Regex(
            @"\\f[^;]+;|\\A[0-2];|\\[a-zA-HJ-Z0-9]+|[{}]",
            RegexOptions.Compiled);
        private static readonly Regex BridgeLabelRegex = new Regex(
            @"^桥架\s*([0-9]+)\s*[\*xX]\s*([0-9]+)\s+([0-9]+(?:\.[0-9]+)?)\s*格$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BridgeMillimetreLabelRegex = new Regex(
            @"^桥架\s*([0-9]+(?:\.[0-9]+)?)\s*[\*xX×]\s*([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*mm$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string CleanMText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return MTextCodeRegex.Replace(s, "");
        }

        /// <summary>按 MTEXT 换行符 \P 拆分成多行。</summary>
        public static List<string> SplitMTextLines(string s)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(s)) return lines;
            string rest = s;
            int pos;
            while ((pos = rest.IndexOf("\\P", StringComparison.Ordinal)) >= 0)
            {
                string line = rest.Substring(0, pos).Trim();
                if (line.Length > 0) lines.Add(line);
                rest = rest.Substring(pos + 2);
            }
            rest = rest.Trim();
            if (rest.Length > 0) lines.Add(rest);
            return lines;
        }

        /// <summary>
        /// 电缆长度：单行文本整行匹配——纯数字 + 结尾 00 + mm（如 1200mm / 3000mm）。
        /// 除 mm 外必须全是数字："电缆 2000mm 长度"这类带前后缀的文字不算。
        /// </summary>
        public static double? ExtractCableLength(string s)
        {
            string t = (s ?? "").Trim();
            var m = Regex.Match(t, @"^([0-9]+00)mm$", RegexOptions.IgnoreCase);
            if (m.Success && m.Groups[1].Success)
                return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return null;
        }

        /// <summary>
        /// 兼容旧桥架标注“桥架宽*高 格数格”；不允许前缀、后缀或中间备注。
        /// </summary>
        public static bool TryExtractBridgeLabel(string s, out string spec, out double grids)
        {
            Match match = BridgeLabelRegex.Match((s ?? "").Trim());
            if (!match.Success)
            {
                spec = null;
                grids = 0;
                return false;
            }
            spec = "桥架" + match.Groups[1].Value + "*" + match.Groups[2].Value;
            grids = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            return grids > 0;
        }

        /// <summary>
        /// Reads both the legacy grid label and the current millimetre label. The returned
        /// grid count is an internal compatibility value so existing statistics can keep
        /// aggregating by the configured millimetres-per-grid scale.
        /// </summary>
        public static bool TryExtractBridgeLabel(string s, double mmPerGrid,
            out string spec, out double grids)
        {
            if (TryExtractBridgeLabel(s, out spec, out grids)) return true;
            Match match = BridgeMillimetreLabelRegex.Match((s ?? "").Trim());
            if (!match.Success || double.IsNaN(mmPerGrid) || double.IsInfinity(mmPerGrid)
                || mmPerGrid <= 0
                || !double.TryParse(match.Groups[3].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double millimetres)
                || millimetres <= 0)
            {
                spec = null;
                grids = 0;
                return false;
            }
            spec = "桥架" + match.Groups[1].Value + "*" + match.Groups[2].Value;
            grids = millimetres / mmPerGrid;
            return grids > 0 && !double.IsNaN(grids) && !double.IsInfinity(grids);
        }

        public static string ExtractBridgeSpec(string s)
        {
            return TryExtractBridgeLabel(s, 250.0, out string spec, out _) ? spec : null;
        }

        /// <summary>
        /// 线管规格：完整文字以 ⌀/Ø/Φ + 直径 + 线管 开头，统一返回“⌀直径线管”。
        /// </summary>
        public static string ExtractConduitSpec(string s)
        {
            var m = Regex.Match((s ?? "").Trim(),
                @"^[⌀ØΦ]\s*([0-9]+(?:\.[0-9]+)?)\s*线管", RegexOptions.IgnoreCase);
            return m.Success ? "⌀" + m.Groups[1].Value + "线管" : null;
        }

        /// <summary>线管长度：完整格式“⌀20线管 2000mm”，返回毫米数。</summary>
        public static double? ExtractConduitLength(string s)
        {
            var m = Regex.Match((s ?? "").Trim(),
                @"^[⌀ØΦ]\s*[0-9]+(?:\.[0-9]+)?\s*线管\s*([0-9]+(?:\.[0-9]+)?)\s*mm$",
                RegexOptions.IgnoreCase);
            if (m.Success)
                return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return null;
        }
    }
}
