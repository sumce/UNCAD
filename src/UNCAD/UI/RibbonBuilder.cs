using System;
using System.Linq;
using Autodesk.Windows;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 从 FeatureRegistry 自动构建 Ribbon 功能区：
    /// 每个 Feature 按 RibbonPanel 分组生成面板，每个命令生成一个按钮。
    /// 新增功能后无需改这里。
    /// </summary>
    public static class RibbonBuilder
    {
        public const string TabTitle = "UNCAD · UNSIAO Work™";

        public static void Build()
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null) return;

                // 防重复（插件重复加载时）
                if (ribbon.Tabs.Cast<RibbonTab>().Any(t => t.Title == TabTitle)) return;

                var tab = new RibbonTab { Title = TabTitle };

                foreach (var group in FeatureRegistry.Features.GroupBy(f => f.RibbonPanel))
                {
                    var panel = new RibbonPanelSource { Title = group.Key };
                    foreach (var feat in group)
                    {
                        foreach (var cmd in feat.Commands)
                        {
                            panel.Items.Add(new RibbonButton
                            {
                                Text = cmd,
                                ShowText = true,
                                CommandParameter = cmd,
                                Size = RibbonItemSize.Large,
                                ToolTip = feat.Description
                            });
                        }
                    }
                    tab.Panels.Add(new RibbonPanel { Source = panel });
                }

                ribbon.Tabs.Add(tab);
            }
            catch (Exception ex)
            {
                // Ribbon 构建失败不影响命令可用
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                    .MdiActiveDocument?.Editor.WriteMessage("\n[Ribbon] " + ex.Message);
            }
        }
    }
}
