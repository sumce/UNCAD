using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using UNCAD.Core.Dwg;
using UNCAD.Core.Submission;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1DWG / U1PDF 结果窗（D-042 UI 信息完整性，2026-11-03）：按机台/按回路
    /// 汇报产物与状态，徽章行 + 打开输出目录；命令行只留一行摘要。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class ExportResultWindowTests
    {
        [Fact]
        public void DwgWindow_ListsMachineRowsWithBadges()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var machines = new List<DwgExportMachine>
                    {
                        new DwgExportMachine
                        {
                            MachineId = "MTTSA01", Frames = 3, FilePath = @"C:\out\MTTSA01\MTTSA01.dwg",
                            Circuits = { "AP1", "AP2", "AP3" }
                        },
                        new DwgExportMachine
                        {
                            MachineId = "MEFMA01", Frames = 1, FilePath = @"C:\out\MEFMA01\MEFMA01.dwg",
                            Circuits = { "主回路" }
                        }
                    };
                    var window = new DwgExportResultWindow(machines, 4, @"C:\out");
                    window.Show();
                    Pump(4);

                    Assert.Equal(2, window.RowsGrid.Items.Count);
                    Assert.Equal(5, window.RowsGrid.Columns.Count);
                    Assert.Contains("4 个图框 / 2 个机台文件", window.Heading,
                        StringComparison.Ordinal);
                    string badges = StatusBadgeBar.Summary(window.BadgeBar);
                    Assert.Contains("机台 2 台", badges, StringComparison.Ordinal);
                    Assert.Contains("DWG 2 份", badges, StringComparison.Ordinal);
                    var first = (DwgExportMachineRow)window.RowsGrid.Items[0];
                    Assert.Equal(3, first.CircuitCount);
                    Assert.Contains("AP1、AP2、AP3", first.CircuitsText,
                        StringComparison.Ordinal);
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
        public void PdfWindow_ListsPerSheetStatusAndFailureReason()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var targets = new List<PdfExportTarget>
                    {
                        new PdfExportTarget
                        {
                            MachineId = "M1", CircuitName = "AP1",
                            FilePath = @"C:\out\M1\M1_AP1.pdf"
                        },
                        new PdfExportTarget
                        {
                            MachineId = "M2", CircuitName = "AP9",
                            FilePath = @"C:\out\M2\M2_AP9.pdf",
                            LastError = "出图失败：打印设备不可用"
                        }
                    };
                    var window = new PdfExportResultWindow(targets, 1, 1, @"C:\out");
                    window.Show();
                    Pump(4);

                    Assert.Equal(2, window.RowsGrid.Items.Count);
                    Assert.Equal(5, window.RowsGrid.Columns.Count);
                    Assert.Contains("成功 1 / 失败 1（共 2 份）", window.Heading,
                        StringComparison.Ordinal);
                    string badges = StatusBadgeBar.Summary(window.BadgeBar);
                    Assert.Contains("成功 1 份", badges, StringComparison.Ordinal);
                    Assert.Contains("失败 1 份", badges, StringComparison.Ordinal);
                    var failed = (PdfExportResultRow)window.RowsGrid.Items[1];
                    Assert.Equal("失败", failed.Status);
                    Assert.Contains("打印设备不可用", failed.Error, StringComparison.Ordinal);
                    // 下一步指引随失败状态变化。
                    Assert.Contains("修复后重跑", window.NextStep, StringComparison.Ordinal);
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
