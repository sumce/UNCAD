using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    internal sealed class OnlineLicenseForm : Form
    {
        private readonly TextBox _authorizationCode;
        private readonly Label _status;
        private readonly Button _save;
        private readonly Button _cancel;
        private bool _validating;

        internal OnlineLicenseForm(string reason)
        {
            DialogLayout.Apply(this, "UNCAD Pro 在线授权",
                new Size(610, 260), new Size(540, 240), false);

            _authorizationCode = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = OnlineLicenseMonitor.StoredAuthorizationCode,
                Font = UiTheme.FontInput
            };
            UiTheme.StyleInput(_authorizationCode);
            _status = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                ForeColor = UiTheme.WarningFg,
                Text = string.IsNullOrWhiteSpace(reason) ? "请输入授权码。" : reason,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(
                    8, 12,
                    8, 8),
                ColumnCount = 2,
                RowCount = 3
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fields.Controls.Add(new Label
            {
                Text = "授权状态",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, 0, 12, 0)
            }, 0, 0);
            fields.Controls.Add(_status, 1, 0);
            fields.Controls.Add(new Label
            {
                Text = "授权码",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, 0, 12, 0)
            }, 0, 1);
            fields.Controls.Add(_authorizationCode, 1, 1);
            fields.Controls.Add(new Label
            {
                Text = "客户与授权期限将从授权文件自动读取。",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(0, 8, 0, 0)
            }, 1, 2);

            _save = UiTheme.PrimaryButton("验证并保存");
            _save.Click += ValidateAndSave;
            _cancel = UiTheme.Button("稍后处理", DialogResult.Cancel);
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Controls.Add(_cancel);
            commands.Controls.Add(_save);

            Controls.Add(fields);
            Controls.Add(commands);
            Controls.Add(UiTheme.Header("在线授权", "授权码验证成功后会保存在当前客户机，并每 5 分钟刷新状态。"));
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
