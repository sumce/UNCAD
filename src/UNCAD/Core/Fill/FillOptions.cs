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

    /// <summary>
    /// BOQ 自动填充类别开关。U1F/U1U 规划清单行时跳过关闭的类别，
    /// 用户仍可在审阅窗口手动加入固定清单项目；所有最终行继续受 D-016 校验。
    /// </summary>
    public sealed class FillAutoFillOptions
    {
        private FillAutoFillOptions(bool cable, bool breaker, bool flexibleConduit,
            bool rigidConduit, bool bridge, bool outletPanel, bool busPlugBox)
        {
            Cable = cable;
            Breaker = breaker;
            FlexibleConduit = flexibleConduit;
            RigidConduit = rigidConduit;
            Bridge = bridge;
            OutletPanel = outletPanel;
            BusPlugBox = busPlugBox;
        }

        /// <summary>默认：插座盘与电盘关闭，其余开启。</summary>
        public static FillAutoFillOptions Default { get; } =
            new FillAutoFillOptions(true, false, true, true, true, false, true);

        public bool Cable { get; }
        public bool Breaker { get; }
        public bool FlexibleConduit { get; }
        public bool RigidConduit { get; }
        public bool Bridge { get; }
        public bool OutletPanel { get; }
        public bool BusPlugBox { get; }

        public static FillAutoFillOptions Create(bool cable, bool breaker,
            bool flexibleConduit, bool rigidConduit, bool bridge,
            bool outletPanel, bool busPlugBox)
        {
            return new FillAutoFillOptions(cable, breaker, flexibleConduit,
                rigidConduit, bridge, outletPanel, busPlugBox);
        }
    }

    public sealed class FillRuntimeOptions
    {
        public const int DefaultStartRow = 1;
        public const int MaximumStartRow = 1000;
        public const int MaximumClearRows = 100;
        public const double MaximumTextHeight = 100000.0;
        public const double MaximumMmPerGrid = 100000.0;

        private FillRuntimeOptions(string machineWorkbookPath,
            int startRow, int clearRows,
            double textHeight, double mmPerGrid, string bridgeInfo,
            FillPlanningOptions planning, FillAutoFillOptions autoFill)
        {
            MachineWorkbookPath = machineWorkbookPath;
            StartRow = startRow;
            ClearRows = clearRows;
            TextHeight = textHeight;
            MmPerGrid = mmPerGrid;
            BridgeInfo = bridgeInfo;
            Planning = planning;
            AutoFill = autoFill;
        }

        public string MachineWorkbookPath { get; }
        public int StartRow { get; }
        public int ClearRows { get; }
        public double TextHeight { get; }
        public double MmPerGrid { get; }
        public string BridgeInfo { get; }
        public FillPlanningOptions Planning { get; }
        public FillAutoFillOptions AutoFill { get; }

        public static FillRuntimeOptions Create(string machineWorkbookPath,
            int startRow, int clearRows, double textHeight, double mmPerGrid,
            string bridgeInfo, FillPlanningOptions planning,
            FillAutoFillOptions autoFill = null)
        {
            return new FillRuntimeOptions(
                (machineWorkbookPath ?? "").Trim(),
                Clamp(startRow, 1, MaximumStartRow, DefaultStartRow),
                Clamp(clearRows, 1, MaximumClearRows, TableClearPolicy.DefaultRows),
                PositiveWithin(textHeight, MaximumTextHeight,
                    TableFillFormatter.DefaultTextHeight),
                PositiveWithin(mmPerGrid, MaximumMmPerGrid, 250.0),
                (bridgeInfo ?? "").Trim(),
                planning ?? FillPlanningOptions.Default,
                autoFill ?? FillAutoFillOptions.Default);
        }

        private static int Clamp(int value, int minimum, int maximum, int fallback)
            => value >= minimum && value <= maximum ? value : fallback;

        private static double PositiveWithin(double value, double maximum, double fallback)
            => value > 0 && value <= maximum && !double.IsNaN(value)
                && !double.IsInfinity(value) ? value : fallback;
    }
}
