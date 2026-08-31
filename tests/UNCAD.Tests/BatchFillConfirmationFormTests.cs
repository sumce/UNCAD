using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public class BatchFillConfirmationFormTests
    {
        [Fact]
        public void Dialog_ShowsEveryFrameAndExplicitConfirmationButtons()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = Enumerable.Range(1, 54).Select(index =>
                        new BatchFillConfirmationRow("H" + index, "M" + index,
                            "设备" + index, index % 2 == 0 ? "有" : "无", index + 1))
                        .ToList();
                    using (var form = new BatchFillConfirmationForm(rows))
                    {
                        form.Show();
                        Application.DoEvents();
                        DataGridView grid = Find<DataGridView>(form);
                        Assert.NotNull(grid);
                        Assert.Equal(54, grid.Rows.Count);
                        Assert.Equal(5, grid.Columns.Count);
                        Assert.Equal("M1", grid.Rows[0].Cells[1].Value);
                        Assert.Equal("确认更新", FindButton(form).Text);
                        Assert.Same(FindButton(form), form.AcceptButton);
                        Assert.True(form.ClientSize.Width >= 940);
                        Assert.True(form.ClientSize.Height >= 600);
                        form.Close();
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static Button FindButton(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Button button && button.Text == "确认更新") return button;
                Button nested = FindButton(child);
                if (nested != null) return nested;
            }
            return null;
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
