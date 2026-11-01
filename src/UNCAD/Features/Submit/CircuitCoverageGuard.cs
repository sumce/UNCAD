using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Features.Fill;
using UNCAD.Infra;

namespace UNCAD.Features.Submit
{
    /// <summary>
    /// U1S/U1DWG 的整机回路校验入口（2.10.0）。
    ///
    /// 这两个命令不像 U1F/U1U 那样先加载机台工作簿，但生成 BOQ/DWG 前同样必须保证
    /// "一个机台的所有回路都在本次选集中"：这里只读**上次成功刷新的 SQLite 快照**
    /// （不读源 Excel、不弹选择框、不联网），再把机台/回路对交给
    /// <see cref="FillFeature.EnsureCircuitCoverage(CadContext, FillWorkbookSnapshot, string, IEnumerable{KeyValuePair{string, string}})"/>，
    /// 与 U1F/U1U 共用同一套开关（<see cref="ConfigKeys.FillRequireAllCircuits"/>）与中文提示。
    ///
    /// 没有配置机台数据源 / 尚无快照时跳过校验并记日志：U1S/U1DWG 的历史契约是
    /// "不依赖机台工作簿也能导出"，而"回路是否齐全"在无快照时无从判断。
    /// </summary>
    internal static class CircuitCoverageGuard
    {
        /// <summary>校验整机回路覆盖；返回 false 时调用方必须中止且不写任何文件。</summary>
        internal static bool Ensure(CadContext ctx, string command,
            IEnumerable<KeyValuePair<string, string>> machineCircuits)
        {
            FillWorkbookSnapshot workbook = TryLoadSnapshot(ctx, command);
            if (workbook == null) return true;
            return FillFeature.EnsureCircuitCoverage(ctx, workbook, command, machineCircuits);
        }

        /// <summary>只读上次成功刷新的 SQLite 快照；不可用时返回 null（跳过校验）。</summary>
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
                Log.Warn(command + " 整机回路校验读取机台快照失败（已跳过校验）: "
                    + ex.Message);
                ctx.Write("\n[" + command + "] 读取机台快照失败，已跳过整机回路校验："
                    + ex.Message);
                return null;
            }
        }
    }
}
