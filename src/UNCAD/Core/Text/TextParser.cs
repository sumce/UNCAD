using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UNCAD.Core.Text
{
    /// <summary>
    /// 文本解析（纯 C#，无 AutoCAD 依赖，可单元测试）。
    /// 规则与 LISP 版逐字对应，后续如需调整规则只改这里。
    /// </summary>
    public static class TextParser
    {
        // 清理 MTEXT 控制符（与 LISP UNADD-CleanMText 的 Pattern 逐字对应）：
        // \f...; 字体代码、\A0-2; 对齐代码、\[a-zA-HJ-Z0-9]+ 其他格式代码、{} 花括号
        private static readonly Regex MTextCodeRegex = new Regex(
            @"\\f[^;]+;|\\A[0-2];|\\[a-zA-HJ-Z0-9]+|[{}]",
            RegexOptions.Compiled);

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

        /// <summary>桥架格数：数字(可带小数)+格，且必须在文字结尾（如 10格 / 13.5格）。</summary>
        public static double? ExtractGridCount(string s)
        {
            var m = Regex.Match(s ?? "", @"([0-9]+\.?[0-9]*)\s*格$", RegexOptions.IgnoreCase);
            if (m.Success && m.Groups[1].Success)
                return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return null;
        }

        /// <summary>
        /// 桥架规格："桥架"开头 + 数字*数字（如 桥架200*100 / 桥架 300 x 150）。
        /// 不以"桥架"开头（如"安装桥架…"）或没有规格尺寸的一律不算。
        /// </summary>
        public static string ExtractBridgeSpec(string s)
        {
            var m = Regex.Match(s ?? "", @"^桥架\s*([0-9]+\s*[\*xX]\s*[0-9]+)", RegexOptions.IgnoreCase);
            if (m.Success && m.Groups[1].Success)
            {
                string spec = Regex.Replace(m.Groups[1].Value, @"\s+", "")
                    .Replace("x", "*").Replace("X", "*");
                return "桥架" + spec;
            }
            return null;
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
