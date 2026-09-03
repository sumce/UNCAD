using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Text;

namespace UNCAD.Core.Stat
{
    public enum MeasurementState
    {
        Unknown = 0,
        Measured = 1,
        ConfirmedEmpty = 2
    }

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
        public bool IncludeCable { get; internal set; } = true;
        public bool IncludeBridge { get; internal set; } = true;
        public bool IncludeConduit { get; internal set; } = true;
        public MeasurementState CableState { get; internal set; }
        public MeasurementState BridgeState { get; internal set; }
        public MeasurementState ConduitState { get; internal set; }

        internal void ApplySourceCoverage(bool completeScope)
        {
            CableState = ResolveState(IncludeCable, CableFormatted.Count > 0,
                completeScope);
            BridgeState = ResolveState(IncludeBridge, Bridges.Count > 0,
                completeScope);
            ConduitState = ResolveState(IncludeConduit, Conduits.Count > 0,
                completeScope);
        }

        private static MeasurementState ResolveState(bool enabled, bool measured,
            bool completeScope)
        {
            if (!enabled) return MeasurementState.Unknown;
            if (measured) return MeasurementState.Measured;
            return completeScope ? MeasurementState.ConfirmedEmpty : MeasurementState.Unknown;
        }
    }

    public sealed class StatCalculationOptions
    {
        public double MmPerGrid { get; set; } = 250.0;
        public bool IncludeCable { get; set; } = true;
        public bool IncludeBridge { get; set; } = true;
        public bool IncludeConduit { get; set; } = true;
    }

    /// <summary>
    /// UNADD 统计引擎（纯 C#，可单测）：
    /// 输入清理后的文本行 → 输出汇总报表行。
    /// </summary>
    public static class StatCalculator
    {
        public static CableStatResult Calculate(IEnumerable<string> lines, double mmPerGrid)
            => Calculate(lines, new StatCalculationOptions { MmPerGrid = mmPerGrid });

        public static CableStatResult Calculate(IEnumerable<string> lines,
            StatCalculationOptions options)
        {
            options = options ?? new StatCalculationOptions();
            double mmPerGrid = options.MmPerGrid > 0 ? options.MmPerGrid : 250.0;
            var result = new CableStatResult
            {
                IncludeCable = options.IncludeCable,
                IncludeBridge = options.IncludeBridge,
                IncludeConduit = options.IncludeConduit
            };
            var bridgeMap = new Dictionary<string, BridgeStat>(StringComparer.Ordinal);
            var conduitMap = new Dictionary<string, ConduitStat>(StringComparer.Ordinal);

            foreach (var source in lines ?? Enumerable.Empty<string>())
            {
                string s = (source ?? "").Trim();
                // 条件 A：电缆长度
                double? mm = options.IncludeCable
                    ? TextParser.ExtractCableLength(s) : null;
                if (mm.HasValue && mm.Value > 0)
                {
                    double m = mm.Value / 1000.0;
                    result.CableFormatted.Add(TextFormatter.FormatNum(m));
                    result.CableSum += m;
                    continue;
                }

                // 条件 B：桥架标注必须整行命中，不允许“共用”等前后缀或中间备注。
                if (options.IncludeBridge
                    && TextParser.TryExtractBridgeLabel(s, mmPerGrid,
                        out string spec, out double grids))
                {
                    if (!bridgeMap.TryGetValue(spec, out var entry))
                    {
                        entry = new BridgeStat { Spec = spec, MmPerGrid = mmPerGrid };
                        bridgeMap[spec] = entry;
                        result.Bridges.Add(entry);
                    }
                    entry.Grids.Add(grids);
                }

                // 条件 C：线管标注，如“⌀20线管 2000mm”，按管径聚合实际长度
                string conduitSpec = options.IncludeConduit
                    ? TextParser.ExtractConduitSpec(s) : null;
                double? conduitLength = options.IncludeConduit
                    ? TextParser.ExtractConduitLength(s) : null;
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
            result.ApplySourceCoverage(false);
            return result;
        }

        /// <summary>由统计结果生成图纸 MTEXT 报表行。</summary>
        public static List<string> BuildReport(CableStatResult r)
        {
            if (r == null) return new List<string>();
            var outLines = new List<string>();
            if (r.IncludeCable)
            {
                outLines.Add(r.CableFormatted.Count > 0
                    ? "电缆长度: " + TextFormatter.Join(r.CableFormatted, "+") + "=" + TextFormatter.FormatNum(r.CableSum) + "M"
                    : "电缆长度: 0M");
            }

            if (r.IncludeBridge) foreach (var b in r.Bridges)
            {
                var millimetreParts = b.Grids.Select(grid =>
                    TextFormatter.FormatNum(grid * b.MmPerGrid)).ToList();
                outLines.Add(b.Spec + " " + TextFormatter.FormatNum(b.TotalMm) + "mm:("
                    + TextFormatter.Join(millimetreParts, "+") + ")="
                    + TextFormatter.FormatNum(b.TotalMm)
                    + "mm = " + TextFormatter.FormatNum(b.TotalM) + "M");
            }
            if (r.IncludeConduit) foreach (var c in r.Conduits)
            {
                var lengths = c.LengthsMm.Select(v => TextFormatter.FormatNum(v / 1000.0)).ToList();
                outLines.Add(c.Spec + ": " + TextFormatter.Join(lengths, "+")
                    + "=" + TextFormatter.FormatNum(c.TotalM) + "M");
            }
            return outLines;
        }

        public static List<string> BuildReport(IEnumerable<string> lines, double mmPerGrid)
            => BuildReport(Calculate(lines, mmPerGrid));

        public static List<string> BuildReport(IEnumerable<string> lines,
            StatCalculationOptions options)
            => BuildReport(Calculate(lines, options));
    }
}
