using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// 阶段3「整机回路校验」门禁（D-033/D-034，2.10.0）：一个机台必须把机台数据
    /// 里的所有回路都框选进来才允许生成 BOQ/DWG。U1F/U1S 缺回路硬拦截；
    /// U1U 缺回路不拦整条命令，只跳过该机的 BOQ（D-034/D-044）；U1DWG 用评估
    /// 版按机台取舍，缺回路的默认跳过、选择窗可强制导出（D-047）。
    /// 校验只读快照、不修改图纸。
    /// 从 FillFeature 拆出：单框 U1F/U1U、批量 U1U（CoverageIncompleteMachines）
    /// 与 CircuitCoverageGuard（U1S/U1DWG）必须共用同一套判定口径。
    /// </summary>
    internal static class FillCoverageGate
    {
        /// <summary>硬拦截版（U1F/U1S 用）：缺回路时列出明细并返回 false。</summary>
        internal static bool EnsureCircuitCoverage(CadContext ctx,
            FillWorkbookSnapshot workbook, string command, string machineId,
            string circuitName)
            => EnsureCircuitCoverage(ctx, workbook, command,
                new[]
                {
                    new KeyValuePair<string, string>(machineId, circuitName)
                });

        /// <summary>硬拦截版（U1F/U1S 用）：缺回路时列出明细并返回 false。</summary>
        internal static bool EnsureCircuitCoverage(CadContext ctx,
            FillWorkbookSnapshot workbook, string command,
            IEnumerable<KeyValuePair<string, string>> machineCircuits)
        {
            List<CircuitCoverageResult> failed = EvaluateCircuitCoverage(
                ctx, workbook, command, machineCircuits);
            if (failed.Count == 0) return true;
            ctx.Write("\n[" + command + "] 按现场规则：回路不全不生成 BOQ/DWG，"
                + "本次图纸与 BOQ 均未修改。请补选缺失图框后重试；若该机台不需要"
                + "绘制全部回路，可在 U1SET 关闭「整机回路校验」。");
            return false;
        }

        /// <summary>
        /// 整机回路校验（2.10.0）的评估版：只逐台列出缺失回路并返回未通过的
        /// 机台结果，不打印“中止”类文案，也不修改图纸。U1U 用它区分
        /// “缺回路 → 只跳过该机 BOQ”，U1F/U1S/U1DWG 继续走硬拦截版。
        /// </summary>
        internal static List<CircuitCoverageResult> EvaluateCircuitCoverage(
            CadContext ctx, FillWorkbookSnapshot workbook, string command,
            IEnumerable<KeyValuePair<string, string>> machineCircuits)
        {
            var failed = new List<CircuitCoverageResult>();
            if (ctx == null || workbook == null) return failed;
            if (!Settings.GetBool(ConfigKeys.FillRequireAllCircuits, true)) return failed;
            List<KeyValuePair<string, string>> pairs = (machineCircuits
                    ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToList();
            if (pairs.Count == 0) return failed;

            foreach (IGrouping<string, KeyValuePair<string, string>> group in pairs
                .GroupBy(pair => pair.Key.Trim(), IdentityTextNormalizer.Comparer))
            {
                string machineId = group.Key;
                List<string> expected;
                try
                {
                    expected = workbook.FindRows(machineId)
                        .Select(row => row.CircuitName).ToList();
                }
                catch (System.Exception ex)
                {
                    // 快照查询失败时不要误判为"回路不全"，交给后续流程给出真实错误。
                    // 3.2.0.6（审计修复）：命令行点名一行——此前只写日志，快照损坏
                    // 导致全部查询失败时整机回路校验整体静默失效，用户无感。
                    Log.Warn("整机回路校验读取机台数据失败: " + machineId + " → "
                        + ex.Message);
                    ctx.Write("\n[" + command + "] 机台 " + machineId
                        + " 的数据读取失败，本轮未做整机回路校验。");
                    continue;
                }
                if (expected.Count == 0) continue;
                CircuitCoverageResult result = CircuitCoverage.Evaluate(machineId,
                    expected, group.Select(pair => pair.Value));
                if (!result.Complete) failed.Add(result);
            }
            if (failed.Count == 0) return failed;

            foreach (CircuitCoverageResult result in failed.Take(12))
            {
                string missing = string.Join("、", result.Missing.Take(12));
                if (result.Missing.Count > 12) missing += " 等";
                ctx.Write("\n[" + command + "] 整机回路校验未通过：机台 "
                    + result.MachineId + " 共 " + result.Expected.Count
                    + " 个回路，本次只选中 " + result.Selected.Count
                    + " 个；缺少：" + missing + "。");
            }
            if (failed.Count > 12)
                ctx.Write("\n[" + command + "] 另有 " + (failed.Count - 12)
                    + " 个机台回路不全，未逐条列出。");
            Log.Info(command + " 整机回路校验未通过，机台数 " + failed.Count);
            return failed;
        }

        /// <summary>返回未通过整机回路校验的机台 ID（规范化键，供按机台过滤 BOQ）。</summary>
        internal static List<string> CoverageIncompleteMachines(CadContext ctx,
            FillWorkbookSnapshot workbook, string command,
            IEnumerable<KeyValuePair<string, string>> machineCircuits)
            => EvaluateCircuitCoverage(ctx, workbook, command, machineCircuits)
                .Select(result => result.MachineId).ToList();

        /// <summary>
        /// D-048（2026-11-04）：为缺回路的机台弹“强制生成 BOQ”选择窗。默认全部
        /// 不勾选 = 照旧跳过（只更新图纸）；用户勾选并确认的机台按已画回路强制
        /// 生成 BOQ（缺的行不在清单里，补画后用 U1S 重出完整清单）。窗口取消
        /// 等价于全部跳过。单框 U1U 与批量 U1U 共用这一个实现。
        /// 返回用户确认强制生成的机台 ID 集合（规范化键；空 = 全部跳过）。
        /// </summary>
        internal static HashSet<string> OfferForceBoq(CadContext ctx, string command,
            IReadOnlyList<CircuitCoverageResult> coverageFailed)
        {
            var forced = new HashSet<string>(IdentityTextNormalizer.Comparer);
            if (coverageFailed == null || coverageFailed.Count == 0) return forced;
            var forceWindow = new UNCAD.UI.BoqForceSelectionWindow(
                coverageFailed.Select(result => new UNCAD.UI.BoqForceMachineChoice
                {
                    MachineId = result.MachineId,
                    SelectedCircuitCount = result.Selected.Count,
                    ExpectedCircuitCount = result.Expected.Count,
                    MissingNames = result.Missing.ToList()
                }).OrderBy(choice => choice.MachineId,
                    StringComparer.OrdinalIgnoreCase).ToList());
            Autodesk.AutoCAD.ApplicationServices.Application
                .ShowModalWindow(forceWindow);
            if (!forceWindow.Confirmed) return forced;
            forced = new HashSet<string>(forceWindow.ForcedMachineIds,
                IdentityTextNormalizer.Comparer);
            foreach (CircuitCoverageResult result in coverageFailed)
            {
                if (forced.Contains(result.MachineId))
                    Log.Info("[" + command + "] 机台 " + result.MachineId
                        + " 回路不全：已按用户确认强制生成 BOQ（仅含已画回路）。");
                else
                    Log.Info("[" + command + "] 机台 " + result.MachineId
                        + " 回路不全：本次只更新图纸、不生成该机 BOQ。");
            }
            return forced;
        }
    }
}
