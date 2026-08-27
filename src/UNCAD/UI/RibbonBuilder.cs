using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Autodesk.Windows;
using UNCAD.Infra;

namespace UNCAD.UI
{
    public static class RibbonBuilder
    {
        public const string TabTitle = "UNCAD · UNSIAO Work™";
        private static readonly ICommand CommandHandler = new CadRibbonCommandHandler();
        private static readonly HashSet<string> QuickCommands = new HashSet<string>(
            new[] { "UNC_FILL", "UNC_SUBMIT", "UNC_SET", "UNC_ABOUT" },
            StringComparer.OrdinalIgnoreCase);

        public static void Build()
        {
            try
            {
                RibbonControl ribbon = ComponentManager.Ribbon;
                if (ribbon == null) return;
                if (ribbon.Tabs.Cast<RibbonTab>().Any(t => t.Title == TabTitle)) return;

                var tab = new RibbonTab { Title = TabTitle };
                tab.Panels.Add(BuildQuickPanel());

                foreach (var group in FeatureRegistry.Features.GroupBy(f => f.RibbonPanel))
                {
                    var panel = new RibbonPanelSource { Title = group.Key };
                    foreach (var feature in group)
                    {
                        foreach (string command in feature.Commands.Where(c => !QuickCommands.Contains(c)))
                            panel.Items.Add(CreateButton(command, command, feature.Description));
                    }
                    if (panel.Items.Count > 0) tab.Panels.Add(new RibbonPanel { Source = panel });
                }
                ribbon.Tabs.Add(tab);
            }
            catch (Exception ex)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                    .MdiActiveDocument?.Editor.WriteMessage("\n[Ribbon] " + ex.Message);
            }
        }

        private static RibbonPanel BuildQuickPanel()
        {
            var panel = new RibbonPanelSource { Title = "UNSIAO Work™" };
            panel.Items.Add(CreateButton("填充", "UNC_FILL", "按机台和回路填充图纸"));
            panel.Items.Add(CreateButton("提交", "UNC_SUBMIT", "读取图纸信息并更新提交记录"));
            panel.Items.Add(CreateButton("设置", "UNC_SET", "打开 UNCAD 配置中心"));
            panel.Items.Add(CreateButton("关于", "UNC_ABOUT", "查看版本、授权和联系方式"));
            return new RibbonPanel { Source = panel };
        }

        private static RibbonButton CreateButton(string text, string command, string tooltip)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                CommandParameter = command,
                CommandHandler = CommandHandler,
                Size = RibbonItemSize.Large,
                ToolTip = tooltip
            };
        }

        private sealed class CadRibbonCommandHandler : ICommand
        {
            event EventHandler ICommand.CanExecuteChanged { add { } remove { } }

            public bool CanExecute(object parameter)
                => Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                    .MdiActiveDocument != null && !string.IsNullOrWhiteSpace(Convert.ToString(parameter));

            public void Execute(object parameter)
            {
                string command = (Convert.ToString(parameter) ?? "").Trim();
                if (command.Length == 0) return;
                Autodesk.AutoCAD.ApplicationServices.Document document =
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                document?.SendStringToExecute(command + " ", true, false, true);
            }
        }
    }
}
