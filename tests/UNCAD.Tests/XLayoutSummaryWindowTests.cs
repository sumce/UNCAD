using System;
using System.Collections.Generic;
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
                    var window = new XLayoutSummaryWindow(summaries, 8);
                    window.Show();
                    Pump(4);

                    Assert.Equal(3, window.SummaryGrid.Items.Count);
                    Assert.Equal(4, window.SummaryGrid.Columns.Count);
                    Assert.Equal("详情", Convert.ToString(
                        window.SummaryGrid.Columns[2].Header));
                    Assert.True(window.Resources.MergedDictionaries.Count >= 2);
                    Assert.Contains("8 个图框", window.Heading);
                    var incomplete = (XLayoutMachineSummary)window.SummaryGrid.Items[1];
                    Assert.Equal("缺少回路", incomplete.StatusText);
                    var duplicated = (XLayoutMachineSummary)window.SummaryGrid.Items[2];
                    Assert.Equal("重复 1", duplicated.StatusText);
                    Assert.Equal("重复 1 处", duplicated.DetailText);
                    // 机台状态徽章（2026-11-03）：完整 1 / 警告 1（重复） / 缺回路 1 / 未判断 0。
                    string badges = StatusBadgeBar.Summary(window.BadgeBar);
                    Assert.Contains("完整 1 台", badges, StringComparison.Ordinal);
                    Assert.Contains("警告 1 台", badges, StringComparison.Ordinal);
                    Assert.Contains("缺回路 1 台", badges, StringComparison.Ordinal);
                    Assert.Contains("未判断 0 台", badges, StringComparison.Ordinal);
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
