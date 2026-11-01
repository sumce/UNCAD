using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.UI;
using UNCAD.Infra;

namespace UNCAD.Features.ConfigCenter
{
    /// <summary>
    /// 配置中心 U1SET：所有功能的配置集中在一个页面（页签：线段/桥架/拱桥/统计/输出与分享/日志与记录）。
    /// 旧的“功能命令打开指定页签”入口（Show(0/1/2)）已废弃，统一只通过 U1SET 打开。
    /// </summary>
    [Feature("settings", "配置中心",
        Commands = CommandIds.Settings,
        Description = "统一配置所有功能")]
    public sealed class SettingsFeature : CommandBase
    {
        [CommandMethod(CommandIds.Settings)]
        public void UncadSet() => Run();

        protected override void Execute(CadContext ctx) => Show();

        /// <summary>打开配置中心；返回是否点了确定（已保存）。</summary>
        internal static bool Show()
        {
            try
            {
                using (var dlg = new UnifiedSettingsForm(0))
                {
                    if (Application.ShowModalDialog(dlg) !=
                        System.Windows.Forms.DialogResult.OK)
                        return false;
                    dlg.Save();
                    Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[U1SET] 配置已保存！");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    "\n[U1SET] 配置保存失败: " + ex.Message);
                Log.Error("U1SET failed", ex);
                return false;
            }
        }
    }
}
