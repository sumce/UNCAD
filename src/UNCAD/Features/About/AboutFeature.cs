using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.UI;
using UNCAD.Infra;

namespace UNCAD.Features.About
{
    [Feature("about", "关于 UNCAD", Commands = CommandIds.About,
        Description = "查看版本、授权状态、开发者和联系方式")]
    public sealed class AboutFeature : CommandBase
    {
        [CommandMethod(CommandIds.About)]
        public void UncadAbout() => Run();

        protected override void Execute(CadContext ctx)
        {
            using (var form = new AboutForm())
                form.ShowDialog(new WindowWrapper(
                    Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle));
        }
    }
}
