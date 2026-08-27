using System;
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
    }
}
