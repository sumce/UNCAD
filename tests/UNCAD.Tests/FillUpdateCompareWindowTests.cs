using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UNCAD.Core.Fill;
using UNCAD.Features.Fill;
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
        public void Window_RendersExactlyWhatCallerPasses()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // 2026-11-04：弹不弹、弹哪些由调用方门控（HasChanges），
                    // 窗口不再按 HasHistory 二次过滤——"有变化但无历史"的图框
                    // 也要能看到对比。
                    var withoutHistory = Item("M9");
                    withoutHistory.HasHistory = false;
                    withoutHistory.AddedCount = 1;
                    var window = new FillUpdateCompareWindow(new List<FillUpdateCompareItem>
                    {
                        withoutHistory
                    });
                    window.Show();
                    Pump(3);

                    Assert.Single(window.FrameList.Items);
                    Assert.Contains("M9", window.TitleText.Text);
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
        public void HasChanges_GatesOnRowDiffsOnly()
        {
            var noChanges = Item("M1");
            Assert.False(noChanges.HasChanges);
            var changed = Item("M2");
            changed.QuantityChangedCount = 1;
            Assert.True(changed.HasChanges);
            var removed = Item("M3");
            removed.RemovedCount = 2;
            Assert.True(removed.HasChanges);
        }

        [Fact]
        public void CableCard_RendersTotalsAndComponents()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var item = Item("M1");
                    item.AddedCount = 1;
                    item.CableCard = FillReviewStage.BuildCableCard(
                        new FrameInfoJsonRecord { TotalCableM = "19.4" },
                        new CableReserveInfo
                        {
                            MeasuredMeters = 16,
                            PanelReserveMillimeters = 3400,
                            PanelReserveLabel = "盘内",
                            DeviceReserveMillimeters = 3000,
                            OwnerReserveMillimeters = 3000,
                            TotalMeters = 25.4
                        });
                    var window = new FillUpdateCompareWindow(
                        new List<FillUpdateCompareItem> { item });
                    window.Show();
                    Pump(3);

                    Assert.Equal(System.Windows.Visibility.Visible,
                        window.CableCardPanel.Visibility);
                    Assert.Contains("上次 19.4 m", window.CableTotalText.Text);
                    Assert.Contains("本次 25.4 m", window.CableTotalText.Text);
                    Assert.Contains("+6 m", window.CableDeltaText.Text);
                    Assert.Contains("实测 16 m", window.CableComponentsText.Text);
                    Assert.Contains("盘内预留 +3.4 m", window.CableComponentsText.Text);
                    Assert.Contains("设备内预留 +3 m", window.CableComponentsText.Text);
                    Assert.Contains("业主预留 +3 m", window.CableComponentsText.Text);
                    Assert.Contains("总长 25.4 m", window.CableComponentsText.Text);
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
        public void CableCard_HidesWhenNoReserveData()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var item = Item("M1");
                    item.AddedCount = 1;
                    item.CableCard = null;
                    var window = new FillUpdateCompareWindow(
                        new List<FillUpdateCompareItem> { item });
                    window.Show();
                    Pump(3);

                    Assert.Equal(System.Windows.Visibility.Collapsed,
                        window.CableCardPanel.Visibility);
                    Assert.Equal("", window.CableComponentsText.Text);
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
        public void BuildCableCard_BreaksDownReserveAndHandlesUnknownPrevious()
        {
            // 纯逻辑：构成分解、差值符号、上次值未知时不显示差值。
            var card = FillReviewStage.BuildCableCard(
                new FrameInfoJsonRecord { TotalCableM = "25.4 m" },
                new CableReserveInfo
                {
                    MeasuredMeters = 22,
                    PanelReserveMillimeters = 500,
                    PanelReserveLabel = "箱内",
                    DeviceReserveMillimeters = 1000,
                    OwnerReserveMillimeters = 3000,
                    TotalMeters = 25.9
                });
            Assert.Equal("25.4 m", card.PreviousTotal);
            Assert.Equal("25.9 m", card.NewTotal);
            Assert.Equal("+0.5 m", card.Delta);
            Assert.True(card.DeltaChanged);
            Assert.Equal(5, card.Components.Count);
            Assert.Equal("箱内预留", card.Components[1].Label);
            Assert.Equal("+0.5 m", card.Components[1].Value);
            Assert.False(card.Components[1].IsTotal);
            Assert.Equal("总长", card.Components[4].Label);
            Assert.True(card.Components[4].IsTotal);

            // 与上次一致：Delta 显示"无变化"。
            var same = FillReviewStage.BuildCableCard(
                new FrameInfoJsonRecord { TotalCableM = "25.9" },
                new CableReserveInfo { TotalMeters = 25.9 });
            Assert.Equal("无变化", same.Delta);
            Assert.False(same.DeltaChanged);

            // 上次值未知（复制框记录被句柄校验拒绝后 previous=null）：只有本次构成。
            var unknown = FillReviewStage.BuildCableCard(null,
                new CableReserveInfo { TotalMeters = 25.9 });
            Assert.Equal("", unknown.PreviousTotal);
            Assert.Equal("", unknown.Delta);
            Assert.Equal(5, unknown.Components.Count);

            // 无预留数据：卡片整体隐藏。
            Assert.Null(FillReviewStage.BuildCableCard(null, null));
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
