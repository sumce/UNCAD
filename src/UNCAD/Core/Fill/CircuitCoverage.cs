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