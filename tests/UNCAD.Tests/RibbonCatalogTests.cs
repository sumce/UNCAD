using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public class RibbonCatalogTests
    {
        [Fact]
        public void PrimaryLayout_HasFourPanelsAndFifteenTaskOrientedControls()
        {
            Assert.Equal(new[] { "清单", "标注", "统计", "系统" },
                RibbonCatalog.Panels.Select(panel => panel.Title));

            RibbonItemDefinition[] primary = RibbonCatalog.Panels
                .SelectMany(panel => panel.Items).ToArray();
            Assert.Equal(14, primary.Length);
            Assert.All(primary, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Text));
                Assert.False(item.Text.StartsWith("UNC_", StringComparison.OrdinalIgnoreCase));
                Assert.False(string.IsNullOrWhiteSpace(item.ToolTip));
            });
            Assert.Equal(primary.Length, primary.Select(item => item.Text)
                .Distinct(StringComparer.Ordinal).Count());

            RibbonItemDefinition quickLine = Assert.Single(primary,
                item => string.Equals(item.Command, CommandIds.LineQuick,
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal("快速标注距离", quickLine.Text);
            Assert.Contains("标注", quickLine.ToolTip);
        }

        [Fact]
        public void Menus_ContainOnlyExecutableLeafActions()
        {
            RibbonItemDefinition[] menus = RibbonCatalog.Panels
                .SelectMany(panel => panel.Items).Where(item => item.IsMenu).ToArray();
            Assert.Single(menus);
            Assert.Equal(new[] { 3 }, menus.Select(menu => menu.Children.Count));
            Assert.All(menus, menu =>
            {
                Assert.Null(menu.Command);
                Assert.NotEmpty(menu.Children);
                Assert.All(menu.Children, child =>
                {
                    Assert.False(child.IsMenu);
                    Assert.False(string.IsNullOrWhiteSpace(child.Command));
                });
            });
        }

        [Fact]
        public void EveryRibbonAction_UsesOneRegisteredCommand()
        {
            var registered = new HashSet<string>(CommandIds.Registered,
                StringComparer.OrdinalIgnoreCase);
            string[] ribbonCommands = RibbonCatalog.CommandItems
                .Select(item => item.Command).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.All(ribbonCommands, command => Assert.Contains(command, registered));
            Assert.All(CommandIds.Canonical, command => Assert.Contains(command,
                ribbonCommands, StringComparer.OrdinalIgnoreCase));
            Assert.Contains(CommandIds.LegacyStatistics, ribbonCommands,
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
