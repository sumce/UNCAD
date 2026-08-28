using System.Drawing;
using System.Windows.Forms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    internal sealed class AboutForm : Form
    {
        public AboutForm()
        {
            AboutInfo info = AboutInfo.Current();
            DialogLayout.Apply(this, "关于 UNCAD · UNSIAO Work™",
                new Size(560, 430), new Size(560, 430), false);

            var title = new Label
            {
                Text = "UNCAD",
                Dock = DockStyle.Top,
                Height = 48,
                Font = new Font("微软雅黑", 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 78, 121),
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(18, 0, 0, 0)
            };
            var nameplate = new Label
            {
                Text = Branding.Nameplate,
                Dock = DockStyle.Top,
                Height = 32,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                Padding = new Padding(20, 4, 0, 0)
            };
            var details = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 116,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(18, 8, 18, 4)
            };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddDetail(details, 0, "版本", info.Version);
            AddDetail(details, 1, "构建时间", info.BuildTime);
            AddDetail(details, 2, "更新时间", info.UpdatedOn);
            AddDetail(details, 3, "授权状态", info.Authorization);

            var legal = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Multiline = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
                Text = "开发者: " + Branding.Developer + "\r\n"
                    + "软件所有权归 UNSIAO.Ltd 所有。未经书面许可，禁止逆向工程、反编译、反汇编、二次开发、复制或再分发。\r\n\r\n"
                    + "Email: info@aosa.me\r\nPhone: 17638636162    WeChat: 17638636162",
                ScrollBars = ScrollBars.Vertical,
                TabStop = false
            };
            var legalHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 4, 20, 8) };
            legalHost.Controls.Add(legal);

            var close = new Button { Text = "关闭", Width = 88, Height = 30, DialogResult = DialogResult.OK };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 6, 16, 6)
            };
            buttons.Controls.Add(close);

            Controls.Add(legalHost);
            Controls.Add(details);
            Controls.Add(nameplate);
            Controls.Add(title);
            Controls.Add(buttons);
            AcceptButton = close;
            CancelButton = close;
            Shown += (sender, args) => close.Select();
        }

        private static void AddDetail(TableLayoutPanel panel, int row, string label, string value)
        {
            panel.Controls.Add(new Label
            {
                Text = label + ":",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 4, 8, 2)
            }, 0, row);
            panel.Controls.Add(new Label
            {
                Text = value ?? "",
                AutoSize = true,
                Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                Margin = new Padding(0, 4, 0, 2)
            }, 1, row);
        }
    }
}
