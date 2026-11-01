using System;
using System.Drawing;
using System.Windows.Forms;

namespace UNCAD.UI
{
    /// <summary>
    /// 批量 U1U 首次使用：GUI 输入设备内预留（毫米），替代命令行提示。
    /// 只应用于本次还没有设备内预留记录的图框；已有记录保持原值。
    /// </summary>
    public sealed class DeviceReserveForm : DpiAwareForm
    {
        private readonly TextBox _reserve;

        public DeviceReserveForm(int frameCount)
        {
            DialogLayout.Apply(this, "U1U 设备内预留", new Size(560, 230),
                new Size(500, 200));

            var header = UiTheme.Header(
                "本次有 " + Math.Max(0, frameCount) + " 个图框还没有设备内预留记录",
                "请输入设备内预留（毫米）；直接确定 = 0。填写后写入 frameinfo_json，后续 U1U 不再询问。");

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(24, 12, 24, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label
            {
                Text = "设备内预留 (mm):",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = UiTheme.TextSecondary
            }, 0, 0);
            _reserve = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = "0",
                Margin = new Padding(8, 4, 0, 4)
            };
            layout.Controls.Add(_reserve, 1, 0);

            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            Button confirm = UiTheme.PrimaryButton("确定");
            confirm.Click += Confirm;
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Controls.Add(cancel);
            commands.Controls.Add(confirm);

            Controls.Add(layout);
            Controls.Add(commands);
            Controls.Add(header);
            AcceptButton = confirm;
            CancelButton = cancel;
            Shown += (sender, args) => _reserve.Select();
        }

        /// <summary>用户确认的设备内预留（毫米）。</summary>
        public double ReserveMillimeters { get; private set; }

        private void Confirm(object sender, EventArgs e)
        {
            string text = (_reserve.Text ?? "").Trim();
            double value = 0d;
            if (text.Length > 0
                && (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value)
                    || value < 0d || value > 100000d))
            {
                MessageBox.Show(this, "设备内预留必须是 0~100000 的毫米数。",
                    "U1U 设备内预留", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _reserve.Focus();
                _reserve.SelectAll();
                return;
            }
            ReserveMillimeters = value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
