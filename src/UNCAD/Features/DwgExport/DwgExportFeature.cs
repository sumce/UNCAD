using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Features.Submit;
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
            // 2.10.0 整机回路校验：一个机台的所有回路都必须选中，否则不生成 DWG（写盘之前）。
            if (!CircuitCoverageGuard.Ensure(ctx, CommandIds.DwgExport,
                frames.Select(frame => new KeyValuePair<string, string>(
                    frame.MachineId, frame.DeviceName))))
                return;

            DwgExportResult result;
            try
            {
                // 图纸名 → <root>\<图纸>\DWG\<机台>\<机台>.dwg；占用预检在写盘之前。
                result = DwgFrameExportService.Export(ctx, frames,
                    AutomaticSubmissionService.CurrentDrawingName(ctx));
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
            ctx.Write("\n[U1DWG] 已导出 " + result.FrameCount + " 个设备图框，"
                + result.MachineCount + " 个机台文件：" + result.FilePath);
        }
    }
}
