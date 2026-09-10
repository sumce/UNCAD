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
            // AutoScaleDimensions 缺省为空时 WinForms 不执行任何缩放；显式给出
            // 96-DPI 设计基准，让控件边界、窗体尺寸和内边距按实际 DPI 缩放。
            form.AutoScaleDimensions = new SizeF(96F, 96F);
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
