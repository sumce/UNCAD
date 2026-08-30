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
        public const string TabTitle = "UNCAD";
        private static readonly ICommand CommandHandler = new CadRibbonCommandHandler();
        private static bool _eventsAttached;
        private static bool _idleAttached;
        private static bool _building;
        private static DateTime _retryAfterUtc;

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
            if (_building || DateTime.UtcNow < _retryAfterUtc) return;
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
                _retryAfterUtc = DateTime.UtcNow.AddSeconds(2);
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
            RibbonTab existing = ribbon.Tabs.Cast<RibbonTab>()
                .FirstOrDefault(t => t.Id == TabId || t.Title == TabTitle);
            if (existing != null)
            {
                existing.IsVisible = true;
                return true;
            }

            var tab = new RibbonTab { Id = TabId, Title = TabTitle, IsVisible = true };
            foreach (RibbonPanelDefinition definition in RibbonCatalog.Panels)
                tab.Panels.Add(BuildPanel(definition));
            ribbon.Tabs.Add(tab);
            Log.Info("Ribbon registered: " + TabTitle);
            return true;
        }

        private static RibbonPanel BuildPanel(RibbonPanelDefinition definition)
        {
            var source = new RibbonPanelSource { Title = definition.Title };
            RibbonItemSize size = definition.Title == "清单"
                ? RibbonItemSize.Large : RibbonItemSize.Standard;
            foreach (RibbonItemDefinition item in definition.Items)
            {
                source.Items.Add(item.IsMenu
                    ? (RibbonItem)CreateMenuButton(item, size)
                    : CreateButton(item, size));
            }
            return new RibbonPanel { Source = source };
        }

        private static RibbonButton CreateButton(RibbonItemDefinition definition, RibbonItemSize size)
        {
            return new RibbonButton
            {
                Text = definition.Text,
                ShowText = true,
                ShowImage = true,
                CommandParameter = definition.Command,
                CommandHandler = CommandHandler,
                Size = size,
                ToolTip = definition.ToolTip,
                Image = RibbonIconFactory.Create(definition.IconCommand, 16),
                LargeImage = RibbonIconFactory.Create(definition.IconCommand, 32)
            };
        }

        private static RibbonSplitButton CreateMenuButton(RibbonItemDefinition definition,
            RibbonItemSize size)
        {
            var menu = new RibbonSplitButton
            {
                Text = definition.Text,
                ShowText = true,
                ShowImage = true,
                Size = size,
                ToolTip = definition.ToolTip,
                IsSplit = false,
                IsSynchronizedWithCurrentItem = false,
                Image = RibbonIconFactory.Create(definition.IconCommand, 16),
                LargeImage = RibbonIconFactory.Create(definition.IconCommand, 32)
            };
            foreach (RibbonItemDefinition child in definition.Children)
                menu.Items.Add(CreateButton(child, RibbonItemSize.Standard));
            return menu;
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
                object value = parameter;
                if (parameter is RibbonButton button) value = button.CommandParameter;
                else if (parameter is RibbonMenuItem menuItem) value = menuItem.CommandParameter;
                return (Convert.ToString(value) ?? "").Trim();
            }
        }
    }
}
