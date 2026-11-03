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
    /// 3.0 WPF 在线登录窗（D-038，P4 聚焦输入）：窗口结构、P4 窄窗、
    /// 账号联动与 WebView2 不可用的降级路径。
    ///
    /// 无头环境无法保证 WebView2 运行时/网络，因此 AutoBeginLogin=false
    /// 只测窗口本身；WebView2 的导航拦截、回调校验、bridge 脚本行为
    /// 由 ClientLoginContractTests（纯逻辑）+ SecurityHardeningTests
    /// （源码契约）+ 真机回归覆盖。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class ClientLoginWindowTests
    {
        [Fact]
        public void Window_IsP4NarrowFocusFormWithControlsWired()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = NewWindow();
                    window.Show();
                    Pump(3);

                    // 主题按窗口合并（D-038：绝不进 Application 级资源）。
                    Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                        "Fluent 主题字典必须按窗口合并");
                    // P4 聚焦输入：窄窗（≤460px）。
                    Assert.True(window.Width <= 460, "在线登录窗必须是窄窗");

                    // 账号预填本机存储值；状态条是初始“准备中”文案。
                    Assert.Equal(OnlineLicenseMonitor.StoredAccount,
                        window.Account.Text);
                    Assert.Equal("正在准备登录页面...", window.LoginStatus.Message);

                    // 按钮语义：在此设备上线 = 默认(Enter)；取消 = Esc；
                    // 手动输入默认隐藏（WebView2 可用时不出现）。
                    Assert.Equal("在此设备上线", window.ActivateButton.Content);
                    Assert.True(window.ActivateButton.IsDefault,
                        "上线必须是默认按钮（Enter 触发）");
                    Assert.Equal("取消", window.CancelButton.Content);
                    Assert.True(window.CancelButton.IsCancel,
                        "取消必须是取消按钮（Esc 触发）");
                    Assert.Equal(Visibility.Collapsed, window.ManualButton.Visibility);
                    Assert.False(window.Succeeded);
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
        public void ActivateButton_RequiresTypedAccount()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = NewWindow();
                    window.Show();
                    Pump(2);

                    window.Account.Text = "";
                    Pump(1);
                    Assert.False(window.ActivateButton.IsEnabled,
                        "账号为空时不得允许上线");

                    window.Account.Text = "devuser";
                    Pump(1);
                    Assert.True(window.ActivateButton.IsEnabled,
                        "填写账号后必须可以上线");
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
        public void WebUnavailable_FallbackShowsManualInputAndCancelKeepsNotSucceeded()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = NewWindow();
                    window.Show();
                    Pump(2);

                    // WebView2 运行时缺失的降级路径：手动输入可见、重新登录禁用、
                    // 状态转警告并给出可行动的提示。
                    window.ShowWebUnavailable(
                        "未检测到 Microsoft Edge WebView2 运行时，无法打开在线登录页面。"
                        + "请先安装“Microsoft Edge WebView2 Runtime”（常青版），"
                        + "或改用手动输入账号。");
                    Pump(1);
                    Assert.Equal(Visibility.Visible, window.ManualButton.Visibility);
                    Assert.False(window.ReloadButton.IsEnabled,
                        "WebView2 不可用时“重新登录”必须禁用");
                    Assert.Equal(Wpf.Ui.Controls.InfoBarSeverity.Warning,
                        window.LoginStatus.Severity);
                    Assert.Contains("WebView2", window.LoginStatus.Message);
                    Assert.Equal(Visibility.Visible, window.UnavailablePanel.Visibility);

                    Click(window.CancelButton);
                    Pump(2);
                    Assert.False(window.Succeeded);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static ClientLoginWindow NewWindow()
            // 无头测试：只构造窗口，不触发 WebView2 初始化与真实导航。
            => new ClientLoginWindow { AutoBeginLogin = false };

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
