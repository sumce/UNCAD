using System;
using System.Collections.Generic;

namespace UNCAD.Core.Text
{
    /// <summary>
    /// 有界编辑距离判定（3.2.0.7，现场报告"MSRTL04 ↔ MPSTL04 就差一个字母"）。
    /// 图上机台号与档案机台号在精确匹配失手后，用"仅一字母之差"识别疑似笔误：
    /// 纯 Core、只读；比较基于 <see cref="IdentityTextNormalizer.Key"/>
    ///（忽略空白与大小写，与全库身份口径一致）。
    /// </summary>
    public static class LevenshteinNeighbour
    {
        /// <summary>
        /// 判断两个字符串的编辑距离是否 ≤ maxDistance。长度差先决（超过直接 false），
        /// DP 行内最小值越过上限即提前退出——短机台号（≤10 字符）上的开销可忽略。
        /// </summary>
        public static bool IsWithinDistance(string left, string right, int maxDistance)
        {
            if (maxDistance < 0) return false;
            string a = IdentityTextNormalizer.Key(left ?? "");
            string b = IdentityTextNormalizer.Key(right ?? "");
            if (a.Length == 0 && b.Length == 0) return true;
            if (Math.Abs(a.Length - b.Length) > maxDistance) return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

            int width = b.Length;
            var previous = new int[width + 1];
            var current = new int[width + 1];
            for (int j = 0; j <= width; j++) previous[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                int rowMinimum = current[0];
                for (int j = 1; j <= width; j++)
                {
                    int cost = char.ToLowerInvariant(a[i - 1]) ==
                            char.ToLowerInvariant(b[j - 1])
                        ? 0 : 1;
                    int value = Math.Min(Math.Min(current[j - 1] + 1,
                        previous[j] + 1), previous[j - 1] + cost);
                    current[j] = value;
                    if (value < rowMinimum) rowMinimum = value;
                }
                if (rowMinimum > maxDistance) return false;
                int[] swap = previous;
                previous = current;
                current = swap;
            }
            return previous[width] <= maxDistance;
        }

        /// <summary>
        /// 在候选集中找出与 value 编辑距离 ≤ maxDistance 的邻居（保序，最多返回
        /// maxResults 个）。候选集很大时由调用方先按长度分桶/前缀粗筛，这里不做。
        /// </summary>
        public static List<string> FindNeighbours(string value,
            IEnumerable<string> candidates, int maxDistance, int maxResults)
        {
            var result = new List<string>();
            if (candidates == null || maxResults <= 0) return result;
            string key = IdentityTextNormalizer.Key(value ?? "");
            if (key.Length == 0) return result;
            int lower = key.Length - maxDistance;
            int upper = key.Length + maxDistance;
            foreach (string candidate in candidates)
            {
                string candidateKey = IdentityTextNormalizer.Key(candidate ?? "");
                if (candidateKey.Length < lower || candidateKey.Length > upper) continue;
                if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
                    continue;   // 精确相等不算"近名"——那是命中，不是疑似笔误
                if (IsWithinDistance(key, candidateKey, maxDistance))
                {
                    result.Add(candidate);
                    if (result.Count >= maxResults) break;
                }
            }
            return result;
        }
    }
}
