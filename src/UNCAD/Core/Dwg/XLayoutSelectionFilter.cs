using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UNCAD.Core.Text;

namespace UNCAD.Core.Dwg
{
    /// <summary>
    /// XLAYOUT 排版前的机台/回路筛选表达式。
    ///
    /// 用户在筛选框输入文本，用空格、半角/全角逗号、分号、顿号或竖线分隔多个关键词，
    /// **任一关键词命中即保留**（OR 语义，与 Excel 多选筛选一致——粘一串机台ID 就能只留这些机台），
    /// 命中范围为"机台ID + 回路名称"，忽略大小写。关键词为空时命中全部。
    ///
    /// 3.2.0.7 表达力升级（现场驱动：144 台台账按 PSP/OHVC/EPLC × M603~M612 分组，
    /// 旧"子串包含"写不过来）：
    /// - **区间词** <c>M603-M612</c>（也接受 <c>~</c>/全角 <c>－</c>）：两侧同为
    ///   字母前缀+数字且前缀一致时，展开为该编号区间内的任一机台号命中；
    /// - **前缀词** <c>MS*</c>：以 MS 开头即命中（不再做子串）；
    /// - **排除词** <c>!MS*</c>（也接受全角 <c>！</c>）：命中排除词的一律隐藏，
    ///   与正向词可组合（"AMHS !MS*"= AMHS 里去掉 MS 开头）。
    /// 普通词仍为子串包含，旧用法全部不变。
    /// </summary>
    public static class XLayoutSelectionFilter
    {
        private static readonly char[] Separators =
        {
            ' ', '\t', '\r', '\n', ',', '，', ';', '；', '、', '|'
        };

        /// <summary>区间词允许展开的最大编号数（防 "M1-M999999" 之类病态输入）。</summary>
        private const int MaxRangeExpansion = 500;

        /// <summary>把筛选框文本拆成关键词（去空、去首尾空白）。</summary>
        public static IReadOnlyList<string> SplitTerms(string text)
            => (text ?? "")
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => term.Length > 0)
                .ToArray();

        /// <summary>机台ID 是否命中任一关键词（用于判断机台节点是否显示）。</summary>
        public static bool MatchesMachine(string machineId, IReadOnlyList<string> terms)
            => MatchesAny(machineId, terms);

        /// <summary>回路是否命中任一关键词（机台ID 与回路名称都参与匹配）。</summary>
        public static bool MatchesCircuit(string machineId, string deviceName,
            IReadOnlyList<string> terms)
            => MatchesAny((machineId ?? "") + " " + (deviceName ?? ""), terms);

        public static bool MatchesCircuit(string machineId, string deviceName, string filterText)
            => MatchesCircuit(machineId, deviceName, SplitTerms(filterText));

        /// <summary>
        /// 任一正向关键词命中且没有排除词命中即为真；只有排除词时，未命中排除词
        /// 即显示；关键词全为空时视为命中（不做筛选）。
        /// </summary>
        public static bool MatchesAny(string value, IReadOnlyList<string> terms)
        {
            if (terms == null || terms.Count == 0) return true;
            string text = IdentityTextNormalizer.Key(value ?? "");
            bool hasPositive = false;
            bool positiveHit = false;
            for (int index = 0; index < terms.Count; index++)
            {
                string term = (terms[index] ?? "").Trim();
                if (term.Length == 0) continue;
                if (term[0] == '!' || term[0] == '！')
                {
                    string inner = term.Substring(1).Trim();
                    if (inner.Length > 0 && TermMatches(text, inner)) return false;
                    continue;
                }
                hasPositive = true;
                if (TermMatches(text, term)) positiveHit = true;
            }
            // 没有正向词（全是排除词）＝显示所有未被打中的；全是空白词等于没筛选。
            return hasPositive ? positiveHit : true;
        }

        private static bool TermMatches(string normalizedText, string term)
        {
            if (TryParseRange(term, out string prefix, out int start, out int count))
                return RangeTokenMatches(normalizedText, prefix, start, count);
            if (term.EndsWith("*", StringComparison.Ordinal))
            {
                string head = term.Substring(0, term.Length - 1).TrimEnd('*');
                // 纯 "*"（空前缀）不作为命中条件，交由其他词决定。
                return head.Length > 0 && normalizedText.StartsWith(head,
                    StringComparison.OrdinalIgnoreCase);
            }
            return normalizedText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 区间词命中：在文本里找"前缀+完整数字串"的词元（数字串后不能再跟数字
        /// ——子串时代的 "M6030" 被 "M603-M612" 误捞就是缺这道锚），数字落在区间
        /// 内即命中。前缀为空（纯编号区间 "603-612"）时扫描全部完整数字串。
        /// </summary>
        private static bool RangeTokenMatches(string text, string prefix,
            int start, int count)
        {
            int end = start + count;
            int searchFrom = 0;
            while (searchFrom <= text.Length)
            {
                int digitStart;
                if (prefix.Length == 0)
                {
                    digitStart = IndexOfDigit(text, searchFrom);
                    if (digitStart < 0) return false;
                }
                else
                {
                    int occurrence = text.IndexOf(prefix, searchFrom,
                        StringComparison.OrdinalIgnoreCase);
                    if (occurrence < 0) return false;
                    digitStart = occurrence + prefix.Length;
                }
                int digitEnd = digitStart;
                while (digitEnd < text.Length && char.IsDigit(text[digitEnd])) digitEnd++;
                if (digitEnd > digitStart
                    && int.TryParse(text.Substring(digitStart, digitEnd - digitStart),
                        NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                    && value >= start && value < end)
                    return true;
                searchFrom = digitStart + 1;
            }
            return false;
        }

        private static int IndexOfDigit(string text, int from)
        {
            for (int index = from; index < text.Length; index++)
                if (char.IsDigit(text[index])) return index;
            return -1;
        }

        /// <summary>
        /// 解析区间词：形如 <c>M603-M612</c>/<c>M603~M612</c>，两侧都必须是
        /// （可空字母前缀+数字）、前缀一致、起 ≤ 讫、跨度受 <see cref="MaxRangeExpansion"/>
        /// 限制。不满足一律返回 false（当普通子串词处理，不误伤含连字符的机台名）。
        /// </summary>
        private static bool TryParseRange(string term, out string prefix,
            out int start, out int count)
        {
            prefix = "";
            start = 0;
            count = 0;
            if (string.IsNullOrEmpty(term)) return false;
            int separator = term.LastIndexOf('-');
            if (separator < 0) separator = term.LastIndexOf('~');
            if (separator < 0) separator = term.LastIndexOf('－');
            if (separator <= 0 || separator >= term.Length - 1) return false;
            string left = term.Substring(0, separator);
            string right = term.Substring(separator + 1);
            if (!TryParseAlphaNumeric(left, out string leftPrefix, out int leftNumber)
                || !TryParseAlphaNumeric(right, out string rightPrefix, out int rightNumber))
                return false;
            if (!string.Equals(leftPrefix, rightPrefix, StringComparison.OrdinalIgnoreCase))
                return false;
            if (rightNumber < leftNumber
                || rightNumber - leftNumber + 1 > MaxRangeExpansion)
                return false;
            prefix = leftPrefix;
            start = leftNumber;
            count = rightNumber - leftNumber + 1;
            return true;
        }

        /// <summary>解析"字母前缀+结尾数字"（字母可空，数字非溢出）。</summary>
        private static bool TryParseAlphaNumeric(string token, out string prefix,
            out int number)
        {
            prefix = "";
            number = 0;
            if (string.IsNullOrEmpty(token)) return false;
            int index = 0;
            while (index < token.Length && char.IsLetter(token[index])) index++;
            prefix = token.Substring(0, index);
            string digits = token.Substring(index);
            if (digits.Length == 0 || digits.Length > 9
                || !int.TryParse(digits, NumberStyles.None,
                    CultureInfo.InvariantCulture, out number))
                return false;
            return true;
        }
    }
}
