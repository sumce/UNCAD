using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Text;

namespace UNCAD.Core.Stat
{
    /// <summary>桥架统计项。</summary>
    public sealed class BridgeStat
    {
        public string Spec { get; set; }
        public List<double> Grids { get; } = new List<double>();
        public double MmPerGrid { get; set; } = 250.0;

        public double TotalGrids => Grids.Sum();
        public double TotalMm => TotalGrids * MmPerGrid;
        public double TotalM => TotalMm / 1000.0;
    }

    /// <summary>按管径聚合的线管长度统计项。</summary>
    public sealed class ConduitStat
    {
        public string Spec { get; set; }
        public List<double> LengthsMm { get; } = new List<double>();
        public double TotalMm => LengthsMm.Sum();
        public double TotalM => TotalMm / 1000.0;
    }

    /// <summary>统计结果。</summary>
    public sealed class CableStatResult
    {
        public List<string> CableFormatted { get; } = new List<string>();
        public double CableSum { get; set; }
        public List<BridgeStat> Bridges { get; } = new List<BridgeStat>();
        public List<ConduitStat> Conduits { get; } = new List<ConduitStat>();
    }

    /// <summary>
    /// UNADD 统计引擎（纯 C#，可单测）：
    /// 输入清理后的文本行 → 输出汇总报表行。
    /// </summary>
    public static class StatCalculator
    {
        public static CableStatResult Calculate(IEnumerable<string> lines, double mmPerGrid)
        {
            var result = new CableStatResult();
            var bridgeMap = new Dictionary<string, BridgeStat>(StringComparer.Ordinal);
            var conduitMap = new Dictionary<string, ConduitStat>(StringComparer.Ordinal);

            foreach (var source in lines)
            {
                string s = (source ?? "").Trim();
                // 条件 A：电缆长度
                double? mm = TextParser.ExtractCableLength(s);
                if (mm.HasValue && mm.Value > 0)
                {
                    double m = mm.Value / 1000.0;
                    result.CableFormatted.Add(TextFormatter.FormatNum(m));
                    result.CableSum += m;
                    continue;
                }

                // 条件 B：桥架标注 —— "桥架"开头 + 规格 + 数字格结尾（整行匹配）
                // 如 "桥架400*100 10格" / "桥架200*100 13.5格"；其他文字一律不算
                if (s.StartsWith("桥架", StringComparison.Ordinal)
                    && s.EndsWith("格", StringComparison.Ordinal))
                {
                    double? cnt = TextParser.ExtractGridCount(s);
                    string spec = TextParser.ExtractBridgeSpec(s);
                    if (cnt.HasValue && cnt.Value > 0 && spec != null)
                    {
                        if (!bridgeMap.TryGetValue(spec, out var entry))
                        {
                            entry = new BridgeStat { Spec = spec, MmPerGrid = mmPerGrid };
                            bridgeMap[spec] = entry;
                            result.Bridges.Add(entry);
                        }
                        entry.Grids.Add(cnt.Value);
                    }
                }

                // 条件 C：线管标注，如“⌀20线管 2000mm”，按管径聚合实际长度
                string conduitSpec = TextParser.ExtractConduitSpec(s);
                double? conduitLength = TextParser.ExtractConduitLength(s);
                if (conduitSpec != null && conduitLength.HasValue && conduitLength.Value > 0)
                {
                    if (!conduitMap.TryGetValue(conduitSpec, out var conduit))
                    {
                        conduit = new ConduitStat { Spec = conduitSpec };
                        conduitMap[conduitSpec] = conduit;
                        result.Conduits.Add(conduit);
                    }
                    conduit.LengthsMm.Add(conduitLength.Value);
                }
            }
            return result;
        }

        /// <summary>由统计结果生成图纸 MTEXT 报表行。</summary>
        public static List<string> BuildReport(CableStatResult r)
        {
            var outLines = new List<string>
            {
                r.CableFormatted.Count > 0
                    ? "电缆长度: " + TextFormatter.Join(r.CableFormatted, "+") + "=" + TextFormatter.FormatNum(r.CableSum) + "M"
                    : "电缆长度: 0M"
            };

            foreach (var b in r.Bridges)
            {
                var gridStrs = b.Grids.Select(TextFormatter.FormatNum).ToList();
                outLines.Add(b.Spec + " " + TextFormatter.FormatNum(b.TotalGrids) + "格:("
                    + TextFormatter.Join(gridStrs, "+") + ")*" + TextFormatter.FormatNum(b.MmPerGrid)
                    + "=" + TextFormatter.FormatNum(b.TotalMm)
                    + "mm = " + TextFormatter.FormatNum(b.TotalM) + "M");
            }
            foreach (var c in r.Conduits)
            {
                var lengths = c.LengthsMm.Select(v => TextFormatter.FormatNum(v / 1000.0)).ToList();
                outLines.Add(c.Spec + ": " + TextFormatter.Join(lengths, "+")
                    + "=" + TextFormatter.FormatNum(c.TotalM) + "M");
            }
            return outLines;
        }

        public static List<string> BuildReport(IEnumerable<string> lines, double mmPerGrid)
            => BuildReport(Calculate(lines, mmPerGrid));
    }
}
