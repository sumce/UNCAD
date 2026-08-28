using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Contracts;

namespace UNCAD.Core.Stat
{
    public sealed class SummationRequest
    {
        private readonly List<string> _lines;

        public SummationRequest(IEnumerable<string> lines, StatCalculationOptions options)
        {
            _lines = (lines ?? Enumerable.Empty<string>())
                .Select(line => line ?? "").ToList();
            StatCalculationOptions source = options ?? new StatCalculationOptions();
            Options = new StatCalculationOptions
            {
                MmPerGrid = source.MmPerGrid,
                IncludeCable = source.IncludeCable,
                IncludeBridge = source.IncludeBridge,
                IncludeConduit = source.IncludeConduit
            };
        }

        public IReadOnlyList<string> Lines => _lines;
        public StatCalculationOptions Options { get; }
    }

    public sealed class SummationOutput
    {
        internal SummationOutput(CableStatResult statistics, int sourceLineCount)
        {
            Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            SourceLineCount = sourceLineCount;
        }

        public CableStatResult Statistics { get; }
        public int SourceLineCount { get; }
        public int CableMatchCount => Statistics.CableFormatted.Count;
        public int BridgeMatchCount => Statistics.Bridges.Sum(item => item.Grids.Count);
        public int ConduitMatchCount => Statistics.Conduits.Sum(item => item.LengthsMm.Count);
        public int TotalMatchCount => CableMatchCount + BridgeMatchCount + ConduitMatchCount;
    }

    public static class SummationModule
    {
        public static readonly ModuleDescriptor Descriptor =
            new ModuleDescriptor("SUM-STAT", "求和统计");

        public static SummationOutput Execute(SummationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            CableStatResult statistics = StatCalculator.Calculate(
                request.Lines, request.Options);
            return new SummationOutput(statistics, request.Lines.Count);
        }
    }
}
