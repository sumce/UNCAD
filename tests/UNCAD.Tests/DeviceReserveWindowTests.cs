using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 设备内预留窗（D-038，P3 模式）：逐行输入、统一值快捷填充、
    /// 权威校验在“确定”按钮（无头环境不依赖任何单元格事件）。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class DeviceReserveWindowTests
    {
        [Fact]
        public void Window_ShowsOneEditableRowPerFrame()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = Enumerable.Range(1, 6).Select(index =>
                        new DeviceReserveRow("M-" + index, "设备" + index)).ToList();
                    var window = new DeviceReserveWindow(rows);
                    window.Show();
                    Pump(4);

                    Assert.Equal(6, window.ReserveGrid.Items.Count);
                    Assert.Equal(4, window.ReserveGrid.Columns.Count);
                    // 只有“设备内预留”列可编辑，其余三列只读。
                    Assert.Equal(3, window.ReserveGrid.Columns.Count(c => c.IsReadOnly));
                    Assert.Equal(1, window.ReserveGrid.Columns.Count(c => !c.IsReadOnly));
                    Assert.All(rows, row => Assert.Equal("0", row.ReserveMm));
                    Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                        "Fluent 主题必须按窗口合并");
                    Assert.True(window.ConfirmButton.IsDefault);
                    Assert.True(window.CancelButton.IsCancel);
                    window.Close();
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
                    var rows = new List<DeviceReserveRow>
                    {
                        new DeviceReserveRow("M-1", "注塑机"),
                        new DeviceReserveRow("M-2", "模温机"),
                        new DeviceReserveRow("M-3", "组装线")
                    };
                    var window = new DeviceReserveWindow(rows);
                    window.Show();
                    Pump(3);

                    window.UniformBox.Text = "2000";
                    Click(window.ApplyAllButton);
                    Pump(2);
                    Assert.All(rows, row => Assert.Equal("2000", row.ReserveMm));

                    // 应用后仍可逐行覆盖。
                    rows[2].ReserveMm = "500";
                    Assert.Equal("2000", rows[0].ReserveMm);
                    Assert.Equal("500", rows[2].ReserveMm);
                    window.Close();
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
                    var rows = new List<DeviceReserveRow>
                    {
                        new DeviceReserveRow("M-1", "注塑机"),
                        new DeviceReserveRow("M-2", "模温机")
                    };
                    var window = new DeviceReserveWindow(rows);
                    window.Show();
                    Pump(3);

                    rows[1].ReserveMm = "abc";
                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.False(window.Confirmed, "坏值不得通过确认");
                    Assert.True(window.ErrorBar.IsOpen, "必须给出可见错误");
                    Assert.Contains("M-2", window.ErrorBar.Message);

                    rows[1].ReserveMm = "1500";
                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.True(window.Confirmed);
                    Assert.Equal(1500d, window.ReserveMillimeters(1));
                    Assert.Equal(0d, window.ReserveMillimeters(0));
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Theory]
        [InlineData("", null)]
        [InlineData("0", null)]
        [InlineData("2500.5", null)]
        [InlineData("100000", null)]
        [InlineData("-1", "必须是 0~100000 的毫米数。")]
        [InlineData("100001", "必须是 0~100000 的毫米数。")]
        [InlineData("abc", "必须是 0~100000 的毫米数。")]
        public void ReserveError_FollowsFieldRange(string input, string expected)
        {
            Assert.Equal(expected, DeviceReserveWindow.ReserveError(input));
        }

        private static void Click(System.Windows.Controls.Primitives.ButtonBase button)
        {
            Assert.NotNull(button);
            button.RaiseEvent(new RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }
    }
}
