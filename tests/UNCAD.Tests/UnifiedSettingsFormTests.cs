using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public class UnifiedSettingsFormTests
    {
        [Fact]
        public void StatisticsTab_HasGroupedSourceCategoryAndNumericControls()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new UnifiedSettingsForm(4))
                    {
                        form.Show();
                        Application.DoEvents();
                        var tabs = Find<TabControl>(form);
                        Assert.NotNull(tabs);
                        Assert.Equal("统计汇总", tabs.SelectedTab.Text);
                        List<GroupBox> groups = FindAll<GroupBox>(tabs.SelectedTab);
                        List<CheckBox> switches = FindAll<CheckBox>(tabs.SelectedTab);
                        Assert.Equal(3, groups.Count);
                        Assert.Equal(6, switches.Count);
                        Assert.Contains(switches, checkBox =>
                            checkBox.Text == "尺寸标注文字 (DIMENSION)");
                        Assert.Equal(2, FindAll<NumericUpDown>(tabs.SelectedTab).Count);
                        groups.Sort((left, right) => left.Top.CompareTo(right.Top));
                        foreach (GroupBox group in groups)
                        {
                            Assert.True(group.Visible);
                            Assert.InRange(group.Height, 55, 105);
                        }
                        Assert.True(groups[0].Bottom <= groups[1].Top);
                        Assert.True(groups[1].Bottom <= groups[2].Top);
                        foreach (CheckBox item in switches)
                        {
                            Assert.True(item.Visible);
                            Assert.True(item.Width > 0 && item.Height > 0);
                        }
                        Assert.True(form.ClientSize.Width >= 900);
                        Assert.True(form.ClientSize.Height >= 600);
                        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                        Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void TextStyleTab_ExposesStartupSplashSwitch()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new UnifiedSettingsForm(5))
                    {
                        form.Show();
                        Application.DoEvents();
                        TabControl tabs = Find<TabControl>(form);
                        Assert.Equal("文字样式", tabs.SelectedTab.Text);
                        Assert.Contains(FindAll<CheckBox>(tabs.SelectedTab), checkBox =>
                            checkBox.Text == "启动时显示全屏品牌动画");
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void ExcelTab_HasConfigurableTemplateClearRange()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new UnifiedSettingsForm(6))
                    {
                        form.Show();
                        Application.DoEvents();
                        TabControl tabs = Find<TabControl>(form);
                        Assert.Equal("Excel 数据", tabs.SelectedTab.Text);
                        List<NumericUpDown> numbers = FindAll<NumericUpDown>(tabs.SelectedTab);
                        List<ComboBox> combos = FindAll<ComboBox>(tabs.SelectedTab);
                        Assert.Equal(2, combos.Count);
                        List<ComboBox> colors = combos.Where(color =>
                            color.DrawMode == DrawMode.OwnerDrawFixed).ToList();
                        Assert.Equal(2, colors.Count);
                        Assert.All(colors, color =>
                        {
                            Assert.Equal(ComboBoxStyle.DropDownList, color.DropDownStyle);
                            Assert.Equal(DrawMode.OwnerDrawFixed, color.DrawMode);
                            Assert.Contains(color.Items.Cast<object>(),
                                item => item.ToString().Contains("ACI 3"));
                            Assert.Contains(color.Items.Cast<object>(),
                                item => item.ToString().Contains("ACI 6"));
                        });
                        Assert.Equal(4, numbers.Count);
                        Assert.Contains(FindAll<Button>(tabs.SelectedTab), button =>
                            button.Text == "浏览...");
                        Assert.Contains(FindAll<Button>(tabs.SelectedTab), button =>
                            button.Text == "刷新");
                        NumericUpDown clearRows = Assert.Single(numbers, number =>
                            number.DecimalPlaces == 0 && number.Minimum == 1m
                                && number.Maximum == 100m);
                        Assert.Equal(0, clearRows.DecimalPlaces);
                        Assert.Equal(1m, clearRows.Minimum);
                        Assert.Equal(100m, clearRows.Maximum);
                        Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                            label => label.Text == "每次清空数据行数:");
                        Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                            label => label.Text == "软管手动数量 (m):");
                        Assert.DoesNotContain(FindAll<CheckBox>(tabs.SelectedTab),
                            check => check.Text.Contains("未匹配"));
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void ExcelRefresh_DownloadsAndParsesOffTheUiThread()
        {
            string source = File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "UNCAD", "UI", "UnifiedSettingsForm.cs")));

            Assert.Contains("private async void RefreshMachineWorkbook", source);
            Assert.Contains("await Task.Run", source);
            Assert.Contains("button.Text = \"刷新中...\"", source);
        }

        private static T Find<T>(Control root) where T : Control
        {
            List<T> matches = FindAll<T>(root);
            return matches.Count > 0 ? matches[0] : null;
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
