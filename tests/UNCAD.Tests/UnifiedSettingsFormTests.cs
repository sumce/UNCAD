using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
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
                        Assert.Equal(5, switches.Count);
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
                        Assert.Equal(4, numbers.Count);
                        NumericUpDown clearRows = Assert.Single(numbers, number =>
                            number.DecimalPlaces == 0 && number.Minimum == 1m
                                && number.Maximum == 100m);
                        Assert.Equal(0, clearRows.DecimalPlaces);
                        Assert.Equal(1m, clearRows.Minimum);
                        Assert.Equal(100m, clearRows.Maximum);
                        Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                            label => label.Text == "每次清空数据行数:");
                        Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                            label => label.Text == "软管默认长度 (m):");
                        Assert.Contains(FindAll<CheckBox>(tabs.SelectedTab),
                            check => check.Text == "未匹配管材默认勾选");
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
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
