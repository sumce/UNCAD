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
        /// D-056 键口径（D-057 改走统一配对内核 <see cref="CircuitMatcher"/>）：
        /// 期望侧来自快照行（键必有），已选侧来自图框记录（老图框键为空）。
        /// 身份键一一配对；没配掉的图框只在机台内回路名唯一时按名配对
        /// （上游改挂的过渡态照常放行）。同名回路不按名猜——图框没有可用
        /// 的键时记为待确认，整机校验不放行：放行等于拿一张身份不明的图框
        /// 替另一条回路出清单。缺失行带上游编号后缀，待确认的合并点名。
        /// </summary>
        public static CircuitCoverageResult Evaluate(string machineId,
            IEnumerable<CircuitSelection> expected, IEnumerable<CircuitSelection> selected)
        {
            CircuitMatchResult match = CircuitMatcher.Pair(machineId, expected,
                selected, hasBaseline: true);
            return new CircuitCoverageResult((machineId ?? "").Trim(),
                match.Expected.Select(item => item.CircuitName).ToList(),
                match.Selected.Select(item => item.CircuitName).ToList(),
                CircuitMatchText.Missing(match).ToList());
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