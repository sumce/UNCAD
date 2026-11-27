using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Dwg;
using UNCAD.Core.Fill;
using UNCAD.Features.Shared;
using UNCAD.Infra;

namespace UNCAD.Features.DwgExport
{
    /// <summary>Exports selected frame contents into one horizontal DWG per machine.</summary>
    [Feature("dwg-export", "DWG 自动导出", Commands = CommandIds.DwgExportFeatureCommands,
        Description = "按机台ID分组导出图框内容，并按设备横向排列")]
    public sealed class DwgExportFeature : CommandBase
    {
        [CommandMethod(CommandIds.DwgExport, CommandFlags.UsePickSet)]
        public void ExportDwg() => Run();

        [CommandMethod(CommandIds.DwgExportShort, CommandFlags.UsePickSet)]
        public void ExportDwgShort() => Run();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.DwgExport);
            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n请框选或点选需要导出的图框（" + FrameRegionCollector.SupportedFrameDescription + "）: ",
                new TypedValue(0, "INSERT"));
            if (selected == null || selected.Length == 0)
            {
                ctx.Write("\n[U1DWG] 未选择图框，未生成文件。");
                return;
            }

            FrameRegionCollection regions = FrameRegionCollector.CollectForExport(ctx, selected);
            if (regions.Errors.Count > 0)
            {
                foreach (string error in regions.Errors)
                    ctx.Write("\n[U1DWG] 图框识别失败: " + error);
                return;
            }
            if (regions.Groups.Count == 0)
            {
                ctx.Write("\n[U1DWG] 未找到有效图框（" + FrameRegionCollector.SupportedFrameDescription + "）。");
                return;
            }

            // 事务边界只覆盖身份读取：导出本身是纯磁盘 IO（Wblock 到独立库 +
            // SaveAs + 原子替换），不能在只读事务里跑几分钟。
            List<DwgExportFrame> frames;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                frames = DwgFrameExportService.ReadFrames(ctx, transaction, regions.Groups);
                transaction.Commit();
            }
            // D-047（2026-11-04）：U1DWG 按机台评估整机回路。旧实现（2.10.0 D-033）
            // 是"任一机台缺回路 → 整条命令中止"，完整机台也被扣住不出图；现在
            // 缺回路的机台默认跳过、完整机台照常导出，弹选择窗供用户勾选强制
            // 导出。U1S 保持硬拦截不变；校验开关 UNC_FILL_REQUIRE_ALL_CIRCUITS
            // 关闭时全部直接导出。过滤必须发生在写盘之前。
            List<CircuitCoverageResult> coverageFailed = CircuitCoverageGuard.Evaluate(
                ctx, CommandIds.DwgExport,
                frames.Select(frame => new UNCAD.Core.Fill.CircuitSelection
                {
                    MachineId = frame.MachineId,
                    CircuitName = frame.DeviceName,
                    // D-056：带身份键评估，同名回路不塌缩（老图框键为空回退按名）。
                    CircuitKey = frame.CircuitKey,
                    UpstreamId = frame.UpstreamId
                }));
            if (coverageFailed.Count > 0)
            {
                HashSet<string> failedIds = new HashSet<string>(
                    coverageFailed.Select(result => result.MachineId),
                    StringComparer.OrdinalIgnoreCase);
                var choices = frames
                    .GroupBy(frame => (frame.MachineId ?? "").Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => new UNCAD.UI.DwgExportMachineChoice
                    {
                        MachineId = group.Key,
                        Complete = !failedIds.Contains(group.Key),
                        // D-057：按回路身份去重（有键用键）——同机台同名回路（上游
                        // 编号不同）是两条回路，旧口径按名字去重会把 2 条数成 1 条。
                        CircuitCount = group
                            .Select(frame => (frame.CircuitKey ?? "").Length > 0
                                ? "k:" + frame.CircuitKey
                                : "n:" + (frame.DeviceName ?? "").Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        MissingNames = coverageFailed
                            .FirstOrDefault(result => string.Equals(result.MachineId,
                                group.Key, StringComparison.OrdinalIgnoreCase))
                            ?.Missing.ToList() ?? new List<string>(),
                        Export = !failedIds.Contains(group.Key)
                    })
                    .OrderBy(choice => choice.MachineId,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var selectionWindow = new UNCAD.UI.DwgExportSelectionWindow(choices);
                UNCAD.UI.ModalWindowHost.Show(selectionWindow);
                if (!selectionWindow.Confirmed)
                {
                    ctx.Write("\n[U1DWG] 已取消，未生成文件。");
                    return;
                }
                HashSet<string> chosen = new HashSet<string>(
                    selectionWindow.ComputeSelectedMachineIds(),
                    StringComparer.OrdinalIgnoreCase);
                int beforeFrames = frames.Count;
                frames = frames.Where(frame => chosen.Contains(
                    (frame.MachineId ?? "").Trim())).ToList();
                ctx.Write("\n[U1DWG] 跳过 " + choices.Count(choice => !choice.Export)
                    + " 台缺回路机台"
                    + (beforeFrames != frames.Count
                        ? "（少导出 " + (beforeFrames - frames.Count) + " 个图框）" : "")
                    + "；强制导出的机台按图面现有回路出图。");
            }

            DwgExportResult result;
            try
            {
                // 机台 → <root>\<机台>\<机台>.dwg（D-042）；占用预检在写盘之前。
                result = DwgFrameExportService.Export(ctx, frames);
            }
            catch (System.Exception ex)
            {
                Log.Error("U1DWG 导出失败", ex);
                ctx.Write("\n[U1DWG] 导出已中止，未生成文件：\n[U1DWG]   " + ex.Message);
                return;
            }
            // 操作记录：登记机台与 DWG 输出文件（失败只写日志，不影响导出结果）。
            foreach (DwgExportMachine machine in result.Machines)
            {
                ctx.Operation?.Machine(machine.MachineId);
                foreach (string circuit in machine.Circuits)
                    ctx.Operation?.Machine(machine.MachineId, circuit);
                ctx.Operation?.Output("DWG", machine.FilePath);
            }
            // D-042 UI 信息完整性：结果窗按机台列出图框/回路/文件，命令行只留一行摘要。
            string outputRoot = Core.Submission.OutputLayout.NormalizeRoot(
                Infra.Settings.Get(Infra.ConfigKeys.SubmitFolder, ""));
            ctx.Write("\n[U1DWG] 已导出 " + result.FrameCount + " 个设备图框 / "
                + result.MachineCount + " 个机台；明细见结果窗。");
            var resultWindow = new UNCAD.UI.DwgExportResultWindow(result.Machines,
                result.FrameCount, outputRoot);
            UNCAD.UI.ModalWindowHost.Show(resultWindow);
        }
    }
}
