using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UNCAD.Core.Stat;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF XSTS 汇报窗（D-038，P2 模式）：两张统计表、异常页计数、
    /// 主题按窗口合并。无头环境用 STA 线程 + Dispatcher 泵驱动布局。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class XstsWindowTests
    {
        [Fact]
        public void Window_ShowsMachinesAndIssuesWithFluentTheme()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var report = BuildReport();
                    var window = new XstsWindow(report, @"C:\out\UNCAD_XSTS.xlsx");
                    window.Show();
                    Pump(4);

                    // 主题按窗口合并（D-038：绝不进 Application 级资源）。
                    Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                        "Fluent 主题字典必须按窗口合并");

                    Assert.Equal(2, window.MachineGrid.Items.Count);
                    Assert.Equal(7, window.MachineGrid.Columns.Count);
                    Assert.Equal(2, window.IssueGrid.Items.Count);
                    Assert.Equal(4, window.IssueGrid.Columns.Count);
                    Assert.Contains("2", (string)window.IssueTab.Header,
                        StringComparison.Ordinal);
                    Assert.Contains("Excel: C:\\out\\UNCAD_XSTS.xlsx", window.Footer,
                        StringComparison.Ordinal);
                    // 只读汇报窗：Enter 与 Esc 都只是关闭。
                                        // 机台状态徽章（2026-11-03）：BuildReport = M1 缺回路、M2 完整+多出。
                    string badges = StatusBadgeBar.Summary(window.BadgeBar);
                    Assert.Contains("缺回路 1 台", badges, StringComparison.Ordinal);
                    Assert.Contains("完整 0 台", badges, StringComparison.Ordinal);
                    Assert.Contains("多出 1 台", badges, StringComparison.Ordinal);
                    Assert.Contains("无基准 0 台", badges, StringComparison.Ordinal);
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

        [Fact]
        public void Coverage_ReportsExactCountsWhenBaselineAvailable()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var report = BuildReport();
                    var window = new XstsWindow(report, null);
                    Assert.Contains("有效回路 3 / 应有 4", window.Coverage,
                        StringComparison.Ordinal);
                    Assert.Contains("缺少 1", window.Coverage, StringComparison.Ordinal);
                    Assert.Contains("多出 1", window.Coverage, StringComparison.Ordinal);
                    Assert.Contains("未导出 Excel", window.Footer, StringComparison.Ordinal);
                    // 下一步指引随基准与缺回路状态变化。
                    Assert.Contains("补画", window.NextStep, StringComparison.Ordinal);
                    Assert.Contains("U1U", window.NextStep, StringComparison.Ordinal);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static XstsReport BuildReport()
        {
            var report = new XstsReport { FrameCount = 3 };
            var first = new XstsMachineSummary("M1")
            {
                SelectedCircuitCount = 2,
                ExpectedCircuitCount = 3,
                ExpectedDataAvailable = true
            };
            first.SelectedCircuits.AddRange(new[] { "C1", "C2" });
            first.MissingCircuits.Add("C3");
            report.Machines.Add(first);
            var second = new XstsMachineSummary("M2")
            {
                SelectedCircuitCount = 1,
                ExpectedCircuitCount = 1,
                ExpectedDataAvailable = true
            };
            second.SelectedCircuits.Add("主回路");
            second.UnexpectedCircuits.Add("主回路");
            report.Machines.Add(second);
            report.Issues.Add(new XstsFrameIssue
            {
                FrameNumber = 1,
                MachineId = "M1",
                CircuitName = "C3",
                Description = "未在图纸中找到对应回路"
            });
            report.Issues.Add(new XstsFrameIssue
            {
                FrameNumber = 2,
                MachineId = "M2",
                CircuitName = "",
                Description = "缺少回路名称"
            });
            return report;
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
