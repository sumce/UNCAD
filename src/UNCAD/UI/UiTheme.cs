using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UNCAD.UI
{
    /// <summary>
    /// 统一 UI 主题:色板、字体、间距与控件工厂。
    /// 所有窗体必须从这里取样式,禁止直接 <c>Color.FromArgb</c>。
    /// </summary>
    internal static class UiTheme
    {
        // ---- 中性面 ----
        public static readonly Color WindowBg = Color.FromArgb(0xF7, 0xF8, 0xFA);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(0xF0, 0xF2, 0xF5);
        public static readonly Color Border = Color.FromArgb(0xE1, 0xE4, 0xE8);
        public static readonly Color BorderStrong = Color.FromArgb(0xC9, 0xCE, 0xD4);

        // ---- 文本 ----
        public static readonly Color TextPrimary = Color.FromArgb(0x23, 0x2B, 0x34);
        public static readonly Color TextSecondary = Color.FromArgb(0x5F, 0x6B, 0x76);
        public static readonly Color TextDisabled = Color.FromArgb(0x9A, 0xA3, 0xAC);

        // ---- 强调色 ----
        public static readonly Color Accent = Color.FromArgb(0x0E, 0x6F, 0xD1);
        public static readonly Color AccentHover = Color.FromArgb(0x1B, 0x7E, 0xDD);
        public static readonly Color AccentSoft = Color.FromArgb(0xE8, 0xF1, 0xFB);

        // ---- 语义色 ----
        public static readonly Color WarningBg = Color.FromArgb(0xFF, 0xF6, 0xDD);
        public static readonly Color WarningFg = Color.FromArgb(0x8A, 0x53, 0x00);
        public static readonly Color DangerBg = Color.FromArgb(0xFD, 0xEC, 0xE9);
        public static readonly Color DangerFg = Color.FromArgb(0xB4, 0x30, 0x2B);
        public static readonly Color SuccessFg = Color.FromArgb(0x1E, 0x7A, 0x45);

        // ---- 品牌(仅 AboutForm / BrandMarkControl)----
        public static readonly Color BrandBlue = Color.FromArgb(0x00, 0x70, 0xAD);
        public static readonly Color BrandOrange = Color.FromArgb(0xEC, 0x7E, 0x22);

        // ---- 字体 ----
        private const string FontFamily = "微软雅黑";
        public static Font FontBody => new Font(FontFamily, 9f);
        public static Font FontBodyBold => new Font(FontFamily, 9f, FontStyle.Bold);
        public static Font FontHeader => new Font(FontFamily, 11.5f, FontStyle.Bold);
        public static Font FontCaption => new Font(FontFamily, 8.25f);

        // ---- 间距(4pt 基线)----
        public const int SpaceXS = 4;
        public const int SpaceS = 8;
        public const int SpaceM = 12;
        public const int SpaceL = 16;
        public const int SpaceXL = 24;
        public static Padding CardPadding => new Padding(14, 12, 14, 12);
        public static Padding FormPadding => new Padding(SpaceM);

        // ---- 按钮工厂 ----

        /// <summary>扁平主按钮:Accent 底白字,Hover 加亮。</summary>
        public static Button PrimaryButton(string text, DialogResult result = DialogResult.None)
        {
            var button = FlatButton(text, result);
            button.BackColor = Accent;
            button.ForeColor = Surface;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentHover;
            return button;
        }

        /// <summary>次级扁平按钮:白底描边,Hover 浅灰。</summary>
        public static Button Button(string text, DialogResult result = DialogResult.None)
        {
            var button = FlatButton(text, result);
            button.BackColor = Surface;
            button.ForeColor = TextPrimary;
            button.FlatAppearance.BorderColor = BorderStrong;
            button.FlatAppearance.MouseOverBackColor = SurfaceAlt;
            button.FlatAppearance.MouseDownBackColor = SurfaceAlt;
            return button;
        }

        /// <summary>危险按钮:白底红字,Hover 浅红。</summary>
        public static Button DangerButton(string text)
        {
            var button = FlatButton(text);
            button.BackColor = Surface;
            button.ForeColor = DangerFg;
            button.FlatAppearance.BorderColor = DangerBg;
            button.FlatAppearance.MouseOverBackColor = DangerBg;
            button.FlatAppearance.MouseDownBackColor = DangerBg;
            return button;
        }

        /// <summary>文本链接按钮:Accent 前景无边框。</summary>
        public static Button LinkButton(string text)
        {
            var button = FlatButton(text);
            button.BackColor = Color.Transparent;
            button.ForeColor = Accent;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = AccentSoft;
            button.FlatAppearance.MouseDownBackColor = AccentSoft;
            button.AutoSize = true;
            button.FlatStyle = FlatStyle.Flat;
            return button;
        }

        private static Button FlatButton(string text, DialogResult result = DialogResult.None)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(92, 30),
                DialogResult = result,
                FlatStyle = FlatStyle.Flat,
                Font = FontBody,
                UseVisualStyleBackColor = false,
                Margin = new Padding(SpaceXS, 0, 0, 0),
                Padding = new Padding(SpaceM, 0, SpaceM, 0),
                Cursor = Cursors.Hand
            };
        }

        // ---- 容器 ----

        /// <summary>圆角白卡容器。</summary>
        public static UiCard Card()
        {
            return new UiCard
            {
                BackColor = Surface,
                Padding = CardPadding,
                Margin = new Padding(0, 0, 0, SpaceM)
            };
        }

        /// <summary>卡片内区块标题。</summary>
        public static Label SectionHeader(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = FontBodyBold,
                ForeColor = TextPrimary,
                Margin = new Padding(0, 0, 0, SpaceS)
            };
        }

        /// <summary>页头条:白底、标题 + 可选副标题。</summary>
        public static Panel Header(string title, string subtitle = null)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Surface,
                Padding = new Padding(SpaceM, SpaceM, SpaceM, SpaceS),
                ColumnCount = 1,
                RowCount = subtitle == null ? 1 : 2,
                Margin = new Padding(0)
            };
            var titleLabel = new Label
            {
                Text = title,
                AutoSize = true,
                Font = FontHeader,
                ForeColor = TextPrimary,
                Margin = new Padding(0)
            };
            layout.Controls.Add(titleLabel);
            if (subtitle != null)
            {
                layout.Controls.Add(new Label
                {
                    Text = subtitle,
                    AutoSize = true,
                    Font = FontCaption,
                    ForeColor = TextSecondary,
                    Margin = new Padding(0, SpaceXS, 0, 0)
                });
            }
            return layout;
        }

        // ---- 输入控件 ----

        /// <summary>统一输入控件外观:白底、软描边。</summary>
        public static void StyleInput(Control control)
        {
            control.BackColor = Surface;
            control.ForeColor = TextPrimary;
            control.Font = FontBody;
            if (control is TextBox textBox)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is ComboBox comboBox)
            {
                comboBox.FlatStyle = FlatStyle.Flat;
            }
        }

        /// <summary>表单行布局:两/四列"标签:控件"网格。</summary>
        public static TableLayoutPanel FieldGrid(int columns = 2)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = columns * 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            for (var i = 0; i < columns * 2; i++)
            {
                // 标签列自适应,输入列拉伸
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            }
            return grid;
        }

        // ---- 命令栏 ----

        /// <summary>统一 DataGridView 外观:白底、软网格线、AccentSoft 选中、斑马行。</summary>
        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextSecondary;
            grid.ColumnHeadersDefaultCellStyle.Font = FontBodyBold;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(SpaceS, SpaceXS, SpaceS, SpaceXS);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
        }

        /// <summary>底部右对齐命令栏。</summary>
        public static FlowLayoutPanel CommandBar()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, SpaceS, SpaceS, SpaceXS),
                BackColor = WindowBg
            };
        }
    }

    /// <summary>圆角白卡 Panel:6px 圆角 + 软描边。</summary>
    internal class UiCard : Panel
    {
        private const int CornerRadius = 6;

        public UiCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundedPath(rect, CornerRadius);
            using var brush = new SolidBrush(BackColor);
            g.FillPath(brush, path);
            using var pen = new Pen(UiTheme.Border);
            g.DrawPath(pen, path);
            base.OnPaint(e);
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
