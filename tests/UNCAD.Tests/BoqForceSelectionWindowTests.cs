using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1U 强制 BOQ 选择窗（D-048）：缺回路机台默认全部不勾选（跳过其 BOQ）、
    /// 勾选并确定 = 强制生成、确定允许空选择（= 全部跳过）、取消 = 不强制。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class BoqForceSelectionWindowTests
    {
        [Fact]
        public void Defaults_AllUnchecked_AndShowsCoverageAndMissing()
        {
            RunSta(() =>
            {
                var window = new BoqForceSelectionWindow(Choices());
                window.Show();
                Pump(2);

                Assert.Contains("有 2 台机台回路不全", window.HeaderText.Text);
                Assert.All(window.Choices, choice => Assert.False(choice.Force,
                    "缺回路机台默认不强制生成"));
                Assert.Equal("2 / 5", window.Choices[0].CoverageText);
                Assert.Contains("电脑插座3", window.Choices[0].MissingText);
                Assert.Equal("—", window.Choices[1].MissingText);

                // 不勾选直接确定 = 全部跳过（合法选择）。
                Click(window.ConfirmButton);
                Pump(2);
                Assert.True(window.Confirmed);
                Assert.Empty(window.ForcedMachineIds);
                window.Close();
            });
        }

        [Fact]
        public void Confirm_PublishesForcedMachineIds()
        {
            RunSta(() =>
            {
                var window = new BoqForceSelectionWindow(Choices());
                window.Show();
                Pump(2);

                window.Choices[0].Force = true;
                Click(window.ConfirmButton);
                Pump(2);

                Assert.True(window.Confirmed);
                Assert.Equal(new[] { "MDAPT01" }, window.ForcedMachineIds.ToArray());
                window.Close();
            });
        }

        [Fact]
        public void Cancel_MeansNoForce()
        {
            RunSta(() =>
            {
                var window = new BoqForceSelectionWindow(Choices());
                window.Show();
                Pump(2);

                window.Choices[1].Force = true;
                Click(window.CancelButton);
                Pump(2);

                Assert.False(window.Confirmed);
                window.Close();
            });
        }

        [Fact]
        public void MissingText_FoldsLongLists()
        {
            var choice = new BoqForceMachineChoice
            {
                MachineId = "M1",
                MissingNames = new List<string>
                    { "A", "B", "C", "D", "E", "F", "G" }
            };
            Assert.Contains("A、B、C、D、E、F", choice.MissingText);
            Assert.Contains("等 7 个", choice.MissingText);
        }

        private static List<BoqForceMachineChoice> Choices() => new List<BoqForceMachineChoice>
        {
            new BoqForceMachineChoice
            {
                MachineId = "MDAPT01",
                SelectedCircuitCount = 2,
                ExpectedCircuitCount = 5,
                MissingNames = new List<string> { "电脑插座3", "电脑插座4", "电脑插座5" }
            },
            new BoqForceMachineChoice
            {
                MachineId = "MDAPT02",
                SelectedCircuitCount = 1,
                ExpectedCircuitCount = 2,
                MissingNames = new List<string>()
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
