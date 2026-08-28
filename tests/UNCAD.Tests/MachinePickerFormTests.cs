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
    public class MachinePickerFormTests
    {
        [Fact]
        public void Dialog_UsesDpiLayoutResponsiveColumnsAndContinuousSelectionFlow()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<MachineRow>
                    {
                        new MachineRow
                        {
                            Region = "LAB", MachineId = "M100", CircuitName = "设备A",
                            Cable = "ZB-YJVR-3*2.5", Dia = "25", Next = "插座盘",
                            Detail = "U220 1P3W 1P20A"
                        },
                        new MachineRow
                        {
                            Region = "LAB", MachineId = "M100", CircuitName = "设备B",
                            Cable = "ZB-YJVR-5*6", Dia = "32", Next = "I-Line盘",
                            Detail = "N480 3P4W 3P100A"
                        }
                    };
                    using (var form = new MachinePickerForm(new[] { "M100" },
                        id => string.Equals(id, "M100", StringComparison.OrdinalIgnoreCase)
                            ? rows : new List<MachineRow>(),
                        row => row.CircuitName + " / " + row.Cable))
                    {
                        form.Show();
                        form.Size = form.MinimumSize;
                        Application.DoEvents();

                        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                        Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
                        GroupBox machineGroup = FindAll<GroupBox>(form)
                            .Single(group => group.Text == "机台 ID");
                        TextBox machine = Find<TextBox>(machineGroup);
                        machine.Text = "M100";
                        Application.DoEvents();

                        ListView circuits = Find<ListView>(form);
                        Assert.Equal(2, circuits.Items.Count);
                        int totalWidth = circuits.Columns.Cast<ColumnHeader>()
                            .Sum(column => column.Width);
                        Assert.InRange(totalWidth, circuits.ClientSize.Width - 20,
                            circuits.ClientSize.Width + 20);
                        circuits.Items[1].Selected = true;
                        Application.DoEvents();
                        Assert.Equal("设备B", form.Selected.CircuitName);

                        foreach (Button button in FindAll<Button>(form).Where(b => b.Visible))
                            Assert.True(TextRenderer.MeasureText(button.Text, button.Font).Width
                                <= button.ClientSize.Width - 8);
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
            => FindAll<T>(root).FirstOrDefault();

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
