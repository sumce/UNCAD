using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    public sealed class TableGenerationRequest
    {
        public TableGenerationRequest(MachineRow machine, BoqCatalogIndex catalog,
            CableStatResult statistics, FillPlanningOptions options,
            FillAutoFillOptions autoFill = null)
        {
            Machine = CloneMachine(machine ?? new MachineRow());
            Catalog = catalog ?? new BoqCatalogIndex(null);
            Statistics = CloneStatistics(statistics ?? new CableStatResult());
            Options = options ?? FillPlanningOptions.Default;
            AutoFill = autoFill ?? FillAutoFillOptions.Default;
        }

        public MachineRow Machine { get; }
        public BoqCatalogIndex Catalog { get; }
        public CableStatResult Statistics { get; }
        public FillPlanningOptions Options { get; }
        public FillAutoFillOptions AutoFill { get; }

        private static CableStatResult CloneStatistics(CableStatResult source)
        {
            var clone = new CableStatResult
            {
                CableSum = source.CableSum,
                IncludeCable = source.IncludeCable,
                IncludeBridge = source.IncludeBridge,
                IncludeConduit = source.IncludeConduit,
                CableState = source.CableState,
                BridgeState = source.BridgeState,
                ConduitState = source.ConduitState
            };
            clone.CableFormatted.AddRange(source.CableFormatted);
            foreach (BridgeStat bridge in source.Bridges)
            {
                var item = new BridgeStat
                {
                    Spec = bridge.Spec,
                    CatalogModel = bridge.CatalogModel,
                    MmPerGrid = bridge.MmPerGrid
                };
                item.Grids.AddRange(bridge.Grids);
                clone.Bridges.Add(item);
            }
            foreach (ConduitStat conduit in source.Conduits)
            {
                var item = new ConduitStat { Spec = conduit.Spec };
                item.LengthsMm.AddRange(conduit.LengthsMm);
                clone.Conduits.Add(item);
            }
            return clone;
        }

        private static MachineRow CloneMachine(MachineRow source)
        {
            return new MachineRow
            {
                Region = source.Region,
                MachineId = source.MachineId,
                CircuitName = source.CircuitName,
                Cable = source.Cable,
                Fr = source.Fr,
                Detail = source.Detail,
                Seq = source.Seq,
                Dia = source.Dia,
                Next = source.Next,
                DownstreamAxis = source.DownstreamAxis,
                UpstreamAxis = source.UpstreamAxis,
                DeviceFloor = source.DeviceFloor,
                PanelFloor = source.PanelFloor,
                FacilitySwitch = source.FacilitySwitch
            };
        }
    }

    public sealed class TableGenerationOutput
    {
        private readonly List<TableFillRow> _defaultRows;

        internal TableGenerationOutput(IEnumerable<TableFillRow> rows,
            string defaultCableMeters, FillAutoFillOptions autoFill = null)
        {
            _defaultRows = (rows ?? Enumerable.Empty<TableFillRow>())
                .Select(CloneRow).ToList();
            DefaultCableMeters = defaultCableMeters ?? "";
            AutoFill = autoFill ?? FillAutoFillOptions.Default;
        }

        public IReadOnlyList<TableFillRow> DefaultRows
            => _defaultRows.Select(CloneRow).ToList().AsReadOnly();
        public string DefaultCableMeters { get; }
        public FillAutoFillOptions AutoFill { get; }
        public int RowCount => _defaultRows.Count;
        public int UnmatchedCatalogCount => _defaultRows.Count(row => !row.CatalogMatched);

        public List<TableFillRow> CopyDefaultRows()
            => _defaultRows.Select(CloneRow).ToList();

        public FillReviewData CreateReview(MachineRow machine,
            FillPlanningOptions options)
        {
            return CreateReview(machine, options, null);
        }

        public FillReviewData CreateReview(MachineRow machine,
            FillPlanningOptions options, string originalCableModel)
        {
            FillReviewData review = FillReviewData.Create(
                machine, CopyDefaultRows(), options, originalCableModel, AutoFill);
            if (review.CableMeters.Length == 0)
                review.CableMeters = DefaultCableMeters;
            return review;
        }

        private static TableFillRow CloneRow(TableFillRow row)
        {
            return new TableFillRow
            {
                Category = row.Category,
                SortOrder = row.SortOrder,
                Name = row.Name,
                Description = row.Description,
                Unit = row.Unit,
                Quantity = row.Quantity,
                Code = row.Code,
                CatalogMatched = row.CatalogMatched
            };
        }
    }

    public static class TableGenerationModule
    {
        public static readonly ModuleDescriptor Descriptor =
            new ModuleDescriptor("BOQ-TABLE", "清单表格");

        public static TableGenerationOutput Plan(TableGenerationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            List<TableFillRow> rows = TableFillPlanner.Build(request.Machine,
                request.Catalog, request.Statistics, request.Options,
                request.AutoFill);
            string cableMeters = rows.FirstOrDefault(row =>
                row.Category == TableFillCategory.Cable)?.Quantity ?? "";
            if (cableMeters.Length == 0 && request.Statistics.CableSum > 0)
                cableMeters = TextFormatter.FormatNum(request.Statistics.CableSum);
            return new TableGenerationOutput(rows, cableMeters, request.AutoFill);
        }
    }
}
