using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Autodesk.Windows;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public class RibbonBuilderSmokeTests
    {
        [Fact]
        public void EveryCatalogPanel_BuildsWithAutoCadSupportedItemTypes()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    MethodInfo buildPanel = typeof(RibbonBuilder).GetMethod("BuildPanel",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    Assert.NotNull(buildPanel);

                    foreach (RibbonPanelDefinition definition in RibbonCatalog.Panels)
                    {
                        var panel = (RibbonPanel)buildPanel.Invoke(null, new object[] { definition });
                        Assert.Equal(definition.Title, panel.Source.Title);
                        Assert.Equal(definition.Items.Count, panel.Source.Items.Count);

                        for (int i = 0; i < definition.Items.Count; i++)
                        {
                            RibbonItemDefinition item = definition.Items[i];
                            if (item.IsMenu)
                            {
                                var menu = Assert.IsType<RibbonSplitButton>(panel.Source.Items[i]);
                                Assert.Equal(item.Children.Count, menu.Items.Count);
                                Assert.All(menu.Items, child => Assert.IsType<RibbonButton>(child));
                            }
                            else
                            {
                                Assert.IsType<RibbonButton>(panel.Source.Items[i]);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    failure = ex is TargetInvocationException invocation && invocation.InnerException != null
                        ? invocation.InnerException : ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Ribbon construction timed out.");
            if (failure != null) throw failure;
        }

        [Fact]
        public void SynchronizeTab_ReplacesStalePanelsWithCurrentCatalog()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var tab = new RibbonTab { Id = RibbonBuilder.TabId, Title = "UNCAD" };
                    tab.Panels.Add(new RibbonPanel
                    {
                        Source = new RibbonPanelSource { Title = "旧版 U1X" }
                    });

                    RibbonBuilder.SynchronizeTab(tab);

                    Assert.Equal(RibbonCatalog.Panels.Count, tab.Panels.Count);
                    Assert.DoesNotContain(tab.Panels,
                        panel => panel.Source.Title == "旧版 U1X");
                    Assert.Equal(RibbonCatalog.Panels.Select(panel => panel.Title),
                        tab.Panels.Select(panel => panel.Source.Title));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Ribbon synchronization timed out.");
            if (failure != null) throw failure;
        }

        [Fact]
        public void Registration_UsesStableIdAndBuildsPanelsBeforeClearingOldOnes()
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            string source = File.ReadAllText(Path.Combine(root, "src", "UNCAD", "UI",
                "RibbonBuilder.cs"));

            Assert.DoesNotContain("t.Title == TabTitle", source);
            int build = source.IndexOf(
                "RibbonCatalog.Panels.Select(BuildPanel).ToArray()",
                StringComparison.Ordinal);
            int clear = source.IndexOf("tab.Panels.Clear()", StringComparison.Ordinal);
            Assert.True(build >= 0 && build < clear);
        }
    }
}
