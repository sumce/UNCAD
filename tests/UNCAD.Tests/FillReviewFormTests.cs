using System;
using System.Collections.Generic;
using System.Linq;
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
                        new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "5", Code = "1.1", CatalogMatched = true },
                        new TableFillRow { Category = TableFillCategory.Bridge, Name = "桥架", Quantity = "2", Code = "2.1", CatalogMatched = true },
                        new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座", Quantity = "1", Code = "8.1", CatalogMatched = true }
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
                        Assert.Equal("C1", FindByName<TextBox>(form,
                            "OriginalCableModel").Text);
                        Assert.Equal("C1", FindByName<TextBox>(form,
                            "BoqCableModel").Text);
                        Assert.NotNull(FindButton(form, "从固定清单添加"));
                        Assert.NotNull(FindButton(form, "替换为固定清单"));
                        Assert.True(grid.Columns["Name"].ReadOnly);
                        Assert.True(grid.Columns["Description"].ReadOnly);
                        Assert.True(grid.Columns["Unit"].ReadOnly);
                        Assert.True(grid.Columns["Code"].ReadOnly);
                        Button removeItem = FindButton(form, "删除选中项");
                        Assert.NotNull(removeItem);
                        Assert.True(removeItem.Enabled);
                        foreach (DataGridViewRow row in grid.Rows)
                            Assert.True(Convert.ToBoolean(row.Cells["Included"].Value));
                        grid.Rows[2].Cells["Included"].Value = false;
                        Application.DoEvents();
                        Assert.False(form.Data.Items[2].Included);
                        grid.CurrentCell = grid.Rows[2].Cells["Name"];
                        removeItem.PerformClick();
                        Assert.Equal(2, grid.Rows.Count);
                        Assert.Equal(2, form.Data.Items.Count);
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
        public void Dialog_UncheckedOutletImmediatelyConfirmedIsNotSelected()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "5", Code = "1.1", CatalogMatched = true },
                        new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座", Quantity = "1", Code = "8.1", CatalogMatched = true }
                    };
                    using (var form = new FillReviewForm(FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "C1" }, rows)))
                    {
                        form.Show();
                        Application.DoEvents();
                        DataGridView grid = Find<DataGridView>(form);
                        DataGridViewCell outletCheck = grid.Rows[1].Cells["Included"];
                        grid.CurrentCell = outletCheck;
                        outletCheck.Value = false;
                        grid.NotifyCurrentCellDirty(true);

                        // 模拟用户取消勾选后不切换单元格，直接确认填充。
                        FindButton(form, "确认填充").PerformClick();
                        Assert.Equal(DialogResult.OK, form.DialogResult);
                        Assert.DoesNotContain(form.Data.SelectedRows(), row =>
                            row.Category == TableFillCategory.Outlet || row.Name == "插座");
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
                            Category = "软管", Code = "3.6", Name = "25mm软管",
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
        public void Dialog_UnmatchedConduitsAreDisabledUntilCatalogReplacement()
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
                        Label warning = FindLabelContaining(form, "找不到对应型号");

                        Assert.Equal(1, tabs.SelectedIndex);
                        Assert.NotNull(warning);
                        Assert.Contains("32", warning.Text);
                        Assert.False((bool)grid.Rows[0].Cells["Included"].Value);
                        Assert.False((bool)grid.Rows[1].Cells["Included"].Value);

                        Assert.True(grid.Rows[0].Cells["Included"].ReadOnly);
                        Assert.True(grid.Rows[1].Cells["Included"].ReadOnly);
                        grid.Rows[0].Cells["Included"].Value = true;
                        Application.DoEvents();
                        Assert.Empty(form.Data.SelectedRows());
                        Assert.True(FindButton(form, "替换为固定清单").Enabled);
                        Assert.True(FindButton(form, "选择未匹配项").Enabled);
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
        public void Dialog_SelectUnmatchedCableOpensCableCatalogTab()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new[]
                    {
                        new TableFillRow
                        {
                            Category = TableFillCategory.Cable, Name = "电缆",
                            Description = "1.名称:44*2+21", Quantity = "10",
                            CatalogMatched = false
                        }
                    };
                    var catalog = new[]
                    {
                        new ListItem
                        {
                            Category = "电缆", Code = "1.1", Name = "多芯电缆",
                            Feature = "电缆模板", Unit = "m", Alias = "3*2.5"
                        },
                        new ListItem
                        {
                            Category = "桥架", Code = "2.1", Name = "桥架",
                            Feature = "桥架模板", Unit = "m", Alias = "200*100"
                        }
                    };
                    using (var form = new FillReviewForm(FillReviewData.Create(
                        new MachineRow
                        {
                            MachineId = "M1", CircuitName = "设备A", Cable = "44*2+21"
                        }, rows), catalog.ToList()))
                    {
                        Exception pickerFailure = null;
                        bool pickerOpened = false;
                        var timer = new System.Windows.Forms.Timer { Interval = 30 };
                        timer.Tick += (sender, args) =>
                        {
                            ManualListItemForm picker = Application.OpenForms
                                .OfType<ManualListItemForm>().FirstOrDefault();
                            if (picker == null) return;
                            timer.Stop();
                            try
                            {
                                pickerOpened = true;
                                Assert.Equal("电缆", picker.SelectedCategory);
                                ListView list = Find<ListView>(picker);
                                ListViewItem row = Assert.Single(list.Items.Cast<ListViewItem>());
                                Assert.Equal("1.1", ((ListItem)row.Tag).Code);
                            }
                            catch (Exception ex) { pickerFailure = ex; }
                            finally
                            {
                                picker.DialogResult = DialogResult.Cancel;
                                picker.Close();
                            }
                        };
                        form.Shown += (sender, args) =>
                        {
                            timer.Start();
                            FindButton(form, "选择未匹配项").PerformClick();
                            timer.Dispose();
                            Assert.True(pickerOpened);
                            if (pickerFailure != null) throw pickerFailure;
                            form.Close();
                        };
                        form.ShowDialog();
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

        private static T FindByName<T>(Control root, string name) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                if (child is T match && child.Name == name) return match;
                T nested = FindByName<T>(child, name);
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
