using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.Help
{
    /// <summary>Opens the read-only command reference for all public commands.</summary>
    [Feature("help", "命令帮助", Commands = CommandIds.HelpFeatureCommands,
        Description = "查看全部命令的使用方法、功能和注意事项")]
    public sealed class HelpFeature : CommandBase
    {
        [CommandMethod(CommandIds.Help)]
        public void ShowHelp() => Run();

        protected override void Execute(CadContext ctx)
        {
            // 3.0 WPF 窗体（D-038，P5 内容阅读）；只读展示，无返回结果。
            var window = new CommandHelpWindow(CommandHelpCatalog.All);
            Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(window);
        }
    }
}
