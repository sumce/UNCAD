using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UNCAD.Core.Fill;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 清单更新对比窗（D-038，P2 模式）：左右并排清单、变化行着色、
    /// 多框左侧列表、“继续更新”才确认。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class FillUpdateCompareWindowTests
    {
        [Fact]
        public void MultipleFrames_ShowsFrameList_AndSelectsFirst()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new FillUpdateCompareWindow(new List<FillUpdateCompareItem>
                    {
                        Item("M1"), Item("M2")
                    });
                    window.Show();
                    Pump(3);

                    Assert.Equal(System.Windows.Visibility.Visible,
                        window.FrameListPanel.Visibility);
                    Assert.Equal(2, window.FrameList.Items.Count);
                    Assert.Equal(0, window.FrameList.SelectedIndex);
                    Assert.Contains("M1", window.TitleText.Text);

                    window.FrameList.SelectedIndex = 1;
                    Pump(2);
                    Assert.Contains("M2", window.TitleText.Text);
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
        public void SingleFrame_HidesFrameList()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new FillUpdateCompareWindow(new List<FillUpdateCompareItem>
                    {
                        Item("M1")
                    });
                    window.Show();
                    Pump(3);

                    Assert.Equal(System.Windows.Visibility.Collapsed,
                        window.FrameListPanel.Visibility);
                    Assert.Contains("M1", window.TitleText.Text);
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
        public void ContinueButton_Confirms_WhenThereAreNoChanges()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new FillUpdateCompareWindow(new List<FillUpdateCompareItem>
                    {
                        Item("M1")
                    });
                    window.Show();
                    Pump(3);
                    Assert.Contains("完全一致", window.SummaryText.Text);

                    Click(window.ContinueButton);
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
        public void Cancel_LeavesUnconfirmed()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new FillUpdateCompareWindow(new List<FillUpdateCompareItem>
                    {
                        Item("M1")
                    });
                    window.Show();
                    Pump(3);

                    Click(window.CancelButton);
                    Pump(2);
                    Assert.False(window.Confirmed);
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
        public void ChangedRows_ShowSummaryAndSemanticRoles()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var item = Item("M1");
                    item.AddedCount = 1;
                    item.RemovedCount = 1;
                    item.QuantityChangedCount = 1;
                    item.PreviousRows = new List<FillRowDiff>
                    {
                        new FillRowDiff { Order = 1, Name = "旧电缆", Unit = "m",
                            OldQuantity = "10", NewQuantity = "12", Code = "1.1",
                            Status = FillRowDiff.StatusRemoved },
                        new FillRowDiff { Order = 2, Name = "桥架", Unit = "m",
                            OldQuantity = "3", NewQuantity = "3", Code = "2.1",
                            Status = FillRowDiff.StatusKept }
                    };
                    item.PlannedRows = new List<FillRowDiff>
                    {
                        new FillRowDiff { Order = 1, Name = "新电缆", Unit = "m",
                            OldQuantity = "10", NewQuantity = "12", Code = "1.2",
                            Status = FillRowDiff.StatusAdded },
                        new FillRowDiff { Order = 2, Name = "桥架", Unit = "m",
                            OldQuantity = "3", NewQuantity = "4", Code = "2.1",
                            Status = FillRowDiff.StatusQuantity },
                        new FillRowDiff { Order = 3, Name = "", Unit = "", Code = "",
                            OldQuantity = "", NewQuantity = "", Status = FillRowDiff.StatusKept }
                    };
                    var window = new FillUpdateCompareWindow(
                        new List<FillUpdateCompareItem> { item });
                    window.Show();
                    Pump(3);

                    Assert.Contains("新增 1 / 移除 1 / 改量 1", window.SummaryText.Text);
                    var previous = Rows(window.PreviousGrid);
                    Assert.Equal("removed", previous[0].Role);
                    Assert.Equal("normal", previous[1].Role);
                    Assert.Equal("10", previous[0].Quantity); // 上次清单看 OldQuantity

                    var planned = Rows(window.PlannedGrid);
                    Assert.Equal("added", planned[0].Role);
                    Assert.Equal("quantity", planned[1].Role);
                    Assert.Equal("12", planned[0].Quantity); // 本次清单看 NewQuantity
                    Assert.Equal("4", planned[1].Quantity);
                    // 空值显示占位符，与旧 ListView 一致。
                    Assert.Equal("—", planned[2].Name);
                    Assert.Equal("—", planned[2].Quantity);
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
        public void Items_WithoutHistory_AreFilteredOut()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var withoutHistory = Item("M9");
                    withoutHistory.HasHistory = false;
                    var window = new FillUpdateCompareWindow(new List<FillUpdateCompareItem>
                    {
                        withoutHistory
                    });
                    window.Show();
                    Pump(3);

                    Assert.Empty(window.FrameList.Items);
                    Assert.Equal("", window.TitleText.Text);
                    Click(window.ContinueButton);
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

        private static List<FillUpdateCompareWindow.CompareRow> Rows(
            System.Windows.Controls.DataGrid grid)
            => grid.Items.Cast<FillUpdateCompareWindow.CompareRow>().ToList();

        private static FillUpdateCompareItem Item(string machineId)
        {
            return new FillUpdateCompareItem
            {
                MachineId = machineId,
                DeviceName = "Device",
                HasHistory = true,
                PreviousRows = new List<FillRowDiff>(),
                PlannedRows = new List<FillRowDiff>()
            };
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
}
