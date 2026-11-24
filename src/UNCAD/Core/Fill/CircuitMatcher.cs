using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>一条已核实的配对：台账行 ↔ 图上图框。</summary>
    public sealed class CircuitPair
    {
        internal CircuitPair(CircuitSelection expected, CircuitSelection drawn, bool byKey)
        {
            Expected = expected;
            Drawn = drawn;
            ByKey = byKey;
        }

        public CircuitSelection Expected { get; }
        public CircuitSelection Drawn { get; }
        /// <summary>
        /// true = 回路身份键精确命中；false = 机台内唯一的回路名命中（老图框明文
        /// 缺上游、或上游改挂后的过渡状态——名字唯一时按名即无歧义）。
        /// </summary>
        public bool ByKey { get; }
    }

    /// <summary>一张把同一回路又画了一遍的图框。</summary>
    public sealed class CircuitDuplicate
    {
        internal CircuitDuplicate(CircuitSelection drawn, CircuitSelection original,
            bool certain)
        {
            Drawn = drawn;
            Original = original;
            Certain = certain;
        }

        public CircuitSelection Drawn { get; }
        /// <summary>
        /// 先被认定的那张图框；同名回路组的台账行已全部核实、说不出这张是
        /// 哪一条的复制时为 null。
        /// </summary>
        public CircuitSelection Original { get; }
        /// <summary>
        /// true = 身份键相同，或机台内回路名唯一——可以确定是同一回路；
        /// false = 没有台账基准且图框未记录上游编号，只能确定“同名”。
        /// </summary>
        public bool Certain { get; }
    }

    /// <summary>
    /// 一个机台的「台账回路 ↔ 图上图框」配对结果（D-057）。XSTS、XLAYOUT、
    /// U1S/U1DWG 整机校验共用这一份口径，同名回路在三处结论一致。
    /// </summary>
    public sealed class CircuitMatchResult
    {
        internal CircuitMatchResult(string machineId, bool hasBaseline,
            IReadOnlyList<CircuitSelection> expected,
            IReadOnlyList<CircuitSelection> selected,
            List<CircuitPair> pairs, List<CircuitSelection> missing,
            List<CircuitSelection> certainMissing, int provisionalCount,
            IReadOnlyDictionary<string, int> ambiguousMissing,
            List<CircuitSelection> extra, List<CircuitDuplicate> duplicates,
            List<CircuitSelection> unresolved,
            IReadOnlyDictionary<string, int> expectedNameCounts)
        {
            MachineId = machineId ?? "";
            HasBaseline = hasBaseline;
            Expected = expected;
            Selected = selected;
            Pairs = pairs;
            Missing = missing;
            CertainMissing = certainMissing;
            ProvisionalCount = provisionalCount;
            AmbiguousMissingCounts = ambiguousMissing;
            Extra = extra;
            Duplicates = duplicates;
            Unresolved = unresolved;
            ExpectedNameCounts = expectedNameCounts;
        }

        public string MachineId { get; }
        /// <summary>本机台是否有台账基准（机台不在快照里 = 无基准）。</summary>
        public bool HasBaseline { get; }
        public IReadOnlyList<CircuitSelection> Expected { get; }
        public IReadOnlyList<CircuitSelection> Selected { get; }
        /// <summary>已核实的配对（按键优先，其次机台内唯一的回路名）。</summary>
        public IReadOnlyList<CircuitPair> Pairs { get; }
        /// <summary>没有被任何图框核实的台账回路（严格口径，含被待确认图框暂时顶替的）。</summary>
        public IReadOnlyList<CircuitSelection> Missing { get; }
        /// <summary>
        /// 逐条点得出“确实没画”的台账回路。同名回路组里只要有待确认图框，
        /// 整组都不在这里——能确定的是“缺几张”，不能确定“缺哪一条”，
        /// 那一组只在 <see cref="Unresolved"/> 文案里报数量。
        /// </summary>
        public IReadOnlyList<CircuitSelection> CertainMissing { get; }
        /// <summary>待确认图框按数量暂时顶替掉的台账行数。</summary>
        public int ProvisionalCount { get; }
        /// <summary>回路名 → 该同名组里可以确定没画的张数（没有待确认图框的名字不在表里）。</summary>
        public IReadOnlyDictionary<string, int> AmbiguousMissingCounts { get; }
        /// <summary>图上有、台账里没有这个回路名的图框。</summary>
        public IReadOnlyList<CircuitSelection> Extra { get; }
        public IReadOnlyList<CircuitDuplicate> Duplicates { get; }
        /// <summary>
        /// 机台内有同名回路、而图框没有可用的回路身份键（老图框明文缺上游，
        /// 或上游改挂后键失配）——无法判定对应哪一条，不猜。
        /// </summary>
        public IReadOnlyList<CircuitSelection> Unresolved { get; }
        /// <summary>台账内按回路名归并后的条数（同名回路 &gt; 1）。</summary>
        public IReadOnlyDictionary<string, int> ExpectedNameCounts { get; }

        /// <summary>没画的回路张数：逐条点得出的 + 同名组里按数量确定的。</summary>
        public int MissingCount => CertainMissing.Count + AmbiguousMissingCounts.Values.Sum();
        /// <summary>图上互不相同（或待确认）的回路张数——同名回路各算一条。</summary>
        public int DistinctDrawnCount => HasBaseline
            ? Pairs.Count + Unresolved.Count
            : Selected.Count - Duplicates.Count;

        /// <summary>
        /// 整机齐全：台账回路全部被核实，且没有待确认图框。多出的回路不拦
        /// （与旧口径一致）。待确认图框不算核实——放行等于用一张身份不明的
        /// 图框替另一条回路出清单。
        /// </summary>
        public bool Complete => Missing.Count == 0 && Unresolved.Count == 0;

        /// <summary>同名回路在该机台是否唯一（唯一的名字按名匹配即无歧义）。</summary>
        public bool IsUniqueName(string circuitName)
            => !ExpectedNameCounts.TryGetValue(NameKey(circuitName), out int count)
                || count <= 1;

        internal static string NameKey(string circuitName)
            => IdentityTextNormalizer.Key(circuitName ?? "");
    }

    /// <summary>
    /// 回路配对（D-057）：把一个机台的台账行与图上图框一一配对。
    ///
    /// 规则——
    /// ① 身份键（机台ID+回路名+上游编号）精确命中，一一消耗；
    /// ② 没被键配掉的图框：回路名在台账里**唯一**才允许按名配对（老图框、
    ///    上游改挂的过渡态都落在这里）；回路名有重名时**不按名猜**，记为
    ///    待确认（<see cref="CircuitMatchResult.Unresolved"/>）；
    /// ③ 键与某条已配对的回路相同 = 重复图框；回路名在台账里不存在 = 多出。
    ///
    /// 这条规则是 XSTS/XLAYOUT 不再把合法的同名回路误报成“重复”、也不把
    /// “同名双回路只画了一条”误报成“完整”的关键：3.5.0 之前按回路名
    /// Distinct，重名一律塌缩成一条。
    /// </summary>
    public static class CircuitMatcher
    {
        public static CircuitMatchResult Pair(string machineId,
            IEnumerable<CircuitSelection> expected, IEnumerable<CircuitSelection> selected,
            bool hasBaseline)
        {
            List<CircuitSelection> expectedRows = Clean(expected);
            List<CircuitSelection> drawn = Clean(selected);
            var pairs = new List<CircuitPair>();
            var missing = new List<CircuitSelection>();
            var certainMissing = new List<CircuitSelection>();
            var extra = new List<CircuitSelection>();
            var duplicates = new List<CircuitDuplicate>();
            var unresolved = new List<CircuitSelection>();
            var nameCounts = expectedRows
                .GroupBy(item => CircuitMatchResult.NameKey(item.CircuitName),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(),
                    StringComparer.OrdinalIgnoreCase);

            if (!hasBaseline)
            {
                // 没有台账基准：只能拿图框自身的身份互相比。同键 = 同一回路
                // （确定重复）；同名但都没有键 = 只能确定“同名”。
                var firstByKey = new Dictionary<string, CircuitSelection>(
                    StringComparer.OrdinalIgnoreCase);
                var firstByName = new Dictionary<string, CircuitSelection>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (CircuitSelection item in drawn)
                {
                    string nameKey = CircuitMatchResult.NameKey(item.CircuitName);
                    if (!string.IsNullOrWhiteSpace(item.CircuitKey))
                    {
                        if (firstByKey.TryGetValue(item.CircuitKey,
                                out CircuitSelection keyed))
                            duplicates.Add(new CircuitDuplicate(item, keyed, true));
                        else firstByKey[item.CircuitKey] = item;
                        continue;
                    }
                    if (firstByName.TryGetValue(nameKey, out CircuitSelection named))
                        duplicates.Add(new CircuitDuplicate(item, named, false));
                    else firstByName[nameKey] = item;
                }
                return new CircuitMatchResult(machineId, false, expectedRows, drawn,
                    pairs, missing, certainMissing, 0, new Dictionary<string, int>(),
                    extra, duplicates, unresolved, nameCounts);
            }

            // ① 身份键一一消耗。
            var drawnByKey = new Dictionary<string, Queue<CircuitSelection>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (CircuitSelection item in drawn)
            {
                if (string.IsNullOrWhiteSpace(item.CircuitKey)) continue;
                if (!drawnByKey.TryGetValue(item.CircuitKey,
                        out Queue<CircuitSelection> queue))
                {
                    queue = new Queue<CircuitSelection>();
                    drawnByKey[item.CircuitKey] = queue;
                }
                queue.Enqueue(item);
            }
            var consumed = new HashSet<CircuitSelection>();
            var drawnOfExpected = new Dictionary<CircuitSelection, CircuitSelection>();
            var unsatisfied = new List<CircuitSelection>();
            foreach (CircuitSelection row in expectedRows)
            {
                CircuitSelection hit = null;
                if (drawnByKey.TryGetValue(row.CircuitKey ?? "",
                        out Queue<CircuitSelection> queue) && queue.Count > 0)
                    hit = queue.Dequeue();
                if (hit == null)
                {
                    unsatisfied.Add(row);
                    continue;
                }
                consumed.Add(hit);
                drawnOfExpected[row] = hit;
                pairs.Add(new CircuitPair(row, hit, true));
            }

            var expectedKeys = new HashSet<string>(
                expectedRows.Select(row => row.CircuitKey ?? ""),
                StringComparer.OrdinalIgnoreCase);
            var expectedByName = expectedRows
                .GroupBy(row => CircuitMatchResult.NameKey(row.CircuitName),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(),
                    StringComparer.OrdinalIgnoreCase);
            var satisfied = new HashSet<CircuitSelection>(drawnOfExpected.Keys);
            foreach (CircuitSelection item in drawn)
            {
                if (consumed.Contains(item)) continue;
                if (!string.IsNullOrWhiteSpace(item.CircuitKey)
                    && expectedKeys.Contains(item.CircuitKey))
                {
                    // 键与一条已配对的台账行相同：同一回路的第二张图框。
                    CircuitSelection row = expectedRows.First(candidate =>
                        string.Equals(candidate.CircuitKey, item.CircuitKey,
                            StringComparison.OrdinalIgnoreCase));
                    duplicates.Add(new CircuitDuplicate(item, drawnOfExpected[row], true));
                    continue;
                }
                if (!expectedByName.TryGetValue(
                        CircuitMatchResult.NameKey(item.CircuitName),
                        out List<CircuitSelection> sameName))
                {
                    extra.Add(item);
                    continue;
                }
                CircuitSelection unique = sameName[0];
                if (sameName.Count > 1)
                {
                    // 机台内有同名回路而图框的键对不上：不按名猜。
                    if (sameName.All(satisfied.Contains))
                        // 同名的台账行已全部核实，这张是多画的一张（数量超了，
                        // 但说不出它是哪一条的复制）。
                        duplicates.Add(new CircuitDuplicate(item, null, true));
                    else unresolved.Add(item);
                    continue;
                }
                if (satisfied.Contains(unique))
                    duplicates.Add(new CircuitDuplicate(item, drawnOfExpected[unique],
                        true));
                else
                {
                    satisfied.Add(unique);
                    unsatisfied.Remove(unique);
                    drawnOfExpected[unique] = item;
                    pairs.Add(new CircuitPair(unique, item, false));
                }
            }

            missing.AddRange(unsatisfied);
            certainMissing.AddRange(unsatisfied);
            // 待确认图框按数量暂时顶替同名的未满足行：能确定“缺几张”，不能
            // 确定“缺哪条”——同名的那几行整组撤出 certainMissing，只在待确认
            // 说明里报“其中缺 N 张”，不冒充具体行。
            var ambiguousMissing = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            int provisional = 0;
            foreach (IGrouping<string, CircuitSelection> group in unresolved
                .GroupBy(item => CircuitMatchResult.NameKey(item.CircuitName),
                    StringComparer.OrdinalIgnoreCase))
            {
                List<CircuitSelection> sameName = certainMissing
                    .Where(row => CircuitMatchResult.NameKey(row.CircuitName) == group.Key)
                    .ToList();
                if (sameName.Count == 0) continue;
                int cover = Math.Min(group.Count(), sameName.Count);
                foreach (CircuitSelection row in sameName) certainMissing.Remove(row);
                ambiguousMissing[group.Key] = sameName.Count - cover;
                provisional += cover;
            }
            return new CircuitMatchResult(machineId, true, expectedRows, drawn, pairs,
                missing, certainMissing, provisional, ambiguousMissing, extra,
                duplicates, unresolved, nameCounts);
        }

        private static List<CircuitSelection> Clean(IEnumerable<CircuitSelection> source)
            => (source ?? Enumerable.Empty<CircuitSelection>())
                .Where(item => item != null
                    && !string.IsNullOrWhiteSpace(item.CircuitName))
                .ToList();
    }

    /// <summary>配对结果的用户可读文案（XSTS/XLAYOUT/整机校验共用，纯格式化）。</summary>
    public static class CircuitMatchText
    {
        /// <summary>图框没有可用的上游编号时的占位说明。</summary>
        public const string NoUpstreamNote = "未记录上游编号";

        /// <summary>回路显示名；需要区分同名回路时带上游编号后缀。</summary>
        public static string Circuit(CircuitSelection item, bool disambiguate)
        {
            if (!disambiguate) return item.CircuitName;
            string upstream = (item.UpstreamId ?? "").Trim();
            return item.CircuitName + "（上游 "
                + (upstream.Length > 0 ? upstream : "未填") + "）";
        }

        private static bool Disambiguate(CircuitMatchResult result, string circuitName)
            => result.ExpectedNameCounts.TryGetValue(
                CircuitMatchResult.NameKey(circuitName), out int count) && count > 1;

        /// <summary>已选的回路（同名回路带上游编号；无基准时按图上先后）。</summary>
        public static IReadOnlyList<string> Selected(CircuitMatchResult result)
        {
            if (!result.HasBaseline)
            {
                var repeated = new HashSet<CircuitSelection>(
                    result.Duplicates.Select(duplicate => duplicate.Drawn));
                return result.Selected
                    .Where(item => !repeated.Contains(item))
                    .Select(item => item.CircuitName).ToList();
            }
            var values = result.Pairs
                .Select(pair => Circuit(pair.Expected,
                    Disambiguate(result, pair.Expected.CircuitName)))
                .ToList();
            values.AddRange(Unresolved(result));
            return values;
        }

        /// <summary>
        /// 没画的回路：能确定的那几条逐条点名（同名带上游编号）；被待确认
        /// 图框顶替的部分合并成一条“无法判定”的说明，不冒充具体行。
        /// </summary>
        public static IReadOnlyList<string> Missing(CircuitMatchResult result)
        {
            if (!result.HasBaseline) return new List<string>();
            var values = result.CertainMissing
                .Select(row => Circuit(row, Disambiguate(result, row.CircuitName)))
                .ToList();
            values.AddRange(Unresolved(result));
            return values;
        }

        /// <summary>没画的回路张数（与 <see cref="Missing"/> 文案条数不同名时可能不等）。</summary>
        public static int MissingCount(CircuitMatchResult result)
            => result.CertainMissing.Count + result.AmbiguousMissingCounts.Values.Sum();

        public static IReadOnlyList<string> Extra(CircuitMatchResult result)
            => result.Extra.Select(item => Circuit(item,
                Disambiguate(result, item.CircuitName))).ToList();

        /// <summary>
        /// 待确认说明：一条同名回路一条。图上画了但缺的同名回路按数量
        /// 点明（“其中缺 1 张”），让用户知道该对哪张图框跑 U1F/U1U。
        /// </summary>
        public static IReadOnlyList<string> Unresolved(CircuitMatchResult result)
        {
            var values = new List<string>();
            if (!result.HasBaseline)
            {
                // 无台账基准：只能确定“同名”，说不出是重复图框还是两条回路。
                foreach (CircuitDuplicate duplicate in result.Duplicates
                    .Where(duplicate => !duplicate.Certain))
                    values.Add(duplicate.Drawn.CircuitName + "（与图框 "
                        + (duplicate.Original?.Handle ?? "—") + " 同名且"
                        + NoUpstreamNote + "，无台账基准，无法判断是否重复）");
                return values;
            }
            foreach (IGrouping<string, CircuitSelection> group in result.Unresolved
                .GroupBy(item => CircuitMatchResult.NameKey(item.CircuitName),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                CircuitSelection first = group.First();
                string name = first.CircuitName;
                int total = result.ExpectedNameCounts.TryGetValue(group.Key,
                    out int count) ? count : 0;
                int missingCount = result.AmbiguousMissingCounts.TryGetValue(group.Key,
                    out int ambiguous) ? ambiguous : 0;
                string handles = string.Join("、", group
                    .Select(item => (item.Handle ?? "").Trim())
                    .Where(handle => handle.Length > 0));
                values.Add(name + "（机台有 " + total + " 条同名回路，图框 "
                    + (handles.Length > 0 ? handles : "—") + " " + NoUpstreamNote
                    + "，无法判定对应哪一条"
                    + (missingCount > 0 ? "；其中缺 " + missingCount + " 张" : "")
                    + "）");
            }
            return values;
        }

        /// <summary>单张待确认图框的说明（异常图框页一行一图框用）。</summary>
        public static string UnresolvedFrameNote(CircuitMatchResult result,
            CircuitSelection item)
        {
            int total = result.ExpectedNameCounts.TryGetValue(
                CircuitMatchResult.NameKey(item.CircuitName), out int count) ? count : 0;
            return "回路“" + item.CircuitName + "”在本机台有 " + total
                + " 条同名回路，图框未记录可用的上游编号，无法判定对应哪一条——"
                + "请对该图框执行 U1F 或 U1U 指认";
        }

        /// <summary>重复图框的说明（异常图框页一行一图框用）。</summary>
        public static string DuplicateFrameNote(CircuitMatchResult result,
            CircuitDuplicate duplicate, string originalHandle)
        {
            if (!duplicate.Certain)
                return "回路名称与图框 " + (originalHandle ?? "") + " 相同且"
                    + NoUpstreamNote + "，无法判断是重复图框还是同名不同上游的回路";
            return (originalHandle ?? "").Length > 0
                ? "与图框 " + originalHandle + " 是同一回路（机台/回路/上游编号相同），疑似重复图框"
                : "同名回路的台账行已全部上图，本图框多出一张，疑似重复图框";
        }
    }
}
