using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1U 批量结果窗（D-042，2026-11-03）：结构渲染 + 机台行备注去重/BOQ 状态。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class BatchUpdateResultWindowTests
    {
        [Fact]
        public void Window_ListsMachineRowsWithBoqStatus()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<BatchUpdateMachineRow>
                    {
                        new BatchUpdateMachineRow("MTTSA01")
                        {
                            Frames = 3, TableRows = 12, AttributeValues = 60,
                            BoqStatus = "已生成",
                            BoqPath = @"C:\out\MTTSA01\[BOQ]MTTSA01.xlsx"
                        },
                        new BatchUpdateMachineRow("MEFMA01")
                        {
                            Frames = 1, TableRows = 4, AttributeValues = 20,
                            BoqStatus = "跳过（回路不全）"
                        }
                    };
                    rows[1].AddNote("回路不全：本次只更新图纸、不生成该机 BOQ。");

                    var window = new BatchUpdateResultWindow(rows, 4, 16, 30, 80,
                        1, 0, @"C:\out");
                    window.Show();
                    Pump(4);

                    Assert.Equal(2, window.RowsGrid.Items.Count);
                    // D-056：新增「回路」列（按身份键去重计数）。
                    Assert.Equal(8, window.RowsGrid.Columns.Count);
                    Assert.Contains("4 个图框 / 2 个机台", window.Heading);
                    Assert.Contains("表格写入 16 行", window.SummaryText);
                    Assert.Contains("BOQ 新建 1 / 覆盖 0", window.SummaryText);
                    var incomplete = (BatchUpdateMachineRow)window.RowsGrid.Items[1];
                    Assert.Equal("跳过（回路不全）", incomplete.BoqStatus);
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
        public void NoteText_DeduplicatesRepeatedFrameNotes()
        {
            var row = new BatchUpdateMachineRow("M-1");
            row.AddNote("存在未启用或不完整的统计类别，仅这些类别保留旧值。");
            row.AddNote("存在未启用或不完整的统计类别，仅这些类别保留旧值。");
            row.AddNote("Downstream 状态: OK");
            row.AddNote("  ");
            row.AddNote(null);

            Assert.Equal("存在未启用或不完整的统计类别，仅这些类别保留旧值。；Downstream 状态: OK",
                row.NoteText);
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
