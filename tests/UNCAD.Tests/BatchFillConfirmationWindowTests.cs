using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 批量确认窗（D-038 首个迁移窗体）：结构、数据、主题与
    /// 确认/取消路径。无头环境用 STA 线程 + Dispatcher 泵驱动布局。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class BatchFillConfirmationWindowTests
    {
        [Fact]
        public void Window_ShowsEveryFrameWithFluentThemeAndExplicitButtons()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = Enumerable.Range(1, 54).Select(index =>
                        new BatchFillConfirmationRow("H" + index, "M" + index,
                            "设备" + index, index % 2 == 0 ? "有" : "无", index + 1))
                        .ToList();
                    var window = new BatchFillConfirmationWindow(rows);
                    window.Show();
                    Pump(5);

                    // 主题按窗口合并（D-038：绝不进 Application 级资源）。
                    Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                        "Fluent 主题字典必须按窗口合并");

                    Assert.Equal(54, window.BatchGrid.Items.Count);
                    Assert.Equal(5, window.BatchGrid.Columns.Count);
                    Assert.Equal("M1",
                        ((BatchFillConfirmationRow)window.BatchGrid.Items[0]).MachineId);
                    Assert.Equal("确认更新", window.ConfirmButton.Content);
                    Assert.True(window.ConfirmButton.IsDefault, "确认必须是默认按钮（Enter 触发）");
                    Assert.True(window.CancelButton.IsCancel, "取消必须是取消按钮（Esc 触发）");
                    Assert.True(window.ActualWidth >= 940);
                    Assert.True(window.ActualHeight >= 600);
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
        public void ConfirmButton_SetsConfirmed_AndCancelKeepsItFalse()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new BatchFillConfirmationWindow(new List<BatchFillConfirmationRow>
                    {
                        new BatchFillConfirmationRow("3A5F", "M-2F-01", "注塑机", "Running", 42)
                    });
                    window.Show();
                    Pump(3);

                    Click(window.ConfirmButton);
                    Pump(3);
                    Assert.True(window.Confirmed);

                    var cancelWindow = new BatchFillConfirmationWindow(
                        new List<BatchFillConfirmationRow>());
                    cancelWindow.Show();
                    Pump(3);
                    Click(cancelWindow.CancelButton);
                    Pump(3);
                    Assert.False(cancelWindow.Confirmed);
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
            // Wpf.Ui 的 Button 不是原生 Button，AutomationPeer 不认；
            // 直接抬 ClickEvent 路由事件，与用户点击走同一条处理链。
            Assert.NotNull(button);
            button.RaiseEvent(new RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
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
