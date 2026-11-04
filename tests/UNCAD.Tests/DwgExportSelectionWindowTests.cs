using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1DWG 导出机台选择窗（D-047）：完整机台默认勾选、缺回路默认不勾选、
    /// 勾选强制导出、空选择不得确认、取消不出文件。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class DwgExportSelectionWindowTests
    {
        [Fact]
        public void Defaults_CompleteChecked_IncompleteUnchecked()
        {
            RunSta(() =>
            {
                var window = new DwgExportSelectionWindow(Choices());
                window.Show();
                Pump(2);

                Assert.Contains("有 1 台机台回路不全", window.HeaderText.Text);
                Assert.Equal(2, window.Choices.Count);

                DwgExportMachineChoice complete = window.Choices.First(c => c.Complete);
                DwgExportMachineChoice incomplete = window.Choices.First(c => !c.Complete);
                Assert.True(complete.Export, "完整机台默认导出");
                Assert.False(incomplete.Export, "缺回路机台默认跳过");
                Assert.Equal("回路齐全", complete.StatusText);
                Assert.Equal("缺回路", incomplete.StatusText);
                Assert.Contains("电脑插座3", incomplete.MissingText);
                Assert.Equal("—", complete.MissingText);

                // 默认只选完整机台。
                Assert.Equal(new[] { "MDAPT01" },
                    window.ComputeSelectedMachineIds().ToArray());
                window.Close();
            });
        }

        [Fact]
        public void ForceCheck_IncompleteMachine_IsIncludedOnConfirm()
        {
            RunSta(() =>
            {
                var window = new DwgExportSelectionWindow(Choices());
                window.Show();
                Pump(2);

                DwgExportMachineChoice incomplete = window.Choices.First(c => !c.Complete);
                incomplete.Export = true;
                Click(window.ExportButton);
                Pump(2);

                Assert.True(window.Confirmed);
                Assert.Equal(new[] { "MDAPT01", "MDAPT02" },
                    window.SelectedMachineIds.OrderBy(id => id).ToArray());
                window.Close();
            });
        }

        [Fact]
        public void Confirm_BlocksEmptySelectionAndCancelMeansNoFiles()
        {
            RunSta(() =>
            {
                var window = new DwgExportSelectionWindow(Choices());
                window.Show();
                Pump(2);

                foreach (DwgExportMachineChoice choice in window.Choices)
                    choice.Export = false;
                Click(window.ExportButton);
                Pump(2);
                Assert.False(window.Confirmed, "空选择不得确认");
                Assert.True(window.WarningBar.IsOpen);
                Assert.Contains("至少勾选一个机台", window.WarningBar.Message,
                    StringComparison.Ordinal);

                Click(window.CancelButton);
                Pump(2);
                Assert.False(window.Confirmed, "取消 = 本次不出任何文件");
                window.Close();
            });
        }

        [Fact]
        public void MissingText_FoldsLongLists()
        {
            var choice = new DwgExportMachineChoice
            {
                MachineId = "M1",
                Complete = false,
                MissingNames = new List<string>
                    { "A", "B", "C", "D", "E", "F", "G", "H" }
            };
            Assert.Contains("A、B、C、D、E、F", choice.MissingText);
            Assert.Contains("等 8 个", choice.MissingText);
            var shortList = new DwgExportMachineChoice
            {
                MachineId = "M2",
                MissingNames = new List<string> { "A" }
            };
            Assert.Equal("A", shortList.MissingText);
        }

        private static List<DwgExportMachineChoice> Choices() => new List<DwgExportMachineChoice>
        {
            new DwgExportMachineChoice
            {
                MachineId = "MDAPT01",
                Complete = true,
                CircuitCount = 3,
                Export = true
            },
            new DwgExportMachineChoice
            {
                MachineId = "MDAPT02",
                Complete = false,
                CircuitCount = 2,
                MissingNames = new List<string> { "电脑插座3", "电脑插座4" },
                Export = false
            }
        };

        private static void RunSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(60000);
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
                Thread.Sleep(30);
            }
        }
    }
}
