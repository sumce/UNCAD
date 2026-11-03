using System;
using System.Threading.Tasks;
using System.Windows;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 在线授权兜底窗（3.0 WPF 版，D-038，P4 聚焦输入：窄窗 + 大主按钮 +
    /// 最少输入项）。WebView2 登录不可用时，由 ClientLoginForm 的
    /// “手动验证”入口打开；账号 + 本机凭证心跳语义与旧 OnlineLicenseForm 一致。
    /// </summary>
    public sealed partial class OnlineLicenseWindow : FluentWindow
    {
        /// <summary>授权验证并保存成功为 true；关闭/取消/Esc/失败一律 false。</summary>
        public bool Saved { get; private set; }

        private bool _validating;
        private bool _closed;

        /// <param name="reason">触发本次授权的原因；空则提示输入账号。</param>
        public OnlineLicenseWindow(string reason)
        {
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            AuthorizationCode.Text = OnlineLicenseMonitor.StoredAuthorizationCode;
            Status.Message = string.IsNullOrWhiteSpace(reason) ? "请输入账号。" : reason;
            Loaded += (sender, args) =>
            {
                // 与旧窗一致：打开即全选账号，便于直接覆盖输入。
                AuthorizationCode.SelectAll();
                AuthorizationCode.Focus();
            };
            Closing += (sender, args) =>
            {
                // 验证请求进行中不允许关闭（与旧 FormClosing 语义一致）。
                if (_validating) args.Cancel = true;
            };
            Closed += (sender, args) => _closed = true;
        }

        private async void OnValidateAndSave(object sender, RoutedEventArgs e)
        {
            if (_validating) return;
            _validating = true;
            SaveButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            AuthorizationCode.IsEnabled = false;
            Status.Severity = InfoBarSeverity.Informational;
            Status.Message = "正在连接授权服务器...";
            try
            {
                await Task.Run(() =>
                    OnlineLicenseMonitor.ValidateAndSave(
                        (AuthorizationCode.Text ?? "").Trim()));
                if (_closed) return;
                Status.Severity = InfoBarSeverity.Success;
                Status.Message = "授权验证成功。";
                Saved = true;
                Close();
            }
            catch (Exception ex)
            {
                if (_closed) return;
                Status.Severity = InfoBarSeverity.Error;
                Status.Message = ex.Message;
            }
            finally
            {
                _validating = false;
                SaveButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
                AuthorizationCode.IsEnabled = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Saved = false;
            Close();
        }
    }
}
