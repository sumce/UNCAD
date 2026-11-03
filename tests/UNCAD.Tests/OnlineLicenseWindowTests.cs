using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 在线授权兜底窗（D-038，P4 聚焦输入）：结构、主题与
    /// 取消路径。无头环境用 STA 线程 + Dispatcher 泵驱动布局。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class OnlineLicenseWindowTests
    {
        [Fact]
        public void Window_IsNarrowFocusFormWithReasonPrefilled()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new OnlineLicenseWindow("账号已在此设备离线。");
                    window.Show();
                    Pump(3);

                    // 主题按窗口合并（D-038：绝不进 Application 级资源）。
                    Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                        "Fluent 主题字典必须按窗口合并");
                    // P4 聚焦输入：窄窗（≤460px）。
                    Assert.True(window.Width <= 460, "在线授权窗必须是窄窗");
                    Assert.Equal("账号已在此设备离线。", window.Status.Message);
                    Assert.Equal(OnlineLicenseMonitor.StoredAuthorizationCode,
                        window.AuthorizationCode.Text);
                    Assert.Equal("验证并保存", window.SaveButton.Content);
                    Assert.True(window.SaveButton.IsDefault, "验证必须是默认按钮（Enter 触发）");
                    Assert.True(window.CancelButton.IsCancel, "稍后处理必须是取消按钮（Esc 触发）");
                    Assert.False(window.Saved);
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
        public void EmptyReason_AsksForAccountAndCancelKeepsSavedFalse()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new OnlineLicenseWindow("  ");
                    window.Show();
                    Pump(3);
                    Assert.Equal("请输入账号。", window.Status.Message);

                    Click(window.CancelButton);
                    Pump(2);
                    Assert.False(window.Saved);
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
            // Wpf.Ui 的 Button 不是原生 Button；直接抬 ClickEvent 路由事件。
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
