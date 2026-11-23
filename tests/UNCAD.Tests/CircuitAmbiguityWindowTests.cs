using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using UNCAD.Core.Excel;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-056 同名回路选择窗：结构、候选数据、确认/取消与双击路径。
    /// 无头环境用 STA 线程驱动（与其它 WPF 窗体测试同模式）。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class CircuitAmbiguityWindowTests
    {
        private static List<MachineRow> Candidates()
        {
            return new List<MachineRow>
            {
                new MachineRow { MachineId = "M01", CircuitName = "ECS",
                    Fr = "3N-11B1A-PP-02", FacilitySwitch = "3P50A", Cable = "R-4*10" },
                new MachineRow { MachineId = "M01", CircuitName = "ECS",
                    Fr = "3N-31D2A-PP-03", FacilitySwitch = "3P63A", Cable = "R-4*16" }
            };
        }

        [Fact]
        public void Window_ShowsCandidatesAndDefaultsToFirstRow()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new CircuitAmbiguityWindow("M01", "ECS", Candidates());
                    window.Show();
                    Pump(5);

                    Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                        "Fluent 主题字典必须按窗口合并");
                    Assert.Equal(2, window.CandidateGrid.Items.Count);
                    Assert.Equal(7, window.CandidateGrid.Columns.Count);
                    Assert.Equal(0, window.CandidateGrid.SelectedIndex);
                    // 详情条默认显示第一行全字段。
                    Assert.Contains("3N-11B1A-PP-02", window.DetailText.Text);
                    Assert.Contains("厂务开关", window.DetailText.Text);
                    Assert.False(window.Confirmed);
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
        public void Confirm_ReturnsSelectedRowAndConfirmedFlag()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new CircuitAmbiguityWindow("M01", "ECS", Candidates());
                    window.Show();
                    Pump(3);
                    window.CandidateGrid.SelectedIndex = 1;
                    Pump(1);
                    window.ConfirmButton.RaiseEvent(new RoutedEventArgs(
                        ButtonBase.ClickEvent));
                    Pump(1);

                    Assert.True(window.Confirmed);
                    Assert.NotNull(window.SelectedRow);
                    Assert.Equal("3N-31D2A-PP-03", window.SelectedRow.Fr);
                    Assert.Equal("R-4*16", window.SelectedRow.Cable);
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
        public void Cancel_LeavesNoSelection()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new CircuitAmbiguityWindow("M01", "ECS", Candidates());
                    window.Show();
                    Pump(3);
                    window.CancelButton.RaiseEvent(new RoutedEventArgs(
                        ButtonBase.ClickEvent));
                    Pump(1);

                    Assert.False(window.Confirmed);
                    Assert.Null(window.SelectedRow);
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
        public void Constructor_RejectsEmptyCandidates()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.Throws<ArgumentException>(
                        () => new CircuitAmbiguityWindow("M01", "ECS",
                            new List<MachineRow>()));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static void Pump(int rounds)
        {
            for (int index = 0; index < rounds; index++)
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
