using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public class ManualListItemFormTests
    {
        [Fact]
        public void Dialog_CapturesSocketQuantityWithBoundedNumericInput()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ManualListItemForm())
                    {
                        form.Shown += (sender, args) =>
                        {
                            Find<TextBox>(form, "ManualName").Text = "插座";
                            Find<TextBox>(form, "ManualDescription").Text = "普通五孔插座";
                            Find<TextBox>(form, "ManualUnit").Text = "个";
                            Find<NumericUpDown>(form, "ManualQuantity").Value = 2;
                            Find<TextBox>(form, "ManualCode").Text = "8.9";
                            All<Button>(form).Single(button => button.Text == "添加")
                                .PerformClick();
                        };
                        Assert.Equal(DialogResult.OK, form.ShowDialog());
                        Assert.Equal("插座", form.ItemName);
                        Assert.Equal("普通五孔插座", form.Description);
                        Assert.Equal("个", form.Unit);
                        Assert.Equal("2", form.Quantity);
                        Assert.Equal("8.9", form.Code);
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
