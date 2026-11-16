using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Text;

namespace UNCAD.Core.Stat
{
    /// <summary>一台"档案里有、图上没有"的机台（3.2.0.7，XSTS 盲区名单）。</summary>
    public sealed class XstsBlindMachine
    {
        public string MachineId { get; set; } = "";
        public string Batch { get; set; } = "";
        public string Seq { get; set; } = "";
        /// <summary>近名提示：图上存在仅一字母之差的机台号（疑似笔误/画错）。</summary>
        public string NearNameNote { get; set; } = "";
    }

    /// <summary>
    /// XSTS 盲区与近名分析（3.2.0.7，现场驱动：清单 144 台只有 49 台进统计、
    /// 95 台连图框都没匹配上；用户只能手工 VLOOKUP 拼盲区名单）。
    ///
    /// 输入全部是字符串（档案行 / 图上机台号 / 本次选中机台号），纯逻辑可单测：
    /// ① 盲区 = 档案机台 − 全图机台（不只是本次框选——没画的机台在框选语义下
    ///    结构性不可见，这正是要补的名单）；
    /// ② 盲区近名：盲区机台与图上机台编辑距离 ≤1 时点名"疑似已上图但画错名"
    ///    （现场实例：清单 MSRTL04 ↔ 图上 MPSTL04）；
    /// ③ 图上未入档近名：图上机台在档案无记录、但与档案机台仅一字母之差时
    ///    点名"疑似笔误"（作为 issue 带出，由调用方并入异常清单）。
    /// </summary>
    public static class XstsBlindAnalyzer
    {
        private const int MaxNeighboursPerMachine = 2;
        /// <summary>近名阈值：编辑距离 ≤2。现场实例 MSRTL04↔MPSTL04 逐位是
        /// 两处替换（S↔P、R↔S），距离恰为 2——"就差一个字母"的视觉口语对应
        /// 编辑距离 1~2。机台号普遍 ≥4 字符，距离 2 不会大面积误报。</summary>
        private const int NearNameMaxDistance = 2;

        /// <summary>
        /// 计算盲区名单与图上未入档近名 issue。
        /// snapshotRows：档案全部行；onDrawingMachineIds：全图扫描到的机台号；
        /// selectedMachineIds：本次框选的机台号（用于近名 issue 的范围收窄）；
        /// issues：输出（追加）"图上 X ≈ 档案 Y"疑似笔误提示。
        /// </summary>
        public static List<XstsBlindMachine> Analyze(
            IEnumerable<MachineRow> snapshotRows,
            IEnumerable<string> onDrawingMachineIds,
            IEnumerable<string> selectedMachineIds,
            List<string> issues)
        {
            var blind = new List<XstsBlindMachine>();
            if (snapshotRows == null) return blind;
            issues = issues ?? new List<string>();

            var onDrawing = new HashSet<string>(
                (onDrawingMachineIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(IdentityTextNormalizer.Key),
                StringComparer.OrdinalIgnoreCase);

            // 档案按机台聚合一次（同机台多回路取首行批次/序号）。
            var snapshotMachines = new List<MachineRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MachineRow row in snapshotRows)
            {
                string id = (row.MachineId ?? "").Trim();
                if (id.Length == 0 || !seen.Add(IdentityTextNormalizer.Key(id)))
                    continue;
                snapshotMachines.Add(row);
            }

            var onDrawingIds = onDrawing.Select(id => id).Distinct().ToList();
            foreach (MachineRow row in snapshotMachines)
            {
                if (onDrawing.Contains(IdentityTextNormalizer.Key(row.MachineId)))
                    continue;
                var entry = new XstsBlindMachine
                {
                    MachineId = row.MachineId.Trim(),
                    Batch = row.Batch ?? "",
                    Seq = row.Seq ?? ""
                };
                List<string> neighbours = LevenshteinNeighbour.FindNeighbours(
                    entry.MachineId, onDrawingIds, NearNameMaxDistance,
                    MaxNeighboursPerMachine);
                if (neighbours.Count > 0)
                    entry.NearNameNote = "≈ 图上 " + string.Join("、", neighbours)
                        + "（仅 1~2 字母之差，请核对是否画错名）";
                blind.Add(entry);
            }
            blind.Sort((left, right) => string.CompareOrdinal(left.MachineId,
                right.MachineId));

            // 图上机台在档案无记录 + 档案近名 → 疑似笔误 issue（只对本次选中的
            // 机台报，全图未入档的非选中机台不刷屏）。
            var snapshotIds = snapshotMachines.Select(row => row.MachineId).ToList();
            var snapshotKeys = new HashSet<string>(
                snapshotIds.Select(IdentityTextNormalizer.Key),
                StringComparer.OrdinalIgnoreCase);
            foreach (string selected in (selectedMachineIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
            {
                if (snapshotKeys.Contains(IdentityTextNormalizer.Key(selected)))
                    continue;
                List<string> near = LevenshteinNeighbour.FindNeighbours(
                    selected, snapshotIds, NearNameMaxDistance,
                    MaxNeighboursPerMachine);
                if (near.Count > 0)
                    issues.Add("图上机台 " + selected.Trim()
                        + " 在档案无记录，与档案机台 " + string.Join("、", near)
                        + " 仅 1~2 字母之差，请核对是否笔误（画错名/档案缺登记）。");
            }
            return blind;
        }

        /// <summary>盲区名单的分组摘要：按机台号的字母前缀聚合计数
        ///（如 PSP×10、OHVC×10），供命令行与汇总行一眼读出结构。</summary>
        public static string GroupSummary(IReadOnlyList<XstsBlindMachine> blind)
        {
            if (blind == null || blind.Count == 0) return "";
            var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (XstsBlindMachine item in blind)
            {
                string prefix = LetterPrefix(item.MachineId);
                if (prefix.Length == 0) prefix = "(其他)";
                counts[prefix] = counts.TryGetValue(prefix, out int value) ? value + 1 : 1;
            }
            return string.Join("、", counts.Take(8)
                .Select(pair => pair.Key + "×" + pair.Value))
                + (counts.Count > 8 ? " 等" : "");
        }

        private static string LetterPrefix(string machineId)
        {
            string id = (machineId ?? "").Trim();
            int index = 0;
            while (index < id.Length && char.IsLetter(id[index])) index++;
            return id.Substring(0, index);
        }
    }
}
