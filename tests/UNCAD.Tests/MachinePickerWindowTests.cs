using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UNCAD.Core.Excel;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 机台选择窗（D-038，P2 模式）：前缀优先相似建议（最多 8 个）、
    /// 精确命中即提交、回路列表/预览联动、无选择确认被拦。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class MachinePickerWindowTests
    {
        private static List<MachineRow> Rows()
            => new List<MachineRow>
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

        private static MachinePickerWindow Window(
            Func<string, List<MachineRow>> lookup = null,
            Func<MachineRow, string> preview = null,
            IList<string> machineIds = null)
            => new MachinePickerWindow(
                machineIds ?? new[] { "M100", "M101", "M102", "M103", "M104",
                    "M105", "M106", "M107", "M108", "M109" },
                lookup ?? (id => new List<MachineRow>()),
                preview);

        [Fact]
        public void Suggestions_PrefixFirstMaxEight_CommitOnExactMatch()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = Window(
                        id => string.Equals(id, "M100",
                            StringComparison.OrdinalIgnoreCase)
                                ? Rows() : new List<MachineRow>(),
                        row => row.CircuitName + " / " + row.Cable);
                    window.Show();
                    Pump(3);

                    window.MachineInput.Text = "M";
                    Pump(2);
                    Assert.Equal(System.Windows.Visibility.Visible,
                        window.MachineSuggest.Visibility);
                    Assert.Equal(8, window.MachineSuggest.Items.Count);
                    Assert.StartsWith("M100",
                        Convert.ToString(window.MachineSuggest.Items[0]));

                    // 精确命中 → 立即提交：回路加载、首行选中、确定可用。
                    window.MachineInput.Text = "M100";
                    Pump(2);
                    Assert.Equal(System.Windows.Visibility.Collapsed,
                        window.MachineSuggest.Visibility);
                    Assert.Equal(2, window.CircuitGrid.Items.Count);
                    Assert.Contains("共 2 个回路", window.MachineSummary.Text);
                    Assert.NotNull(window.Selected);
                    Assert.True(window.ConfirmButton.IsEnabled);
                    Assert.Equal("设备A / ZB-YJVR-3*2.5", window.PreviewBox.Text);
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
        public void UnknownMachine_ShowsNotFoundSummary_AndKeepsConfirmDisabled()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = Window();
                    window.Show();
                    Pump(3);

                    // 与旧窗体一致：输入不匹配文本不触发查找；回车提交后才提示未找到。
                    window.MachineInput.Text = "ZZZ9";
                    Pump(2);
                    Assert.Equal("请输入机台 ID", window.MachineSummary.Text);
                    window.CommitMachineForTest("ZZZ9");
                    Pump(2);
                    Assert.Contains("未找到机台 ZZZ9", window.MachineSummary.Text);
                    Assert.Empty(window.CircuitGrid.Items);
                    Assert.False(window.ConfirmButton.IsEnabled);
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
        public void CircuitSelection_UpdatesPreviewAndSelected()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = Window(
                        id => string.Equals(id, "M100",
                            StringComparison.OrdinalIgnoreCase)
                            ? Rows() : new List<MachineRow>(),
                        row => row.CircuitName + " / " + row.Cable);
                    window.Show();
                    Pump(3);

                    window.CommitMachineForTest("M100");
                    Pump(2);
                    window.CircuitGrid.SelectedIndex = 1;
                    Pump(2);
                    Assert.Equal("设备B", window.Selected.CircuitName);
                    Assert.Equal("设备B / ZB-YJVR-5*6", window.PreviewBox.Text);
                    Assert.Contains("N480 3P4W 3P100A", window.CircuitDetailBox.Text);
                    Assert.True(window.ConfirmButton.IsEnabled);

                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.True(window.Confirmed);
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
        public void Confirm_WithoutSelection_ShowsWarningInsteadOfClosing()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = Window();
                    window.Show();
                    Pump(3);

                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.False(window.Confirmed);
                    Assert.True(window.WarningBar.IsOpen);
                    Assert.Contains("请先在列表中选择", window.WarningBar.Message);
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
        public void EmptyMachineId_ResetsToPrompt()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = Window();
                    window.Show();
                    Pump(3);

                    window.MachineInput.Text = "M10";
                    Pump(2);
                    window.MachineInput.Clear();
                    Pump(2);
                    Assert.Equal("请输入机台 ID", window.MachineSummary.Text);
                    Assert.Equal(System.Windows.Visibility.Collapsed,
                        window.MachineSuggest.Visibility);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static void Click(System.Windows.Controls.Primitives.ButtonBase button)
        {
            Assert.NotNull(button);
            button.RaiseEvent(new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                System.Threading.Thread.Sleep(30);
            }
        }
    }

    internal static class MachinePickerWindowTestExtensions
    {
        /// <summary>测试辅助：直接设置机台输入并提交（等价于用户回车选中）。</summary>
        internal static void CommitMachineForTest(this MachinePickerWindow window,
            string machineId)
        {
            window.MachineInput.Text = machineId;
            window.CommitMachine();
        }
    }
}
