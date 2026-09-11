using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    [CollectionDefinition("Settings UI", DisableParallelization = true)]
    public sealed class SettingsUiCollection { }

    [Collection("Settings UI")]
    public class UnifiedSettingsFormTests
    {
        [Fact]
        public void Navigation_PreservesSevenCommandTabIndices()
        {
            RunSta(() =>
            {
                string[] titles =
                {
                    "线段绘制", "桥架标注", "线管标注", "拱桥开洞",
                    "统计汇总", "文字样式", "Excel 数据"
                };
                for (int index = 0; index < titles.Length; index++)
                {
                    using (var form = new UnifiedSettingsForm(index))
                    {
                        form.Show();
                        Application.DoEvents();
                        TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                        Assert.NotNull(tabs);
                        Assert.IsType<UiNavigationTabControl>(tabs);
                        Assert.Equal(TabAlignment.Left, tabs.Alignment);
                        Assert.Equal(TabDrawMode.OwnerDrawFixed, tabs.DrawMode);
                        Assert.Equal(titles, tabs.TabPages.Cast<TabPage>()
                            .Select(page => page.Text).ToArray());
                        Assert.Equal(index, tabs.SelectedIndex);
                    }
                }
            });
        }

        [Fact]
        public void StatisticsTab_UsesFlatCardsAndKeepsAllControls()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(4))
                {
                    form.Show();
                    form.Size = form.MinimumSize;
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("统计汇总", tabs.SelectedTab.Text);
                    Assert.Empty(FindAll<GroupBox>(tabs.SelectedTab));
                    List<UiCard> cards = FindAll<UiCard>(tabs.SelectedTab);
                    List<CheckBox> switches = FindAll<CheckBox>(tabs.SelectedTab);
                    Assert.Equal(3, cards.Count);
                    Assert.Equal(6, switches.Count);
                    Assert.Contains(switches, checkBox =>
                        checkBox.Text == "尺寸标注文字 (DIMENSION)");
                    Assert.Equal(2, FindAll<NumericUpDown>(tabs.SelectedTab).Count);
                    Assert.All(cards, card =>
                    {
                        Assert.True(card.Visible);
                        Assert.True(card.Width > 0 && card.Height > 60);
                        Assert.Equal(8, card.CornerRadius);
                    });
                    Assert.All(switches, item =>
                    {
                        Assert.True(item.Visible);
                        Assert.True(item.Width > 0 && item.Height > 0);
                    });
                    Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                    Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
                    Assert.True(tabs.Width > 0 && tabs.Height > 0);
                }
            });
        }

        [Fact]
        public void TextStyleTab_ExposesStartupSplashSwitch()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(5))
                {
                    form.Show();
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("文字样式", tabs.SelectedTab.Text);
                    Assert.Contains(FindAll<CheckBox>(tabs.SelectedTab), checkBox =>
                        checkBox.Text == "启动时显示全屏品牌动画");
                }
            });
        }

        [Fact]
        public void ExcelTab_ShowsSnapshotTableOutputAndIndependentAutofillCategories()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(6))
                {
                    form.Show();
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("Excel 数据", tabs.SelectedTab.Text);
                    Assert.Empty(FindAll<GroupBox>(tabs.SelectedTab));
                    Assert.Equal(4, FindAll<UiCard>(tabs.SelectedTab).Count);

                    List<NumericUpDown> numbers = FindAll<NumericUpDown>(tabs.SelectedTab);
                    List<ComboBox> combos = FindAll<ComboBox>(tabs.SelectedTab);
                    Assert.Equal(2, combos.Count);
                    List<ComboBox> colors = combos.Where(color =>
                        color.DrawMode == DrawMode.OwnerDrawFixed).ToList();
                    Assert.Equal(2, colors.Count);
                    Assert.All(colors, color =>
                    {
                        Assert.Equal(ComboBoxStyle.DropDownList, color.DropDownStyle);
                        Assert.Contains(color.Items.Cast<object>(),
                            item => item.ToString().Contains("ACI 3"));
                        Assert.Contains(color.Items.Cast<object>(),
                            item => item.ToString().Contains("ACI 6"));
                    });
                    Assert.Equal(4, numbers.Count);
                    Assert.Contains(FindAll<Button>(tabs.SelectedTab),
                        button => button.Text == "浏览...");
                    Assert.Contains(FindAll<Button>(tabs.SelectedTab),
                        button => button.Text == "刷新");
                    NumericUpDown clearRows = Assert.Single(numbers, number =>
                        number.DecimalPlaces == 0 && number.Minimum == 1m
                            && number.Maximum == 100m);
                    Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                        label => label.Text == "每次清空数据行数");
                    Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                        label => label.Text == "软管手动数量（m）");
                    Label snapshot = Find<Label>(tabs.SelectedTab, "MachineSnapshotStatus");
                    Assert.NotNull(snapshot);
                    Assert.False(string.IsNullOrWhiteSpace(snapshot.Text));

                    List<CheckBox> autoFill = FindAll<CheckBox>(tabs.SelectedTab);
                    Assert.Equal(new[]
                    {
                        "电缆", "断路器", "软管", "线管",
                        "桥架", "插座盘", "插接箱"
                    }, autoFill.Select(check => check.Text).ToArray());
                    Assert.DoesNotContain(autoFill,
                        check => check.Text.Contains("电盘") || check.Text.Contains("未匹配"));
                }
            });
        }

        [Fact]
        public void About_ShowsAuthorizationCardFirstAndKeepsTermsAccessible()
        {
            RunSta(() =>
            {
                using (var form = new AboutForm())
                {
                    form.Show();
                    form.Size = form.MinimumSize;
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "AboutNavigation");
                    Assert.NotNull(tabs);
                    Assert.Equal(TabAlignment.Left, tabs.Alignment);
                    Assert.Equal(new[] { "概览", "使用条款" }, tabs.TabPages
                        .Cast<TabPage>().Select(page => page.Text).ToArray());
                    UiCard authorization = Find<UiCard>(tabs.SelectedTab,
                        "AuthorizationCard");
                    Assert.NotNull(authorization);
                    Assert.True(authorization.Visible);
                    Assert.True(authorization.Height >= 260);
                    Assert.True(authorization.CornerRadius >= 14);
                    Assert.Contains(FindAll<Label>(authorization),
                        label => label.Text == "授权模式");
                    Assert.Contains(FindAll<Label>(authorization),
                        label => label.Text == "授权客户");
                    Label status = Find<Label>(authorization, "AuthorizationStatus");
                    Rectangle statusBounds = authorization.RectangleToClient(
                        status.RectangleToScreen(status.ClientRectangle));
                    Assert.True(statusBounds.Right <= authorization.ClientRectangle.Right,
                        "授权状态文字超出卡片: " + statusBounds);

                    tabs.SelectedIndex = 1;
                    Application.DoEvents();
                    TextBox terms = Assert.Single(FindAll<TextBox>(tabs.SelectedTab));
                    Assert.True(terms.Multiline);
                    Assert.True(terms.ReadOnly);
                    Assert.False(string.IsNullOrWhiteSpace(terms.Text));
                }
            });
        }

        [Fact]
        public void OnlineLicense_UsesProminentCardAndKeepsValidationActions()
        {
            RunSta(() =>
            {
                using (var form = new OnlineLicenseForm("请输入授权码。"))
                {
                    form.Show();
                    Application.DoEvents();
                    UiCard card = Find<UiCard>(form, "AuthorizationCard");
                    Assert.NotNull(card);
                    Assert.Equal(16, card.CornerRadius);
                    Assert.Equal(DockStyle.Fill, card.Dock);
                    Assert.NotNull(Find<TextBox>(form, "AuthorizationCode"));
                    Assert.Contains(FindAll<Button>(form),
                        button => button.Text == "验证并保存");
                    Assert.Contains(FindAll<Button>(form),
                        button => button.Text == "稍后处理");
                    Assert.Equal(FormBorderStyle.FixedDialog, form.FormBorderStyle);
                }
            });
        }

        [Fact]
        public void ExcelRefresh_RemainsExplicitAndRunsOffTheUiThread()
        {
            string source = File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "UNCAD", "UI", "UnifiedSettingsForm.cs")));

            Assert.Contains("private async void RefreshMachineWorkbook", source);
            Assert.Contains("await Task.Run", source);
            Assert.Contains("button.Text = \"刷新中...\"", source);
            Assert.Contains("MachineWorkbookSource.TryGetSnapshot", source);
            Assert.DoesNotContain("Shown += RefreshMachineWorkbook", source);
        }

        private static void RunSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static T Find<T>(Control root, string name = null) where T : Control
        {
            return FindAll<T>(root).FirstOrDefault(control =>
                name == null || control.Name == name);
        }

        private static List<T> FindAll<T>(Control root) where T : Control
        {
            var result = new List<T>();
            foreach (Control child in root.Controls)
            {
                if (child is T match) result.Add(match);
                result.AddRange(FindAll<T>(child));
            }
            return result;
        }
    }
}
