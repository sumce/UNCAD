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
                        Assert.NotNull(FindButton(form, "新增清单项"));
                        Button removeManual = FindButton(form, "删除手动项");
                        Assert.NotNull(removeManual);
                        Assert.False(removeManual.Enabled);
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

        [Fact]
        public void Dialog_SoftConduitDiameterEditRefreshesCatalogRow()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow
                        {
                            Category = TableFillCategory.FlexibleConduit,
                            Name = "38mm软管", Description = "38mm旧模板",
                            Unit = "M", Quantity = "1.5", Code = "3.7"
                        }
                    };
                    var catalog = new List<ListItem>
                    {
                        new ListItem
                        {
                            Code = "3.6", Name = "25mm软管",
                            Feature = "25mm新模板", Unit = "m", Spec = "25mm"
                        }
                    };
                    using (var form = new FillReviewForm(FillReviewData.Create(
                        new MachineRow
                        {
                            MachineId = "M1",
                            CircuitName = "设备A",
                            Dia = "38"
                        }, rows), catalog))
                    {
                        form.Show();
                        Application.DoEvents();
                        TextBox diameter = FindTextBox(form, "38");
                        Assert.NotNull(diameter);
                        diameter.Text = "25";
                        Application.DoEvents();

                        FillReviewItem flexible = form.Data.FlexibleConduitItem();
                        Assert.Equal("25", form.Data.Machine.Dia);
                        Assert.Equal("3.6", flexible.Code);
                        Assert.Equal("25mm新模板", flexible.Description);
                        Assert.Equal("1.5", flexible.Quantity);
                        DataGridView grid = Find<DataGridView>(form);
                        Assert.Equal("3.6", grid.Rows[0].Cells["Code"].Value);
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
        public void Dialog_UnmatchedConduitsOpenWarningAndAllowPerModelSelection()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow
                        {
                            Category = TableFillCategory.RigidConduit, Name = "⌀32线管",
                            Description = "1.名称:⌀32线管", Quantity = "3",
                            CatalogMatched = false
                        },
                        new TableFillRow
                        {
                            Category = TableFillCategory.FlexibleConduit,
                            Name = "包塑金属软管",
                            Description = "1.名称:包塑金属软管\\P2.规格:32mm",
                            Quantity = "1.5", CatalogMatched = false
                        }
                    };
                    using (var form = new FillReviewForm(
                        FillReviewData.Create(new MachineRow { Dia = "32" }, rows)))
                    {
                        form.Show();
                        Application.DoEvents();
                        TabControl tabs = Find<TabControl>(form);
                        DataGridView grid = Find<DataGridView>(form);
                        Label warning = FindLabelContaining(form, "固定清单未找到");

                        Assert.Equal(1, tabs.SelectedIndex);
                        Assert.NotNull(warning);
                        Assert.Contains("32", warning.Text);
                        Assert.False((bool)grid.Rows[0].Cells["Included"].Value);
                        Assert.False((bool)grid.Rows[1].Cells["Included"].Value);

                        grid.Rows[0].Cells["Included"].Value = true;
                        Application.DoEvents();
                        Assert.Single(form.Data.SelectedRows());
                        Assert.Equal("⌀32线管", form.Data.SelectedRows()[0].Name);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static Button FindButton(Control root, string text)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Button button && button.Text == text) return button;
                Button nested = FindButton(child, text);
                if (nested != null) return nested;
            }
            return null;
        }

        private static Label FindLabelContaining(Control root, string text)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label label && label.Text.Contains(text)) return label;
                Label nested = FindLabelContaining(child, text);
                if (nested != null) return nested;
            }
            return null;
        }

        private static TextBox FindTextBox(Control root, string text)
        {
            foreach (Control child in root.Controls)
            {
                if (child is TextBox box && box.Text == text) return box;
                TextBox nested = FindTextBox(child, text);
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
