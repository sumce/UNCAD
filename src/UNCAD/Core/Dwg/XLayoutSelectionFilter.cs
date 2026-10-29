using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.Dwg
{
    /// <summary>
    /// XLAYOUT 排版前的机台/回路筛选表达式。
    ///
    /// 用户在筛选框输入文本，用空格、半角/全角逗号、分号、顿号或竖线分隔多个关键词，
    /// **任一关键词命中即保留**（OR 语义，与 Excel 多选筛选一致——粘一串机台ID 就能只留这些机台），
    /// 命中范围为"机台ID + 回路名称"，忽略大小写。关键词为空时命中全部。
    /// </summary>
    public static class XLayoutSelectionFilter
    {
        private static readonly char[] Separators =
        {
            ' ', '\t', '\r', '\n', ',', '，', ';', '；', '、', '|'
        };

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

        /// <summary>任一关键词命中即为真；关键词全为空时视为命中（不做筛选）。</summary>
        public static bool MatchesAny(string value, IReadOnlyList<string> terms)
        {
            if (terms == null || terms.Count == 0) return true;
            string text = value ?? "";
            bool hasTerm = false;
            for (int index = 0; index < terms.Count; index++)
            {
                string term = terms[index];
                if (string.IsNullOrWhiteSpace(term)) continue;
                hasTerm = true;
                if (text.IndexOf(term.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            // 全是空白词等于没筛选。
            return !hasTerm;
        }
    }
}
