using System;
using System.Drawing;
using System.Windows.Forms;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.UI
{
    public sealed class ManualListItemForm : Form
    {
        private readonly TextBox _name = Field("ManualName");
        private readonly TextBox _description = Field("ManualDescription", true);
        private readonly TextBox _unit = Field("ManualUnit");
        private readonly NumericUpDown _quantity = new NumericUpDown
        {
            Name = "ManualQuantity",
            Dock = DockStyle.Left,
            Width = 160,
            Minimum = 0,
            Maximum = 1000000,
            DecimalPlaces = 3,
            Increment = 1,
            Value = 1,
            ThousandsSeparator = true
        };
        private readonly TextBox _code = Field("ManualCode");

        public ManualListItemForm()
        {
            DialogLayout.Apply(this, "新增清单项 · " + Branding.Nameplate,
                new Size(560, 410), new Size(500, 360));
            _unit.Text = "个";

            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(16, 16, 16, 8)
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            AddField(fields, 0, "名称:", _name);
            AddField(fields, 1, "特征 / 型号:", _description);
            AddField(fields, 2, "单位:", _unit);
            AddField(fields, 3, "数量:", _quantity);
            AddField(fields, 4, "项目编码:", _code);

            Button ok = DialogLayout.CommandButton("添加", DialogResult.None);
            Button cancel = DialogLayout.CommandButton("取消", DialogResult.Cancel);
            ok.Click += Confirm;
            FlowLayoutPanel commands = DialogLayout.CommandBar();
            commands.Controls.Add(cancel);
            commands.Controls.Add(ok);

            Controls.Add(fields);
            Controls.Add(commands);
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (sender, args) => _name.Focus();
        }

        public string ItemName => _name.Text.Trim();
        public string Description => _description.Text.Trim();
        public string Unit => _unit.Text.Trim();
        public string Quantity => TextFormatter.FormatNum((double)_quantity.Value);
        public string Code => _code.Text.Trim();

        private void Confirm(object sender, EventArgs e)
        {
            if (ItemName.Length == 0)
            {
                _name.Focus();
                MessageBox.Show(this, "名称不能为空。", "新增清单项",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private static TextBox Field(string name, bool multiline = false)
            => new TextBox
            {
                Name = name,
                Dock = DockStyle.Fill,
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
            };

        private static void AddField(TableLayoutPanel layout, int row,
            string label, Control control)
        {
            layout.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = row == 1 ? ContentAlignment.TopRight
                    : ContentAlignment.MiddleRight,
                Padding = row == 1 ? new Padding(0, 6, 0, 0) : Padding.Empty
            }, 0, row);
            layout.Controls.Add(control, 1, row);
        }
    }
}
