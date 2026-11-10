using System.Linq;
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
            // D-051（2026-11-10）：坏文件（需要修复/损坏/被占用）不再炸整批——
            // 逐一点名并让用户决定是否跳过后继续合并其余文件。
            XmergeResult result = XmergeService.Merge(ctx, window.Files, unreadable =>
            {
                string detail = string.Join("\r\n", unreadable.Select(item =>
                    "  " + item.Path + " —— " + item.Reason));
                System.Windows.Forms.DialogResult choice =
                    System.Windows.Forms.MessageBox.Show(
                        "以下 " + unreadable.Count + " 个文件无法读取：\r\n\r\n"
                        + detail
                        + "\r\n\r\n是否跳过这些文件，继续合并其余文件？",
                        "Xmerge 部分文件无法读取",
                        System.Windows.Forms.MessageBoxButtons.YesNo,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                return choice == System.Windows.Forms.DialogResult.Yes;
            });
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
            if (result.SkippedFiles.Length > 0)
                ctx.Write("\n[Xmerge] 已跳过 " + result.SkippedFiles.Length
                    + " 个无法读取的文件：\n  "
                    + string.Join("\n  ", result.SkippedFiles));
        }
    }
}
