using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
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
            DwgExportResult result = DwgFrameExportService.Export(ctx, frames);
            ctx.Write("\n[U1DWG] 已导出 " + result.FrameCount + " 个设备图框，"
                + result.MachineCount + " 个机台文件：" + result.FilePath);
            UploadExport(result);
            if (result.MachineCount > 0)
                ctx.Write("\n[U1DWG] 已排队上传 " + result.MachineCount
                    + " 个机台的 DWG 与回路登记（后台执行，失败不影响图纸）。");
        }

        /// <summary>
        /// 导出成功后把数据交给后台线程上传：先登记"该机台覆盖了哪些回路"，
        /// 再上传该机台的 DWG 本体。**绝不能阻塞 CAD 主线程，也绝不能因上传失败回滚图纸**：
        /// 失败只在日志与命令行提示（由 Bootstrap 的 Idle 循环输出），可重新执行命令重试。
        /// </summary>
        private static void UploadExport(DwgExportResult result)
        {
            List<DwgExportMachine> machines = result?.Machines;
            if (machines == null || machines.Count == 0) return;
            var captured = new List<DwgExportMachine>(machines);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (DwgExportMachine machine in captured)
                {
                    try
                    {
                        ClientSubmissionUploader.Default.ReportDwgExport(machine.MachineId,
                            machine.Circuits);
                        ClientSubmissionUploader.Default.UploadDwgFile(machine.MachineId,
                            machine.FilePath, machine.Circuits);
                    }
                    catch (System.Exception ex)
                    {
                        // 上传链路的任何异常都不得影响已完成的导出结果。
                        Log.Warn("U1DWG 上传异常（已忽略）: " + ex.Message);
                    }
                }
            });
        }
    }
}
