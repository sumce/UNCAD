using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Core.Contracts;
using UNCAD.UI;

namespace UNCAD.Features.ConfigCenter
{
    /// <summary>
    /// 配置中心 UNC_SET：所有功能的配置集中在一个页面（页签：线段/桥架/拱桥/统计）。
    /// 各功能 SET 命令通过 Show(tabIndex) 打开对应页签。
    /// </summary>
    [Feature("settings", "配置中心",
        RibbonPanel = "设置",
        Commands = "UNC_SET",
        Description = "统一配置所有功能")]
    public class SettingsFeature
    {
        [CommandMethod("UNC_SET")]
        public void UncadSet()
        {
            Show(0);
        }

        /// <summary>打开配置中心；返回是否点了确定（已保存）。</summary>
        internal static bool Show(int tabIndex)
        {
            try
            {
                using (var dlg = new UnifiedSettingsForm(tabIndex))
                {
                    if (dlg.ShowDialog(new WindowWrapper(Application.MainWindow.Handle)) !=
                        System.Windows.Forms.DialogResult.OK)
                        return false;
                    dlg.Save();
                    Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[UNC_SET] 配置已保存！");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[UNC_SET] 配置保存失败: " + ex.Message);
                return false;
            }
        }
    }
}
