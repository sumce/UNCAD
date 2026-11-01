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
        private readonly UiFieldRow _row;

        public DeviceReserveForm(int frameCount)
        {
            DialogLayout.Apply(this, "U1U 设备内预留", new Size(600, 260),
                new Size(520, 230), false);

            var frame = new UiDialogFrame(this, "设备内预留",
                "本次有 " + Math.Max(0, frameCount)
                + " 个图框还没有设备内预留记录。填写后写入 frameinfo_json，后续 U1U 不再询问。");
            _reserve = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = "0"
            };
            UiTheme.StyleInput(_reserve);
            _row = UiKit.Field("设备内预留（mm）", _reserve, "直接确定 = 0；范围 0~100000 毫米。");
            frame.Body.Controls.Add(_row);

            Button confirm = frame.AddPrimary("确定");
            Button cancel = frame.AddSecondary("取消", DialogResult.Cancel);
            confirm.Click += Confirm;
            frame.Wire(confirm, cancel);
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
                // 行内错误 + 保留输入，不弹 Message 中断填写。
                _row.SetError("设备内预留必须是 0~100000 的毫米数。");
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
