using System;
using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    public sealed class FillPlanningOptions
    {
        public const double DefaultFlexibleConduitMeters = 1.5;
        public const double MaximumFlexibleConduitMeters = 100.0;

        private FillPlanningOptions(double flexibleConduitMeters,
            bool includeUnmatchedConduitsByDefault)
        {
            FlexibleConduitMeters = flexibleConduitMeters;
            // Parameter retained for source compatibility; unmatched output is forbidden.
            IncludeUnmatchedConduitsByDefault = false;
        }

        public double FlexibleConduitMeters { get; }
        public bool IncludeUnmatchedConduitsByDefault { get; }

        public static FillPlanningOptions Default { get; } =
            new FillPlanningOptions(DefaultFlexibleConduitMeters, false);

        public static FillPlanningOptions Create(double flexibleConduitMeters,
            bool includeUnmatchedConduitsByDefault)
        {
            double meters = IsFinitePositive(flexibleConduitMeters)
                && flexibleConduitMeters <= MaximumFlexibleConduitMeters
                ? flexibleConduitMeters
                : DefaultFlexibleConduitMeters;
            return new FillPlanningOptions(meters, includeUnmatchedConduitsByDefault);
        }

        private static bool IsFinitePositive(double value)
            => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class FillRuntimeOptions
    {
        public const int DefaultStartRow = 1;
        public const int MaximumStartRow = 1000;
        public const int MaximumClearRows = 100;
        public const double MaximumTextHeight = 100000.0;
        public const double MaximumMmPerGrid = 100000.0;

        private FillRuntimeOptions(string machineWorkbookPath, MachineWorkbookLayout machineWorkbookLayout,
            int startRow, int clearRows,
            double textHeight, double mmPerGrid, string bridgeInfo,
            FillPlanningOptions planning)
        {
            MachineWorkbookPath = machineWorkbookPath;
            MachineWorkbookLayout = machineWorkbookLayout;
            StartRow = startRow;
            ClearRows = clearRows;
            TextHeight = textHeight;
            MmPerGrid = mmPerGrid;
            BridgeInfo = bridgeInfo;
            Planning = planning;
        }

        public string MachineWorkbookPath { get; }
        /// <summary>Retained for source compatibility; the reader always uses U_ schema.</summary>
        [Obsolete("机台表已统一使用 U_ 字段。")]
        public MachineWorkbookLayout MachineWorkbookLayout { get; }
        public int StartRow { get; }
        public int ClearRows { get; }
        public double TextHeight { get; }
        public double MmPerGrid { get; }
        public string BridgeInfo { get; }
        public FillPlanningOptions Planning { get; }

        public static FillRuntimeOptions Create(string machineWorkbookPath,
            int startRow, int clearRows, double textHeight, double mmPerGrid,
            string bridgeInfo, FillPlanningOptions planning)
            => Create(machineWorkbookPath, MachineWorkbookLayout.A1, startRow, clearRows,
                textHeight, mmPerGrid, bridgeInfo, planning);

        public static FillRuntimeOptions Create(string machineWorkbookPath,
            MachineWorkbookLayout machineWorkbookLayout, int startRow, int clearRows,
            double textHeight, double mmPerGrid,
            string bridgeInfo, FillPlanningOptions planning)
        {
            return new FillRuntimeOptions(
                (machineWorkbookPath ?? "").Trim(),
                machineWorkbookLayout,
                Clamp(startRow, 1, MaximumStartRow, DefaultStartRow),
                Clamp(clearRows, 1, MaximumClearRows, TableClearPolicy.DefaultRows),
                PositiveWithin(textHeight, MaximumTextHeight,
                    TableFillFormatter.DefaultTextHeight),
                PositiveWithin(mmPerGrid, MaximumMmPerGrid, 250.0),
                (bridgeInfo ?? "").Trim(),
                planning ?? FillPlanningOptions.Default);
        }

        private static int Clamp(int value, int minimum, int maximum, int fallback)
            => value >= minimum && value <= maximum ? value : fallback;

        private static double PositiveWithin(double value, double maximum, double fallback)
            => value > 0 && value <= maximum && !double.IsNaN(value)
                && !double.IsInfinity(value) ? value : fallback;
    }
}
