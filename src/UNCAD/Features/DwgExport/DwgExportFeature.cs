using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Infra;

namespace UNCAD.Features.DwgExport
{
    /// <summary>Exports selected frame contents into one horizontal DWG per machine.</summary>
    [Feature("dwg-export", "DWG 自动导出", Commands = CommandIds.DwgExport,
        Description = "按机台ID分组导出图框内容，并按设备横向排列")]
    public sealed class DwgExportFeature : CommandBase
    {
        [CommandMethod(CommandIds.DwgExport, CommandFlags.UsePickSet)]
        public void ExportDwg() => Run();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.DwgExport);
            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n请框选或点选需要导出的 frame_20260812 图框: ",
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
                ctx.Write("\n[U1DWG] 未找到有效的 frame_20260812 图框。");
                return;
            }

            DwgExportResult result = DwgFrameExportService.Export(ctx, regions.Groups);
            ctx.Write("\n[U1DWG] 已导出 " + result.FrameCount + " 个设备图框，"
                + result.MachineCount + " 个机台文件：" + result.FilePath);
        }
    }
}
