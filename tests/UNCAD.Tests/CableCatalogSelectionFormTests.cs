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
    public class CableCatalogSelectionFormTests
    {
        [Fact]
        public void Dialog_FiltersAndReturnsExplicitCatalogCable()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var candidates = new[]
                    {
                        Item("1.1", "3*2.5"),
                        Item("1.2", "5*6")
                    };
                    using (var form = new CableCatalogSelectionForm(
                        candidates, "UNKNOWN"))
                    {
                        form.Shown += (sender, args) =>
                        {
                            Find<TextBox>(form, "CableCatalogSearch").Text = "5*6";
                            Application.DoEvents();
                            ListView list = Find<ListView>(form, "CableCatalogList");
                            Assert.Single(list.Items.Cast<ListViewItem>());
                            list.Items[0].Selected = true;
                            Application.DoEvents();
                            All<Button>(form).Single(button =>
                                button.Text == "使用所选型号").PerformClick();
                        };

                        Assert.Equal(DialogResult.OK, form.ShowDialog());
                        Assert.Equal("1.2", form.SelectedItem.Code);
                        Assert.Equal("5*6", form.SelectedItem.Spec);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static ListItem Item(string code, string spec)
            => new ListItem
            {
                Code = code,
                Spec = spec,
                Name = "电缆" + spec,
                Feature = "模板" + spec,
                Unit = "m"
            };

        private static T Find<T>(Control root, string name) where T : Control
            => All<T>(root).Single(control => control.Name == name);

        private static List<T> All<T>(Control root) where T : Control
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
