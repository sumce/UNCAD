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
        public void PrimaryLayout_HasFourPanelsAndFourteenTaskOrientedControls()
        {
            Assert.Equal(new[] { "UNSIAO Work™", "标注", "绘制", "统计" },
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
        }

        [Fact]
        public void Menus_ContainOnlyExecutableLeafActions()
        {
            RibbonItemDefinition[] menus = RibbonCatalog.Panels
                .SelectMany(panel => panel.Items).Where(item => item.IsMenu).ToArray();
            Assert.Equal(4, menus.Length);
            Assert.Equal(new[] { 3, 3, 2, 2 }, menus.Select(menu => menu.Children.Count));
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
        public void EveryRibbonAction_UsesOneRegisteredCanonicalCommand()
        {
            var canonical = new HashSet<string>(CommandIds.Canonical,
                StringComparer.OrdinalIgnoreCase);
            string[] ribbonCommands = RibbonCatalog.CommandItems
                .Select(item => item.Command).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.All(ribbonCommands, command => Assert.Contains(command, canonical));
            Assert.All(canonical, command => Assert.Contains(command, ribbonCommands,
                StringComparer.OrdinalIgnoreCase));

        }
    }
}
