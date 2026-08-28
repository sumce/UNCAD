using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public class ManualListItemFormTests
    {
        private static ListItem[] Catalog() => new[]
        {
            new ListItem
            {
                Category = "插座", Code = "8.9", Name = "插座",
                Feature = "普通五孔插座", Unit = "个", Alias = "五孔"
            },
            new ListItem
            {
                Category = "桥架", Code = "2.6", Name = "桥架",
                Feature = "200*100桥架", Unit = "m", Alias = "200*100"
            },
            new ListItem
            {
                Category = "电缆", Code = "1.1", Name = "多芯电缆 XLPE",
                Feature = "电缆清单模板", Unit = "m", Alias = "3*2.5"
            }
        };

        [Fact]
        public void Dialog_SelectsDatabaseSocketAndCapturesOnlyQuantity()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ManualListItemForm(Catalog()))
                    {
                        form.Shown += (sender, args) =>
                        {
                            Find<TextBox>(form, "CatalogSearch").Text = "五孔";
                            Find<NumericUpDown>(form, "CatalogQuantity").Value = 2;
                            Application.DoEvents();
                            ListView list = Find<ListView>(form, "CatalogItems");
                            Assert.Single(list.Items.Cast<ListViewItem>());
                            list.Items[0].Selected = true;
                            All<Button>(form).Single(button =>
                                button.Text == "添加所选清单").PerformClick();
                        };

                        Assert.Equal(DialogResult.OK, form.ShowDialog());
                        Assert.Equal("8.9", form.SelectedItem.Code);
                        Assert.Equal("插座", form.SelectedItem.Name);
                        Assert.Equal("普通五孔插座", form.SelectedItem.Feature);
                        Assert.Equal("个", form.SelectedItem.Unit);
                        Assert.Equal("2", form.Quantity);
                        Assert.DoesNotContain(All<TextBox>(form), control =>
                            control.Name == "ManualName" || control.Name == "ManualCode");
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
        public void Dialog_ContinuousAddFiresPickAndKeepsDialogOpen()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var picked = new List<ListItem>();
                    using (var form = new ManualListItemForm(Catalog()))
                    {
                        form.PickRequested += (item, quantity) =>
                        {
                            picked.Add(item);
                            Assert.Equal("2", quantity);
                        };
                        form.Shown += (sender, args) =>
                        {
                            Assert.NotNull(All<Button>(form).FirstOrDefault(button =>
                                button.Text == "添加并继续"));
                            Find<TextBox>(form, "CatalogSearch").Text = "五孔";
                            Find<NumericUpDown>(form, "CatalogQuantity").Value = 2;
                            Application.DoEvents();
                            ListView list = Find<ListView>(form, "CatalogItems");
                            list.Items[0].Selected = true;
                            // 添加并继续：不关闭对话框，触发一次 PickRequested。
                            All<Button>(form).Single(button =>
                                button.Text == "添加并继续").PerformClick();
                            Application.DoEvents();
                            Assert.Single(picked);
                            // 对话框仍开着：还能再点“添加所选清单”正常关闭。
                            All<Button>(form).Single(button =>
                                button.Text == "添加所选清单").PerformClick();
                        };

                        Assert.Equal(DialogResult.OK, form.ShowDialog());
                        Assert.Single(picked);
                        Assert.Equal("8.9", picked[0].Code);
                        Assert.Equal("8.9", form.SelectedItem.Code);
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
        public void Dialog_TokenizedSearchRequiresEveryKeyword()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ManualListItemForm(Catalog()))
                    {
                        form.Shown += (sender, args) =>
                        {
                            TextBox search = Find<TextBox>(form, "CatalogSearch");
                            ListView list = Find<ListView>(form, "CatalogItems");

                            search.Text = "插座 五孔";
                            Application.DoEvents();
                            var socketOnly = list.Items.Cast<ListViewItem>().ToList();
                            Assert.Single(socketOnly);
                            Assert.Equal("8.9", ((ListItem)socketOnly[0].Tag).Code);

                            search.Text = "200 桥架";
                            Application.DoEvents();
                            var bridgeOnly = list.Items.Cast<ListViewItem>().ToList();
                            Assert.Single(bridgeOnly);
                            Assert.Equal("2.6", ((ListItem)bridgeOnly[0].Tag).Code);

                            // 两个关键词不能同时在两类上命中 → 空结果。
                            search.Text = "五孔 桥架";
                            Application.DoEvents();
                            Assert.Empty(list.Items.Cast<ListViewItem>());

                            All<Button>(form).Single(button =>
                                button.Text == "取消").PerformClick();
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

        [Fact]
        public void Dialog_RecentCategoryShowsOnlyPickedItems()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ManualListItemForm(Catalog()))
                    {
                        form.Shown += (sender, args) =>
                        {
                            Find<TextBox>(form, "CatalogSearch").Text = "五孔";
                            Application.DoEvents();
                            ListView list = Find<ListView>(form, "CatalogItems");
                            list.Items[0].Selected = true;
                            All<Button>(form).Single(button =>
                                button.Text == "添加并继续").PerformClick();
                            Application.DoEvents();

                            TabControl tabs = Find<TabControl>(form, "CatalogTabs");
                            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page =>
                                Convert.ToString(page.Tag) == "最近使用");
                            Application.DoEvents();

                            var recent = list.Items.Cast<ListViewItem>()
                                .Select(row => ((ListItem)row.Tag).Code).ToList();
                            Assert.Contains("8.9", recent);

                            All<Button>(form).Single(button =>
                                button.Text == "取消").PerformClick();
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

        [Fact]
        public void Dialog_ReplacementOpensRequestedCableTabAndFiltersRows()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ManualListItemForm(Catalog(), true, "电缆"))
                    {
                        form.Shown += (sender, args) =>
                        {
                            TabControl tabs = Find<TabControl>(form, "CatalogTabs");
                            Assert.DoesNotContain(tabs.TabPages.Cast<TabPage>(),
                                page => page.Text == "桥架");
                            Assert.Contains(tabs.TabPages.Cast<TabPage>(), page => page.Text == "电缆");
                            Assert.Equal("电缆", form.SelectedCategory);

                            ListView list = Find<ListView>(form, "CatalogItems");
                            ListViewItem row = Assert.Single(list.Items.Cast<ListViewItem>());
                            Assert.Equal("1.1", ((ListItem)row.Tag).Code);
                            All<Button>(form).Single(button => button.Text == "取消").PerformClick();
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

        private static T Find<T>(Control root, string name) where T : Control
            => All<T>(root).Single(control => control.Name == name);

        private static List<T> All<T>(Control root)
            where T : Control
        {
            var result = new List<T>();
            foreach (Control child in root.Controls)
            {
                if (child is T match) result.Add(match);
                result.AddRange(All<T>(child));
            }
            return result;
        }
    }
}
