using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 批量 U1U 设备内预留窗：一行一框逐个填写（不同设备预留不同），
    /// 统一值只是快捷填充；非法输入必须挡在确认之前。
    /// </summary>
    public class DeviceReserveFormTests
    {
        private static List<DeviceReserveRow> Rows(params string[] machines)
            => machines.Select(machine => new DeviceReserveRow(machine,
                "设备" + machine)).ToList();

        [Fact]
        public void Dialog_ShowsOneEditableRowPerFrame()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new DeviceReserveForm(
                        Rows("M1", "M2", "M3")))
                    {
                        form.Show();
                        Application.DoEvents();
                        DataGridView grid = Find<DataGridView>(form);
                        Assert.NotNull(grid);
                        Assert.Equal(3, grid.Rows.Count);
                        Assert.Equal("M1", grid.Rows[0].Cells[1].Value);
                        Assert.Equal("设备M3", grid.Rows[2].Cells[2].Value);
                        Assert.False(grid.Columns[grid.Columns.Count - 1].ReadOnly);
                        Assert.Equal("0", grid.Rows[0].Cells[
                            grid.Columns.Count - 1].Value);
                        Button confirm = FindButton(form, "确定");
                        Assert.Same(confirm, form.AcceptButton);
                        Assert.NotNull(FindButton(form, "应用到全部"));
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

        [Fact]
        public void ApplyAll_FillsEveryRowAndKeepsPerRowValues()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new DeviceReserveForm(
                        Rows("M1", "M2", "M3")))
                    {
                        form.Show();
                        Application.DoEvents();
                        DataGridView grid = Find<DataGridView>(form);
                        int reserve = grid.Columns.Count - 1;
                        Find<TextBox>(form).Text = "5000";
                        FindButton(form, "应用到全部").PerformClick();
                        Application.DoEvents();
                        for (int i = 0; i < 3; i++)
                        {
                            Assert.Equal("5000", grid.Rows[i].Cells[reserve].Value);
                            Assert.Equal(5000d, form.ReserveMillimeters(i));
                        }

                        // 单独把第二行改成 1200：确认后必须按行取值。
                        EditCell(grid, 1, reserve, "1200");
                        FindButton(form, "确定").PerformClick();
                        Application.DoEvents();
                        Assert.Equal(DialogResult.OK, form.DialogResult);
                        Assert.Equal(5000d, form.ReserveMillimeters(0));
                        Assert.Equal(1200d, form.ReserveMillimeters(1));
                        Assert.Equal(5000d, form.ReserveMillimeters(2));
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

        [Fact]
        public void InvalidInput_BlocksConfirmUntilFixed()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new DeviceReserveForm(Rows("M1", "M2")))
                    {
                        form.Show();
                        Application.DoEvents();
                        DataGridView grid = Find<DataGridView>(form);
                        int reserve = grid.Columns.Count - 1;
                        EditCell(grid, 0, reserve, "abc");
                        FindButton(form, "确定").PerformClick();
                        Application.DoEvents();
                        // 非法输入必须挡在确认关口：窗不关、行上红字定位。
                        Assert.NotEqual(DialogResult.OK, form.DialogResult);
                        Assert.Equal("必须是 0~100000 的毫米数。",
                            grid.Rows[0].ErrorText);

                        EditCell(grid, 0, reserve, "3000");
                        FindButton(form, "确定").PerformClick();
                        Application.DoEvents();
                        Assert.Equal(DialogResult.OK, form.DialogResult);
                        Assert.Equal(3000d, form.ReserveMillimeters(0));
                        Assert.Equal(0d, form.ReserveMillimeters(1));
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

        [Theory]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("0", null)]
        [InlineData("3400", null)]
        [InlineData("100000", null)]
        [InlineData("-1", "必须是 0~100000 的毫米数。")]
        [InlineData("100001", "必须是 0~100000 的毫米数。")]
        [InlineData("abc", "必须是 0~100000 的毫米数。")]
        public void ReserveError_FollowsFieldRange(string input, string expected)
        {
            Assert.Equal(expected, DeviceReserveForm.ReserveError(input));
        }

        private static void EditCell(DataGridView grid, int row, int column,
            string text)
        {
            grid.EndEdit();
            if (grid.CurrentCell == null
                || grid.CurrentCell.RowIndex != row
                || grid.CurrentCell.ColumnIndex != column)
                grid.CurrentCell = grid.Rows[row].Cells[column];
            if (!grid.BeginEdit(true) && !grid.IsCurrentCellInEditMode)
                Assert.Fail("单元格无法进入编辑状态");
            var editor = grid.EditingControl as DataGridViewTextBoxEditingControl;
            Assert.NotNull(editor);
            editor.Text = text;
            // 程序化改文字不触发用户的脏标记：必须显式声明 dirty，
            // 否则 EndEdit 不走 CellValidating，和真实键入路径不一致。
            grid.NotifyCurrentCellDirty(true);
            grid.EndEdit();
            Application.DoEvents();
        }

        private static T Find<T>(Control root) where T : Control
            => FindAll<T>(root).First();

        private static IEnumerable<T> FindAll<T>(Control root)
            where T : Control
        {
            foreach (Control child in root.Controls)
            {
                if (child is T match) yield return match;
                foreach (T nested in FindAll<T>(child)) yield return nested;
            }
        }

        private static Button FindButton(Control root, string text)
            => FindAll<Button>(root).First(button => button.Text == text);
    }
}
