using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.XLayout
{
    /// <summary>GUI-driven batch DWG importer using the XLAYOUT placement contract.</summary>
    [Feature("xmerge", "DWG 批量合并", Commands = CommandIds.MergeFeatureCommands,
        Description = "拖入 DWG 或文件夹，递归添加并按 XLAYOUT 规则合并到当前图纸")]
    public sealed class XmergeFeature : CommandBase
    {
        [CommandMethod(CommandIds.Merge)]
        public void Merge() => Run();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.Merge);

            // 3.0 WPF 窗体（D-038）：确认与否用 Confirmed 表达，取消/关闭/Esc 一律 false。
            var window = new XmergeWindow();
            Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(window);
            if (!window.Confirmed) return;
            if (window.Files.Count == 0)
            {
                ctx.Write("\n[Xmerge] 未选择要合并的 DWG，未执行合并。");
                return;
            }
            XmergeResult result = XmergeService.Merge(ctx, window.Files);
            // Cloned AutoCAD tables can keep a stale display cache until the next REGEN.
            // Refresh now so a successful merge is immediately visible to the user.
            ctx.Ed.Regen();
            ctx.Write("\n[Xmerge] 已合并 " + result.FileCount + " 个 DWG，导入 "
                + result.FrameCount + " 个图框、" + result.EntityCount
                + " 个实体。当前图纸已按 XLAYOUT 间距排布。"
                + (result.UnplacedEntityCount > 0
                    ? "\n[Xmerge] 注意: " + result.UnplacedEntityCount
                        + " 个无可用坐标的实体已按源坐标导入。"
                    : ""));
        }
    }
}
