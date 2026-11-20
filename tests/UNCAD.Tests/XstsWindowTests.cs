using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                    Assert.Equal(3, window.ReportTabs.Items.Count);
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
        public void FrameTab_ShowsOneRowPerFrameWithSeparateModelAndQuantityColumns()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                XstsWindow window = null;
                try
                {
                    var selected = new XstsCircuitRecord("M1", "主回路") { FrameHandle = "00AB" };
                    selected.TableRows.Add(new List<string> { "1", "电缆", "YJV-3*25", "m", "12", "1.1" });
                    selected.TableRows.Add(new List<string> { "2", "桥架", "200*100", "m", "8", "2.1" });
                    selected.TableRows.Add(new List<string> { "4", "线管", "DN25", "m", "4", "3.1" });
                    selected.TableRows.Add(new List<string> { "5", "软管", "DN20", "m", "2", "3.5" });
                    selected.TableRows.Add(new List<string> { "自填", "配件[自填].A", "现场配套\n待确认型号", "套", "待确认", "CUSTOM-01" });
                    var second = new XstsCircuitRecord("M2", "备用回路") { FrameHandle = "00AC" };
                    second.TableRows.Add(new List<string> { "1", "电缆", "YJV-5*10", "m", "24.50", "1.3" });
                    second.TableRows.Add(new List<string> { "2", "桥架", "100*50", "m", "10", "2.2" });
                    var empty = new XstsCircuitRecord("M3", "照明回路") { FrameHandle = "00AD" };
                    var report = XstsReportBuilder.Build(new[] { selected, second, empty },
                        new[] { new XstsCircuitRecord("M1", "主回路"),
                            new XstsCircuitRecord("M2", "备用回路"), new XstsCircuitRecord("M3", "照明回路") });
                    window = new XstsWindow(report, null);
                    window.ShowActivated = false;
                    window.ShowInTaskbar = false;
                    window.Show();
                    window.ReportTabs.SelectedItem = window.FrameTab;
                    Pump(4);
                    Assert.Equal(3, window.ReportTabs.Items.Count);
                    Assert.Null(window.FindName("BlindTab"));
                    Assert.Null(window.FindName("BlindGrid"));
                    Assert.Equal(3, window.FrameGrid.Items.Count);
                    Assert.True(window.FrameGrid.IsReadOnly);
                    Assert.Equal(3, window.FrameGrid.FrozenColumnCount);
                    Assert.Equal(new[] { "机台 ID", "回路名称", "图框ID", "电缆型号", "电缆米数",
                        "桥架型号", "桥架米数", "软管型号", "软管米数", "线管型号", "线管米数",
                        "配件[自填].A型号", "配件[自填].A数量（套）" },
                        window.FrameGrid.Columns.Select(column => (string)column.Header));
                    Assert.Contains("3 框", (string)window.FrameTab.Header);
                    var cableText = Assert.IsType<System.Windows.Controls.TextBlock>(
                        window.FrameGrid.Columns[3].GetCellContent(report.Frames[0]));
                    Assert.Equal("YJV-3*25", cableText.Text);
                    var cableQuantity = Assert.IsType<System.Windows.Controls.TextBlock>(
                        window.FrameGrid.Columns[4].GetCellContent(report.Frames[0]));
                    Assert.Equal("12", cableQuantity.Text);
                    Assert.Contains("编码：1.1", (string)cableText.ToolTip);
                    var emptyModel = Assert.IsType<System.Windows.Controls.TextBlock>(
                        window.FrameGrid.Columns[3].GetCellContent(report.Frames[2]));
                    Assert.Equal("", emptyModel.Text);
                    Assert.DoesNotContain("\n", cableText.Text);
                    Assert.Equal(32, window.FrameGrid.RowHeight);
                    Assert.DoesNotContain("未上图", StatusBadgeBar.Summary(window.BadgeBar));
                    CaptureIfRequested(window, "xsts-details.png");
                    window.Width = window.MinWidth;
                    window.Height = window.MinHeight;
                    Pump(3);
                    Assert.True(window.FrameGrid.ActualWidth > 0);
                    Assert.True(window.FrameGrid.ActualHeight > 0);
                    window.FrameGrid.ScrollIntoView(window.FrameGrid.Items[0],
                        window.FrameGrid.Columns.Last());
                    Pump(3);
                    var customText = Assert.IsType<System.Windows.Controls.TextBlock>(
                        window.FrameGrid.Columns.Last().GetCellContent(report.Frames[0]));
                    Assert.Contains("待确认", customText.Text);
                    Assert.Contains("CUSTOM-01", (string)customText.ToolTip);
                    CaptureIfRequested(window, "xsts-details-minimum.png");
                }
                catch (Exception ex) { failure = ex; }
                finally { window?.Close(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void NextStep_DoesNotClaimCompleteWhenMachineHasNoBaseline()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var report = XstsReportBuilder.Build(
                        new[] { new XstsCircuitRecord("UNKNOWN", "A") },
                        new XstsCircuitRecord[0]);
                    var window = new XstsWindow(report, null);
                    Assert.Contains("核对图框明细", window.NextStep);
                    Assert.DoesNotContain("回路齐全", window.NextStep);
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

        private static void CaptureIfRequested(Window window, string name)
        {
            string directory = Environment.GetEnvironmentVariable("UNCAD_XSTS_SHOTS");
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
                (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(directory, name))) encoder.Save(stream);
        }
    }
}
