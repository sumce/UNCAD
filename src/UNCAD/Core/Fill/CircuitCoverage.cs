using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>一个机台的回路覆盖检查结果（显示用原始写法，比较忽略空白/大小写）。</summary>
    public sealed class CircuitCoverageResult
    {
        internal CircuitCoverageResult(string machineId, IReadOnlyList<string> expected,
            IReadOnlyList<string> selected, IReadOnlyList<string> missing)
        {
            MachineId = machineId ?? "";
            Expected = expected ?? Array.Empty<string>();
            Selected = selected ?? Array.Empty<string>();
            Missing = missing ?? Array.Empty<string>();
        }

        public string MachineId { get; }
        public IReadOnlyList<string> Expected { get; }
        public IReadOnlyList<string> Selected { get; }
        public IReadOnlyList<string> Missing { get; }
        public bool Complete => Missing.Count == 0;
    }

    /// <summary>
    /// 整机回路完整性校验（2.10.0）：生成 BOQ/DWG 前要求一个机台在机台数据里的
    /// **所有回路**都出现在本次选集中，避免只框选部分回路就输出不完整的清单/图纸。
    /// 比较使用 <see cref="IdentityTextNormalizer"/>（忽略 Unicode 空白与大小写），
    /// 结果里保留原始写法供用户核对。
    /// D-056：带 <see cref="CircuitSelection.CircuitKey"/> 的重载优先按回路身份键
    /// 一一配对（同机台同名回路不再塌缩），无键侧回退按名匹配；名称版重载保持
    /// 旧语义供老调用与测试使用。
    /// </summary>
    public static class CircuitCoverage
    {
        public static CircuitCoverageResult Evaluate(string machineId,
            IEnumerable<string> expectedCircuits, IEnumerable<string> selectedCircuits)
        {
            IReadOnlyList<string> expected = DistinctInOrder(expectedCircuits);
            IReadOnlyList<string> selected = DistinctInOrder(selectedCircuits);
            var selectedKeys = new HashSet<string>(
                selected.Select(IdentityTextNormalizer.Key),
                StringComparer.OrdinalIgnoreCase);
            var missing = expected
                .Where(circuit => !selectedKeys.Contains(IdentityTextNormalizer.Key(circuit)))
                .ToList();
            return new CircuitCoverageResult((machineId ?? "").Trim(), expected,
                selected, missing);
        }

        /// <summary>
        /// D-056 键口径：期望侧来自快照行（键必有），已选侧来自图框记录
        /// （老图框键为空）。两段式配对——先按键一一消耗，再按名消耗剩余；
        /// 一个已选条目只能满足一条期望，同名双回路只画一条时必报缺。
        /// </summary>
        public static CircuitCoverageResult Evaluate(string machineId,
            IEnumerable<CircuitSelection> expected, IEnumerable<CircuitSelection> selected)
        {
            List<CircuitSelection> expectedList = (expected
                ?? Enumerable.Empty<CircuitSelection>())
                .Where(item => item != null
                    && !string.IsNullOrWhiteSpace(item.CircuitName))
                .ToList();
            List<CircuitSelection> selectedList = (selected
                ?? Enumerable.Empty<CircuitSelection>())
                .Where(item => item != null
                    && !string.IsNullOrWhiteSpace(item.CircuitName))
                .ToList();

            var remaining = new List<CircuitSelection>(selectedList);
            var satisfied = new bool[expectedList.Count];
            // 第一段：键匹配（期望键恒非空；已选键为空的老图框进不了这一段）。
            for (int index = 0; index < expectedList.Count; index++)
            {
                CircuitSelection expect = expectedList[index];
                CircuitSelection hit = remaining.FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item.CircuitKey)
                    && string.Equals(item.CircuitKey, expect.CircuitKey,
                        StringComparison.OrdinalIgnoreCase));
                if (hit == null) continue;
                remaining.Remove(hit);
                satisfied[index] = true;
            }
            // 第二段：尚未满足的期望按名匹配剩余已选（一一消耗）。
            for (int index = 0; index < expectedList.Count; index++)
            {
                if (satisfied[index]) continue;
                CircuitSelection expect = expectedList[index];
                CircuitSelection hit = remaining.FirstOrDefault(item =>
                    IdentityTextNormalizer.Equals(item.CircuitName, expect.CircuitName));
                if (hit == null) continue;
                remaining.Remove(hit);
                satisfied[index] = true;
            }
            var missingItems = expectedList
                .Where((item, index) => !satisfied[index]).ToList();

            // 同名回路的缺失行加“（上游 …）”后缀，用户能分清缺的是哪一条。
            var duplicateNames = new HashSet<string>(
                expectedList.GroupBy(item => IdentityTextNormalizer.Key(item.CircuitName),
                        StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
                StringComparer.OrdinalIgnoreCase);
            string Display(CircuitSelection item)
            {
                if (!duplicateNames.Contains(IdentityTextNormalizer.Key(item.CircuitName)))
                    return item.CircuitName;
                string upstream = (item.UpstreamId ?? "").Trim();
                return item.CircuitName + "（上游 "
                    + (upstream.Length > 0 ? upstream : "无编号") + "）";
            }

            return new CircuitCoverageResult((machineId ?? "").Trim(),
                expectedList.Select(item => item.CircuitName).ToList(),
                selectedList.Select(item => item.CircuitName).ToList(),
                missingItems.Select(Display).ToList());
        }

        private static IReadOnlyList<string> DistinctInOrder(IEnumerable<string> values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (string value in values ?? Enumerable.Empty<string>())
            {
                string text = (value ?? "").Trim();
                if (text.Length == 0 || !seen.Add(IdentityTextNormalizer.Key(text)))
                    continue;
                list.Add(text);
            }
            return list;
        }
    }
}