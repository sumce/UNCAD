using System;
using System.Collections.Generic;
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
                        var tabs = Find<TabControl>(form);
                        Assert.NotNull(tabs);
                        Assert.Equal("统计汇总", tabs.SelectedTab.Text);
                        Assert.Equal(3, FindAll<GroupBox>(tabs.SelectedTab).Count);
                        Assert.Equal(5, FindAll<CheckBox>(tabs.SelectedTab).Count);
                        Assert.Equal(2, FindAll<NumericUpDown>(tabs.SelectedTab).Count);
                        Assert.True(form.ClientSize.Width >= 680);
                        Assert.True(form.ClientSize.Height >= 400);
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
