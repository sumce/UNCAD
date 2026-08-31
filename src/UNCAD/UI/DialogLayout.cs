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
    }
}
