using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using UNCAD.Core.Dwg;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>3.0 WPF 排版统计窗（D-038）：结构与数据渲染。</summary>
    [Collection("WpfUiSerial")]
    public sealed class XLayoutSummaryWindowTests
    {
        [Fact]
        public void Window_ListsEveryMachineWithStatus()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var summaries = new List<XLayoutMachineSummary>
                    {
                        new XLayoutMachineSummary("M-2F-01")
                        {
                            CircuitCount = 5, ExpectedCircuitCount = 5,
                            StatusKnown = true, IsComplete = true
                        },
                        new XLayoutMachineSummary("M-3F-07")
                        {
                            CircuitCount = 2, ExpectedCircuitCount = 4,
                            StatusKnown = true, IsComplete = false
                        },
                        // 回路齐全但有重复（2026-11-03 修复口径：黄，不是红“缺回路”）
                        new XLayoutMachineSummary("M-4F-09")
                        {
                            CircuitCount = 6, ExpectedCircuitCount = 5,
                            StatusKnown = true, DuplicateCircuitCount = 1
                        }
                    };
                    summaries[2].DuplicateCircuitNames.Add(
                        "AC Rack（与图框 h1 重复）");
                    var window = new XLayoutSummaryWindow(summaries, 8);
                    window.Show();
                    Pump(4);

                    Assert.Equal(3, window.SummaryGrid.Items.Count);
                    // D-057：密排 9 列（机台/图框/应有/缺/多/重复/待确认/状态/详情）。
                    Assert.Equal(9, window.SummaryGrid.Columns.Count);
                    Assert.Equal(26, window.SummaryGrid.RowHeight);
                    Assert.Equal("状态", Convert.ToString(
                        window.SummaryGrid.Columns[7].Header));
                    Assert.True(window.Resources.MergedDictionaries.Count >= 2);
                    Assert.Contains("8 个图框", window.Heading);
                    var incomplete = (XLayoutMachineSummary)window.SummaryGrid.Items[1];
                    Assert.Equal("缺少回路", incomplete.StatusText);
                    var duplicated = (XLayoutMachineSummary)window.SummaryGrid.Items[2];
                    Assert.Equal("重复 1", duplicated.StatusText);
                    // D-057：重复图框点名到回路与被重复的图框句柄。
                    Assert.Equal("重复 AC Rack（与图框 h1 重复）", duplicated.DetailText);
                    // 机台状态徽章（2026-11-03）：完整 1 / 警告 1（重复） / 缺回路 1 / 未判断 0。
                    string badges = StatusBadgeBar.Summary(window.BadgeBar);
                    Assert.Contains("完整 1 台", badges, StringComparison.Ordinal);
                    Assert.Contains("警告 1 台", badges, StringComparison.Ordinal);
                    Assert.Contains("缺回路 1 台", badges, StringComparison.Ordinal);
                    Assert.Contains("未判断 0 台", badges, StringComparison.Ordinal);
                    Assert.Contains("待确认 0 台", badges, StringComparison.Ordinal);
                    // D-057 详情条：开窗即选中首台机台，全量名单与图框明细随行切换。
                    var missing = (XLayoutMachineSummary)window.SummaryGrid.Items[1];
                    missing.MissingCircuitNames.AddRange(new[] { "C3", "C4" });
                    window.ShowMachineDetail(missing);
                    Assert.Contains("M-3F-07", window.DetailTitleText, StringComparison.Ordinal);
                    Assert.Equal("C3；C4", Detail(window, "缺少回路"));
                    Assert.Equal("无", Detail(window, "待确认"));
                    Assert.True(window.LocateButton.IsEnabled == false);
                    // 下一步指引：有缺回路的机台时点名补画。
                    Assert.Contains("补画", window.NextStep, StringComparison.Ordinal);
                    Assert.True(window.CloseButton.IsDefault);
                    Assert.True(window.CloseButton.IsCancel);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static string Detail(XLayoutSummaryWindow window, string label)
            => window.DetailRows.Single(row => row.Key == label).Value;

        [Fact]
        public void Locate_PassesTheSelectedFrameHandleToTheFeatureCallback()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var summary = new XLayoutMachineSummary("MEDPC01")
                    {
                        CircuitCount = 2, ExpectedCircuitCount = 2,
                        StatusKnown = true, UnresolvedCircuitCount = 1
                    };
                    summary.Frames.Add(new XLayoutFrameRow
                        { Handle = "h1", CircuitName = "AC Rack", UpstreamId = "3N-A", State = "已核实" });
                    summary.Frames.Add(new XLayoutFrameRow
                        { Handle = "h2", CircuitName = "AC Rack", State = "待确认" });
                    var window = new XLayoutSummaryWindow(new[] { summary }, 2);
                    string located = null;
                    int calls = 0;
                    window.LocateRequested = handle => { located = handle; calls++; return true; };
                    window.Show();
                    Pump(2);

                    // 开窗即选中首台：图框明细进网格，选中一行后“定位图框”可用。
                    Assert.Equal(2, window.FrameGrid.Items.Count);
                    Assert.False(window.LocateButton.IsEnabled);
                    window.FrameGrid.SelectedIndex = 1;
                    Pump(1);
                    window.LocateSelectedFrame();
                    Assert.Equal("h2", located);
                    Assert.Equal(1, calls);
                    Assert.Contains("已定位到图框 h2", window.ExportPathText.Text,
                        StringComparison.Ordinal);
                    // 同名回路待确认：黄色 + 指引用户做 U1F/U1U 指认，不是补画。
                    Assert.Equal(XLayoutLabelStatus.Warning, summary.LabelStatus);
                    Assert.Contains("待确认 1", summary.StatusText, StringComparison.Ordinal);
                    Assert.Contains("U1F 或 U1U", window.NextStep, StringComparison.Ordinal);
                    Assert.Contains("待确认 1 台", StatusBadgeBar.Summary(window.BadgeBar),
                        StringComparison.Ordinal);
                    window.Close();
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
            for (var i = 0; i < rounds; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }

        [Fact]
        public void ExportButton_InvokesCallbackShowsPathAndDisables()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new XLayoutSummaryWindow(
                        new List<XLayoutMachineSummary>
                        {
                            new XLayoutMachineSummary("M-01")
                            { CircuitCount = 2, StatusKnown = true, IsComplete = true }
                        }, 2);
                    window.ExportRequested = () => @"C:\out\UNCAD_XLAYOUT_1.xlsx";
                    window.Show();
                    Pump(2);

                    Assert.True(window.ExportButton.IsEnabled);
                    Click(window.ExportButton);
                    Pump(1);

                    Assert.Equal("已导出：C:\\out\\UNCAD_XLAYOUT_1.xlsx",
                        window.ExportPathText.Text);
                    Assert.False(window.ExportButton.IsEnabled);
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
        public void ExportButton_FailureShowsErrorAndKeepsButtonEnabled()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new XLayoutSummaryWindow(
                        new List<XLayoutMachineSummary>
                        {
                            new XLayoutMachineSummary("M-01")
                            { CircuitCount = 1, StatusKnown = true, IsComplete = true }
                        }, 1);
                    window.ExportRequested = () => throw new IOException("磁盘已满");
                    window.Show();
                    Pump(2);

                    Click(window.ExportButton);
                    Pump(1);

                    Assert.Contains("导出失败", window.ExportPathText.Text,
                        StringComparison.Ordinal);
                    Assert.Contains("磁盘已满", window.ExportPathText.Text,
                        StringComparison.Ordinal);
                    Assert.True(window.ExportButton.IsEnabled);
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
    }
}
