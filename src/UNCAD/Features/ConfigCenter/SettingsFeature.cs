using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.UI;
using UNCAD.Infra;

namespace UNCAD.Features.ConfigCenter
{
    /// <summary>
    /// 配置中心 U1SET：所有功能的配置集中在一个窗口（3.0 WPF 版，D-038，P1 导航枢纽：
    /// 概览/绘制/统计/样式/启动/机台/清单/Downstream/颜色/输出与分享/日志与记录）。
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

        /// <summary>打开配置中心；返回是否点了保存（已写入配置）。</summary>
        internal static bool Show()
        {
            try
            {
                // 确认与否用 Confirmed 表达，取消/关闭/Esc 一律 false；
                // 写入仍集中在窗口的 Save()（Features 只读）。
                var window = new SettingsWindow(0);
                UNCAD.UI.ModalWindowHost.Show(window);
                if (!window.Confirmed) return false;
                window.Save();
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[U1SET] 配置已保存！");
                return true;
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
