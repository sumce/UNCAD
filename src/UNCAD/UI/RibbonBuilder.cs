using System;
using System.Linq;
using System.Windows.Input;
using Autodesk.Windows;
using UNCAD.Infra;

namespace UNCAD.UI
{
    public static class RibbonBuilder
    {
        public const string TabId = "UNCAD.Ribbon.Tab";
        public const string TabTitle = "UNCAD · UNSIAO Work™";
        private static readonly ICommand CommandHandler = new CadRibbonCommandHandler();
        private static bool _eventsAttached;
        private static bool _idleAttached;
        private static bool _building;

        public static void Build()
        {
            AttachEvents();
            TryBuildSafely();
        }

        private static void AttachEvents()
        {
            if (!_eventsAttached)
            {
                ComponentManager.ItemInitialized += OnRibbonItemInitialized;
                _eventsAttached = true;
            }
            if (!_idleAttached)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnApplicationIdle;
                _idleAttached = true;
            }
        }

        private static void OnRibbonItemInitialized(object sender, RibbonItemEventArgs args)
            => TryBuildSafely();

        private static void OnApplicationIdle(object sender, EventArgs args)
            => TryBuildSafely();

        private static void TryBuildSafely()
        {
            if (_building) return;
            try
            {
                _building = true;
                if (!TryBuild()) return;
                if (_idleAttached)
                {
                    Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnApplicationIdle;
                    _idleAttached = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Ribbon registration failed", ex);
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                    .MdiActiveDocument?.Editor.WriteMessage("\n[Ribbon] " + ex.Message);
            }
            finally { _building = false; }
        }

        private static bool TryBuild()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return false;
            if (ribbon.Tabs.Cast<RibbonTab>().Any(t => t.Id == TabId || t.Title == TabTitle)) return true;

            var tab = new RibbonTab { Id = TabId, Title = TabTitle };
            tab.Panels.Add(BuildQuickPanel());
            tab.Panels.Add(BuildAnnotationPanel());
            tab.Panels.Add(BuildDrawingPanel());
            tab.Panels.Add(BuildStatisticsPanel());
            ribbon.Tabs.Add(tab);
            Log.Info("Ribbon registered: " + TabTitle);
            return true;
        }

        private static RibbonPanel BuildQuickPanel()
        {
            var panel = new RibbonPanelSource { Title = "UNSIAO Work™" };
            panel.Items.Add(CreateButton("清单填充", "UNC_FILL", "根据机台和回路数据自动填充图纸清单"));
            panel.Items.Add(CreateButton("提交记录", "UNC_SUBMIT", "读取当前图纸信息并更新 Excel 提交记录"));
            panel.Items.Add(CreateButton("配置中心", "UNC_SET", "集中设置数据源、文字和绘图参数"));
            panel.Items.Add(CreateButton("关于", "UNC_ABOUT", "查看版本、授权状态和联系方式"));
            return new RibbonPanel { Source = panel };
        }

        private static RibbonPanel BuildAnnotationPanel()
        {
            var panel = new RibbonPanelSource { Title = "标注" };
            panel.Items.Add(CreateButton("绘制线管", "UNC_CONDUIT", "按当前配置生成线管和直径标注"));
            panel.Items.Add(CreateMenuButton("线管规格", "UNC_CONDUIT", "选择常用线管直径",
                new MenuEntry("直径 20", "UNC_CONDUIT20", "使用 20 mm 线管规格"),
                new MenuEntry("直径 25", "UNC_CONDUIT25", "使用 25 mm 线管规格"),
                new MenuEntry("直径 32", "UNC_CONDUIT32", "使用 32 mm 线管规格")));
            panel.Items.Add(CreateButton("桥架标注", "UNC_TRAY", "选择线段并生成桥架规格标注"));
            panel.Items.Add(CreateMenuButton("桥架规格", "UNC_TRAY", "选择常用桥架规格",
                new MenuEntry("桥架 100", "UNC_TRAY100", "使用 100 mm 桥架规格"),
                new MenuEntry("桥架 200", "UNC_TRAY200", "使用 200 mm 桥架规格"),
                new MenuEntry("桥架 400", "UNC_TRAY400", "使用 400 mm 桥架规格")));
            panel.Items.Add(CreateMenuButton("标注设置", "UNC_SET", "调整线管和桥架标注参数",
                new MenuEntry("线管设置", "UNC_CONDUIT_SET", "设置线管高度、偏移和方向"),
                new MenuEntry("桥架设置", "UNC_TRAY_SET", "设置桥架规格、文字和偏移")));
            return new RibbonPanel { Source = panel };
        }

        private static RibbonPanel BuildDrawingPanel()
        {
            var panel = new RibbonPanelSource { Title = "绘制" };
            panel.Items.Add(CreateButton("连续画线", "UNC_LINE", "连续绘制线段并自动生成长度文字"));
            panel.Items.Add(CreateButton("拱桥开洞", "UNC_ARCH", "在线段交叉位置生成拱桥开洞"));
            panel.Items.Add(CreateMenuButton("绘制设置", "UNC_SET", "调整画线和拱桥参数",
                new MenuEntry("画线设置", "UNC_LINE_SET", "设置线段文字、高度和位置"),
                new MenuEntry("开洞设置", "UNC_ARCH_SET", "设置拱桥开洞直径")));
            return new RibbonPanel { Source = panel };
        }

        private static RibbonPanel BuildStatisticsPanel()
        {
            var panel = new RibbonPanelSource { Title = "统计" };
            panel.Items.Add(CreateButton("图纸统计", "UNC_STAT", "统计所选对象并在图纸中生成汇总文字"));
            panel.Items.Add(CreateButton("Excel 报表", "UNC_STAT_EX", "统计所选对象并导出 Excel 报表"));
            return new RibbonPanel { Source = panel };
        }

        private static RibbonButton CreateButton(
            string text, string command, string tooltip, RibbonItemSize size = RibbonItemSize.Large)
        {
            return new RibbonButton
            {
                Text = text, ShowText = true, ShowImage = true, CommandParameter = command,
                CommandHandler = CommandHandler, Size = size, ToolTip = tooltip,
                Image = RibbonIconFactory.Create(command, 16),
                LargeImage = RibbonIconFactory.Create(command, 32)
            };
        }

        private static RibbonMenuButton CreateMenuButton(
            string text, string iconCommand, string tooltip, params MenuEntry[] entries)
        {
            var menu = new RibbonMenuButton
            {
                Text = text, ShowText = true, ShowImage = true, Size = RibbonItemSize.Large,
                ToolTip = tooltip, IsSplit = false, IsSynchronizedWithCurrentItem = false,
                Image = RibbonIconFactory.Create(iconCommand, 16),
                LargeImage = RibbonIconFactory.Create(iconCommand, 32)
            };
            foreach (MenuEntry entry in entries)
                menu.Items.Add(CreateButton(entry.Text, entry.Command, entry.Tooltip, RibbonItemSize.Standard));
            return menu;
        }

        private sealed class MenuEntry
        {
            public MenuEntry(string text, string command, string tooltip)
            {
                Text = text; Command = command; Tooltip = tooltip;
            }
            public string Text { get; }
            public string Command { get; }
            public string Tooltip { get; }
        }

        private sealed class CadRibbonCommandHandler : ICommand
        {
            event EventHandler ICommand.CanExecuteChanged { add { } remove { } }

            public bool CanExecute(object parameter)
                => Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                    .MdiActiveDocument != null && ResolveCommand(parameter).Length > 0;

            public void Execute(object parameter)
            {
                string command = ResolveCommand(parameter);
                if (command.Length == 0) return;
                Autodesk.AutoCAD.ApplicationServices.Document document =
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                document?.SendStringToExecute(command + " ", true, false, true);
            }

            private static string ResolveCommand(object parameter)
            {
                object value = parameter is RibbonButton button
                    ? button.CommandParameter : parameter;
                return (Convert.ToString(value) ?? "").Trim();
            }
        }
    }
}
