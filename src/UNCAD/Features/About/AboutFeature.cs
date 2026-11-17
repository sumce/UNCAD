using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.UI;
using UNCAD.Infra;

namespace UNCAD.Features.About
{
    [Feature("about", "关于 UNCAD", Commands = CommandIds.AboutFeatureCommands,
        Description = "查看版本、授权状态、开发者和联系方式")]
    public sealed class AboutFeature : CommandBase
    {
        [CommandMethod(CommandIds.About)]
        public void UncadAbout() => Run();

        protected override void Execute(CadContext ctx)
        {
            // 3.0 WPF 窗体（D-038，P7 品牌页）；只读展示，无返回结果。
            var window = new AboutWindow();
            UNCAD.UI.ModalWindowHost.Show(window);
        }
    }
}
