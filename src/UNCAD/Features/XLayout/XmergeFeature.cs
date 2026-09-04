using System;
using System.IO;
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
            using (var form = new XmergeForm())
            {
                if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form)
                    != System.Windows.Forms.DialogResult.OK) return;
                if (form.Files.Count == 0) return;
                XmergeResult result = XmergeService.Merge(ctx, form.Files);
                ctx.Write("\n[Xmerge] 已合并 " + result.FileCount + " 个 DWG，导入 "
                    + result.EntityCount + " 个实体。当前图纸已按 XLAYOUT 间距排布。"
                    + (result.UnplacedCount > 0
                        ? "\n[Xmerge] 注意: " + result.UnplacedCount
                            + " 个无外包框实体无法定位归属，已按原坐标导入。"
                        : ""));
            }
        }
    }
}
