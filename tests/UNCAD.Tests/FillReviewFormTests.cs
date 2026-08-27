using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public class FillReviewFormTests
    {
        [Fact]
        public void Dialog_ShowsEditableBasicsAndEveryMatchedItemChecked()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "5", Code = "1.1" },
                        new TableFillRow { Category = TableFillCategory.Bridge, Name = "桥架", Quantity = "2", Code = "2.1" },
                        new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座", Quantity = "1", Code = "8.1" }
                    };
                    using (var form = new FillReviewForm(FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "C1" }, rows)))
                    {
                        form.Show();
                        Application.DoEvents();
                        TabControl tabs = Find<TabControl>(form);
                        Assert.NotNull(tabs);
                        Assert.Equal(new[] { "基础信息", "清单选择" },
                            new[] { tabs.TabPages[0].Text, tabs.TabPages[1].Text });
                        tabs.SelectedIndex = 1;
                        Application.DoEvents();
                        DataGridView grid = Find<DataGridView>(form);
                        Assert.Equal(3, grid.Rows.Count);
                        Assert.Equal(7, grid.Columns.Count);
                        foreach (DataGridViewRow row in grid.Rows)
                            Assert.True(Convert.ToBoolean(row.Cells["Included"].Value));
                        grid.Rows[2].Cells["Included"].Value = false;
                        Application.DoEvents();
                        Assert.False(form.Data.Items[2].Included);
                        Assert.True(form.ClientSize.Width >= 980);
                        Assert.True(form.ClientSize.Height >= 620);
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
            foreach (Control child in root.Controls)
            {
                if (child is T match) return match;
                T nested = Find<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
