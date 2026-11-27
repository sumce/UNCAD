using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Infra;

namespace UNCAD.Features.Shared
{
    /// <summary>
    /// U1S/U1DWG 的整机回路校验入口（2.10.0）。
    /// 只读上次成功刷新的 SQLite 快照，并委托 <see cref="FillCoverageGate"/> 执行校验。
    /// </summary>
    internal static class CircuitCoverageGuard
    {
        internal static bool Ensure(CadContext ctx, string command,
            IEnumerable<KeyValuePair<string, string>> machineCircuits)
            => Ensure(ctx, command, ToSelections(machineCircuits));

        internal static bool Ensure(CadContext ctx, string command,
            IEnumerable<CircuitSelection> machineCircuits)
        {
            FillWorkbookSnapshot workbook = TryLoadSnapshot(ctx, command);
            if (workbook == null) return true;
            return FillCoverageGate.EnsureCircuitCoverage(ctx, workbook, command,
                machineCircuits);
        }

        internal static List<CircuitCoverageResult> Evaluate(CadContext ctx, string command,
            IEnumerable<KeyValuePair<string, string>> machineCircuits)
            => Evaluate(ctx, command, ToSelections(machineCircuits));

        internal static List<CircuitCoverageResult> Evaluate(CadContext ctx, string command,
            IEnumerable<CircuitSelection> machineCircuits)
        {
            FillWorkbookSnapshot workbook = TryLoadSnapshot(ctx, command);
            return FillCoverageGate.EvaluateCircuitCoverage(ctx, workbook, command,
                machineCircuits);
        }

        private static IEnumerable<CircuitSelection> ToSelections(
            IEnumerable<KeyValuePair<string, string>> machineCircuits)
            => (machineCircuits ?? new KeyValuePair<string, string>[0])
                .Select(pair => new CircuitSelection
                {
                    MachineId = pair.Key, CircuitName = pair.Value
                });

        internal static FillWorkbookSnapshot TryLoadSnapshot(CadContext ctx, string command)
        {
            if (ctx == null) return null;
            string source;
            try
            {
                source = (FillSettings.Current().MachineWorkbookPath ?? "").Trim();
            }
            catch (Exception ex)
            {
                Log.Warn(command + " 读取机台数据源配置失败（跳过整机回路校验）: " + ex.Message);
                return null;
            }
            if (source.Length == 0)
            {
                Log.Info(command + " 未配置机台数据源，跳过整机回路校验。");
                return null;
            }
            try
            {
                if (!MachineWorkbookSource.TryGetSnapshot(source,
                    out MachineWorkbookSnapshotInfo snapshot))
                {
                    Log.Info(command + " 机台数据尚未刷新到 SQLite，跳过整机回路校验。"
                        + " 源: " + source);
                    return null;
                }
                FillWorkbookSnapshot workbook = FillWorkbookSnapshot.Load(source);
                Log.Info(command + " 整机回路校验使用 SQLite 机台快照: "
                    + (snapshot?.SourceDisplay ?? source));
                return workbook;
            }
            catch (Exception ex)
            {
                Log.Warn(command + " 读取机台 SQLite 快照失败（跳过整机回路校验）: " + ex.Message);
                return null;
            }
        }
    }
}
