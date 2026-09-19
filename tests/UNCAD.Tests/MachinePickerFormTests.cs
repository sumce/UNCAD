using System;
using System.Collections.Generic;
using System.Drawing;
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
        [Theory]
        [InlineData(96)]  // 100%
        [InlineData(77)]  // 125%
        [InlineData(64)] // 150%
        [InlineData(55)] // 175%
        [InlineData(48)] // 200%
        public void Dialog_AllSectionsAdaptWithoutOverlap(int designDpi)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new MachinePickerForm(new[] { "M100" },
                        id => new List<MachineRow>()))
                    {
                        form.AutoScaleDimensions = new SizeF(designDpi, designDpi);
                        form.Show();
                        Application.DoEvents();

                        Label machineLabel = FindAll<Label>(form)
                            .Single(label => label.Text == "机台 ID");
                        TableLayoutPanel machinePanel = Assert.IsType<TableLayoutPanel>(
                            machineLabel.Parent);
                        Label circuitLabel = FindAll<Label>(form)
                            .Single(label => label.Text == "设备 / 回路");
                        TableLayoutPanel circuitPanel = Assert.IsType<TableLayoutPanel>(
                            circuitLabel.Parent);
                        Label summary = FindAll<Label>(form)
                            .Single(label => label.Text == "请输入机台 ID");
                        TextBox machine = Find<TextBox>(machinePanel);
                        TabControl tabs = Find<TabControl>(form);
                        FlowLayoutPanel commandBar = Find<FlowLayoutPanel>(form);
                        ListView circuits = Find<ListView>(form);

                        Assert.True(machinePanel.AutoSize);
                        Assert.True(machinePanel.Bottom <= summary.Top);
                        Assert.True(machineLabel.Bottom <= machine.Top);
                        Assert.True(machine.Height >= machine.PreferredHeight);
                        Assert.InRange(machine.Top, 0, machinePanel.ClientSize.Height);
                        Assert.InRange(machine.Bottom, 0, machinePanel.ClientSize.Height);
                        Assert.True(summary.Height >= summary.PreferredSize.Height);
                        Assert.True(summary.Bottom <= circuitPanel.Top);
                        Assert.True(circuitPanel.Bottom <= tabs.Top);
                        Assert.True(tabs.Bottom <= commandBar.Top);
                        Assert.True(commandBar.Bottom <= form.ClientSize.Height);
                        Assert.True(circuits.ClientSize.Height > 0);
                        Assert.True(tabs.ClientSize.Height > 0);

                        int circuitHeight = circuitPanel.Height;
                        int detailHeight = tabs.Height;
                        form.ClientSize = new Size(form.ClientSize.Width + 160,
                            form.ClientSize.Height + 160);
                        Application.DoEvents();

                        Assert.True(circuitPanel.Height > circuitHeight);
                        Assert.True(tabs.Height > detailHeight);
                        Assert.True(circuitPanel.Bottom <= tabs.Top);
                        Assert.True(tabs.Bottom <= commandBar.Top);

                        form.MinimumSize = Size.Empty;
                        form.ClientSize = new Size(720, 520);
                        Application.DoEvents();

                        Assert.True(machine.Height >= machine.PreferredHeight);
                        Assert.True(machine.Bottom <= machinePanel.ClientSize.Height);
                        Assert.True(summary.Bottom <= circuitPanel.Top);
                        Assert.True(circuitPanel.Bottom <= tabs.Top);
                        Assert.True(tabs.Bottom <= commandBar.Top);
                        Assert.True(circuits.ClientSize.Height > 0);
                        Assert.True(tabs.ClientSize.Height > 0);
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
        public void Suggestions_UseTheirContentHeightAndReleaseSpaceWhenHidden()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new MachinePickerForm(
                        new[] { "M100", "M101", "M102" },
                        id => new List<MachineRow>()))
                    {
                        form.Show();
                        Application.DoEvents();

                        Label machineLabel = FindAll<Label>(form)
                            .Single(label => label.Text == "机台 ID");
                        TextBox machine = Find<TextBox>(machineLabel.Parent);
                        ListBox suggestions = Find<ListBox>(form);
                        Label circuitLabel = FindAll<Label>(form)
                            .Single(label => label.Text == "设备 / 回路");
                        Control circuitPanel = circuitLabel.Parent;

                        int circuitTopWithoutSuggestions = circuitPanel.Top;
                        machine.Text = "M";
                        Application.DoEvents();

                        Assert.True(suggestions.Visible);
                        Assert.Equal(3, suggestions.Items.Count);
                        Assert.Equal(suggestions.PreferredHeight, suggestions.Height);
                        Assert.True(circuitPanel.Top > circuitTopWithoutSuggestions);

                        machine.Clear();
                        Application.DoEvents();

                        Assert.False(suggestions.Visible);
                        Assert.Equal(circuitTopWithoutSuggestions, circuitPanel.Top);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Theory]
        [InlineData(780, 2f)]
        [InlineData(460, 1.5f)]
        public void CircuitColumns_FitEveryAvailableWidth(int availableWidth,
            float dpiScale)
        {
            int[] widths = MachinePickerForm.CalculateCircuitColumnWidths(
                availableWidth, dpiScale);

            Assert.Equal(availableWidth, widths.Sum());
            Assert.All(widths, width => Assert.True(width > 0));
        }

        [Fact]
        public void Dialog_UsesAdaptiveLayoutAndContinuousSelectionFlow()
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
                        Label machineLabel = FindAll<Label>(form)
                            .Single(label => label.Text == "机台 ID");
                        TableLayoutPanel machinePanel = Assert.IsType<TableLayoutPanel>(
                            machineLabel.Parent);
                        Label circuitLabel = FindAll<Label>(form)
                            .Single(label => label.Text == "设备 / 回路");
                        TableLayoutPanel circuitPanel = Assert.IsType<TableLayoutPanel>(
                            circuitLabel.Parent);
                        Label summary = FindAll<Label>(form)
                            .Single(label => label.Text == "请输入机台 ID");
                        TextBox machine = Find<TextBox>(machinePanel);
                        Button confirm = FindAll<Button>(form)
                            .Single(button => button.Text == "确定");
                        Assert.False(confirm.Enabled);
                        Assert.Equal(UiTheme.SurfaceAlt, confirm.BackColor);
                        Assert.Equal(UiTheme.TextDisabled, confirm.ForeColor);
                        Assert.True(summary.Bottom <= circuitPanel.Top);
                        Assert.True(machinePanel.Bottom <= summary.Top);
                        Assert.True(machine.Height >= machine.PreferredHeight);
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
                        Assert.True(confirm.Enabled);
                        Assert.Equal(UiTheme.Action, confirm.BackColor);
                        Assert.Equal(UiTheme.Surface, confirm.ForeColor);

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
