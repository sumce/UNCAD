using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Infra;
using UNCAD.UI;

[assembly: ExtensionApplication(typeof(UNCAD.Bootstrap))]

namespace UNCAD
{
    /// <summary>插件启动：扫描 Feature 注册表、构建 Ribbon、输出横幅。</summary>
    public class Bootstrap : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage(
                "\n[UNCAD] " + Branding.Nameplate + " | 已加载：" + FeatureRegistry.Summary);
            RibbonBuilder.Build();
            Log.Info("UNCAD initialized; Ribbon registration requested");
        }

        public void Terminate() { }
    }
}
