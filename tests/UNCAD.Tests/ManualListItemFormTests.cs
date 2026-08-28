using System;
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
        [Fact]
        public void Dialog_SelectsDatabaseSocketAndCapturesOnlyQuantity()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var catalog = new[]
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
                        }
                    };
                    using (var form = new ManualListItemForm(catalog))
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

        private static T Find<T>(Control root, string name) where T : Control
            => All<T>(root).Single(control => control.Name == name);

        private static System.Collections.Generic.List<T> All<T>(Control root)
            where T : Control
        {
            var result = new System.Collections.Generic.List<T>();
            foreach (Control child in root.Controls)
            {
                if (child is T match) result.Add(match);
                result.AddRange(All<T>(child));
            }
            return result;
        }
    }
}
