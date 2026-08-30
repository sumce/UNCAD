using System.Drawing;
using System.Windows.Forms;

namespace UNCAD.UI
{
    internal static class DialogLayout
    {
        public static void Apply(Form form, string title, Size clientSize,
            Size minimumSize, bool resizable = true)
        {
            form.Text = title;
            form.StartPosition = FormStartPosition.CenterParent;
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.Font = new Font("微软雅黑", 9f);
            form.BackColor = Color.FromArgb(246, 247, 249);
            form.ClientSize = clientSize;
            form.MinimumSize = minimumSize;
            form.Padding = new Padding(8);
            form.FormBorderStyle = resizable
                ? FormBorderStyle.Sizable
                : FormBorderStyle.FixedDialog;
            form.MaximizeBox = resizable;
            form.MinimizeBox = false;
        }

        public static FlowLayoutPanel CommandBar()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 8, 4),
                BackColor = Color.FromArgb(246, 247, 249)
            };
        }

        public static Button CommandButton(string text, DialogResult result = DialogResult.None)
        {
            return new Button
            {
                Text = text,
                Width = 92,
                Height = 30,
                DialogResult = result,
                AutoSize = false,
                UseVisualStyleBackColor = true,
                Margin = new Padding(4, 0, 0, 0)
            };
        }
    }
}
