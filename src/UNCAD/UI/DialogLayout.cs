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
            form.Font = UiTheme.FontBody;
            form.BackColor = UiTheme.WindowBg;
            form.ClientSize = clientSize;
            form.MinimumSize = minimumSize;
            form.Padding = UiTheme.FormPadding;
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
                Padding = new Padding(0, UiTheme.SpaceS, UiTheme.SpaceS, UiTheme.SpaceXS),
                BackColor = UiTheme.WindowBg
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
