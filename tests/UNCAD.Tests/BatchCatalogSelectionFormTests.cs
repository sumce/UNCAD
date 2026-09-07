using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class BatchCatalogSelectionFormTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Dialog_RequiresEveryExplicitChoiceAndSupportsCancellation(bool cancel)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var catalog = new BoqCatalogIndex(ListItemReader.ReadEmbedded());
                    ListItem replacement = catalog.FindBusPlugBox("400A");
                    ListItem cable = catalog.Cables[0];
                    var requests = new[]
                    {
                        new BatchCatalogRequest("H1:busPlugBox", "MECTT03", "AC Rack 2-1",
                            "母线插接箱", "N208 3P4W 3P350A", catalog.Items),
                        new BatchCatalogRequest("H1:cable", "MECTT03", "AC Rack 2-1",
                            "电缆", "UNKNOWN", catalog.Items)
                    };
                    Assert.All(requests[0].Candidates, item =>
                        Assert.Equal("母线插接箱", item.Category));
                    using (var form = new BatchCatalogSelectionForm(requests))
                    {
                        form.Show();
                        Application.DoEvents();
                        var grid = (DataGridView)form.Controls.Find("BatchCatalogGrid", true).Single();
                        var confirm = (Button)form.AcceptButton;
                        Assert.False(confirm.Enabled);
                        Assert.Null(grid.Rows[0].Cells["Catalog"].Value);
                        Assert.Null(grid.Rows[1].Cells["Catalog"].Value);
                        Assert.Equal("MECTT03 / AC Rack 2-1", grid.Rows[0].Cells["Frame"].Value);
                        Assert.Contains("350A", Convert.ToString(grid.Rows[0].Cells["Original"].Value));

                        SelectThroughDropdown(grid, 0, replacement.Code);
                        Assert.Equal(replacement.Code, grid.Rows[0].Cells["Catalog"].Value);
                        Assert.False(confirm.Enabled);
                        SelectThroughDropdown(grid, 1, cable.Code);
                        Assert.Equal(cable.Code, grid.Rows[1].Cells["Catalog"].Value);
                        Assert.True(confirm.Enabled, "Selections were not accepted: "
                            + grid.Rows[0].Cells["Catalog"].Value + " / "
                            + grid.Rows[1].Cells["Catalog"].Value);

                        if (cancel)
                        {
                            ((Button)form.CancelButton).PerformClick();
                            Assert.Equal(DialogResult.Cancel, form.DialogResult);
                            Assert.Null(form.Selections);
                        }
                        else
                        {
                            confirm.PerformClick();
                            Assert.Equal(DialogResult.OK, form.DialogResult);
                            Assert.Same(replacement, form.Selections["H1:busPlugBox"]);
                            Assert.Same(cable, form.Selections["H1:cable"]);
                        }
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static void SelectThroughDropdown(DataGridView grid, int row, string code)
        {
            grid.CurrentCell = grid.Rows[row].Cells["Catalog"];
            Assert.True(grid.BeginEdit(true));
            var editor = Assert.IsType<DataGridViewComboBoxEditingControl>(grid.EditingControl);
            editor.SelectedIndex = Enumerable.Range(0, editor.Items.Count).Single(index =>
                ((ListItem)editor.Items[index]).Code == code);
            Application.DoEvents();
        }
    }
}
