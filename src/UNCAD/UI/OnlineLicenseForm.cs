using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    internal sealed class OnlineLicenseForm : DpiAwareForm
    {
        private readonly TextBox _authorizationCode;
        private readonly Label _status;
        private readonly Button _save;
        private readonly Button _cancel;
        private bool _validating;

        internal OnlineLicenseForm(string reason)
        {
            DialogLayout.Apply(this, "UNCAD Pro 在线授权",
                new Size(660, 380), new Size(600, 350), false);

            _authorizationCode = new TextBox
            {
                Name = "AuthorizationCode",
                Dock = DockStyle.Fill,
                Text = OnlineLicenseMonitor.StoredAuthorizationCode,
                Font = UiTheme.FontInput,
                Margin = new Padding(0)
            };
            UiTheme.StyleInput(_authorizationCode);
            _status = new Label
            {
                Name = "AuthorizationStatus",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                ForeColor = UiTheme.WarningFg,
                Text = string.IsNullOrWhiteSpace(reason) ? "请输入授权码。" : reason,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };

            _save = UiTheme.PrimaryButton("验证并保存");
            _save.Click += ValidateAndSave;
            _cancel = UiTheme.Button("稍后处理", DialogResult.Cancel);

            var commands = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, UiTheme.SpaceM, 0, 0)
            };
            commands.Controls.Add(_save);
            commands.Controls.Add(_cancel);

            var card = UiTheme.Card(16);
            card.Name = "AuthorizationCard";
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(28, 24, 28, 22);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.Controls.Add(new Label
            {
                Text = "在线授权",
                Dock = DockStyle.Fill,
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = "验证此设备的授权",
                Dock = DockStyle.Fill,
                Font = UiTheme.FontDisplay,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 1);
            layout.Controls.Add(_status, 0, 2);
            layout.Controls.Add(new Label
            {
                Text = "授权码",
                Dock = DockStyle.Fill,
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.BottomLeft,
                Margin = new Padding(0)
            }, 0, 3);
            layout.Controls.Add(_authorizationCode, 0, 4);
            layout.Controls.Add(new Label
            {
                Text = "客户信息和授权期限会从在线授权响应中读取。",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(0, UiTheme.SpaceM, 0, 0),
                Margin = new Padding(0)
            }, 0, 5);
            layout.Controls.Add(commands, 0, 6);
            card.Controls.Add(layout);
            Controls.Add(card);

            AcceptButton = _save;
            CancelButton = _cancel;
            Shown += (sender, args) =>
            {
                _authorizationCode.SelectAll();
                _authorizationCode.Focus();
            };
            FormClosing += (sender, args) =>
            {
                if (_validating) args.Cancel = true;
            };
        }

        private async void ValidateAndSave(object sender, EventArgs args)
        {
            if (_validating) return;
            bool succeeded = false;
            _validating = true;
            _save.Enabled = false;
            _cancel.Enabled = false;
            _authorizationCode.Enabled = false;
            _status.ForeColor = UiTheme.TextSecondary;
            _status.Text = "正在连接授权服务器...";
            try
            {
                await Task.Run(() =>
                    OnlineLicenseMonitor.ValidateAndSave(_authorizationCode.Text));
                _status.ForeColor = UiTheme.SuccessFg;
                _status.Text = "授权验证成功。";
                succeeded = true;
            }
            catch (Exception ex)
            {
                _status.ForeColor = UiTheme.DangerFg;
                _status.Text = ex.Message;
            }
            finally
            {
                _validating = false;
                _save.Enabled = true;
                _cancel.Enabled = true;
                _authorizationCode.Enabled = true;
            }
            if (succeeded) DialogResult = DialogResult.OK;
        }
    }
}
