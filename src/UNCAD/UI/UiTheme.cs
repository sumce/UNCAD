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
        public static readonly Color WindowBg = Color.FromArgb(0xF6, 0xF6, 0xF6);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(0xFA, 0xFA, 0xFA);
        public static readonly Color Border = Color.FromArgb(0xE7, 0xE7, 0xE7);
        public static readonly Color BorderStrong = Color.FromArgb(0xD4, 0xD4, 0xD4);

        // ---- 文本 ----
        public static readonly Color TextPrimary = Color.FromArgb(0x17, 0x17, 0x17);
        public static readonly Color TextSecondary = Color.FromArgb(0x66, 0x66, 0x66);
        public static readonly Color TextDisabled = Color.FromArgb(0xA3, 0xA3, 0xA3);

        // ---- 强调色 ----
        public static readonly Color Accent = Color.FromArgb(0x25, 0x63, 0xEB);
        public static readonly Color AccentHover = Color.FromArgb(0x1D, 0x4E, 0xD8);
        public static readonly Color AccentSoft = Color.FromArgb(0xEF, 0xF6, 0xFF);
        public static readonly Color Action = Color.FromArgb(0x17, 0x17, 0x17);
        public static readonly Color ActionHover = Color.FromArgb(0x33, 0x33, 0x33);
        /// <summary>Accent 的深色变体，供 Ribbon 图标区分同色系命令。</summary>
        public static readonly Color AccentDark = Color.FromArgb(0x0B, 0x5A, 0xAB);

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
        /// <summary>商标字标使用的西文字体：与中文正文分开，避免改变品牌外观。</summary>
        public const string BrandFontFamily = "Segoe UI";
        public static Font FontBody => new Font(FontFamily, 9.25f);
        public static Font FontBodyBold => new Font(FontFamily, 9.25f, FontStyle.Bold);
        public static Font FontHeader => new Font(FontFamily, 12f, FontStyle.Bold);
        public static Font FontCaption => new Font(FontFamily, 8.25f);
        public static Font FontInput => new Font(FontFamily, 10.5f);
        public static Font FontTitle => new Font(FontFamily, 14f, FontStyle.Bold);
        public static Font FontDisplay => new Font(FontFamily, 18f, FontStyle.Bold);

        // ---- 间距(4pt 基线)----
        public const int SpaceXS = 4;
        public const int SpaceS = 8;
        public const int SpaceM = 12;
        public const int SpaceL = 16;
        public const int SpaceXL = 24;
        public static Padding CardPadding => new Padding(18, 16, 18, 16);
        public static Padding FormPadding => new Padding(SpaceM);

        // ---- DPI 缩放 ----
        // 所有窗体经 DialogLayout.Apply 设置 AutoScaleMode.Dpi +
        // AutoScaleDimensions=(96,96)，由 WinForms 在窗体加载时按实际 DPI 统一
        // 缩放。绝不要对普通控件属性做手工换算——那会与自动缩放叠加，150% 屏幕上
        // 得到 2.25 倍而不是 1.5 倍（实测：控件边界与 TableLayoutPanel 的 Absolute
        // 行列样式都在自动缩放的覆盖范围内）。因此默认写法是直接写 96-DPI 设计值。

        /// <summary>
        /// 仅用于 WinForms 自动缩放**不覆盖**的尺寸属性。实测确认这类属性只有：
        /// <c>ComboBox.ItemHeight</c>、<c>DataGridView.RowTemplate.Height</c>、
        /// <c>DataGridViewColumn.Width</c>、<c>ListView</c> 的 <c>ColumnHeader.Width</c>、
        /// 以及自绘 <c>TabControl.ItemSize</c>。
        /// 它们不随 AutoScaleDimensions 缩放，写死设计值会让高 DPI 下行高/列宽偏小，
        /// 因此必须在此手工换算。其余控件属性一律直接写设计值，不要调用本方法。
        /// 局限：取主显示器 DPI，多显示器混合 DPI 场景下不精确。
        /// </summary>
        public static int NotAutoScaled(int designValue)
            => ScaleForDpi(designValue, SystemDpiScale);

        /// <summary>
        /// 对 WinForms 不自动缩放的属性使用控件当前所在显示器 DPI；用于窗口句柄
        /// 已创建后的列表列宽、表格行高等动态布局。
        /// </summary>
        public static int NotAutoScaled(Control control, int designValue)
        {
            float scale = SystemDpiScale;
            try
            {
                if (control != null && control.IsHandleCreated && control.DeviceDpi > 0)
                    scale = control.DeviceDpi / 96f;
            }
            catch { }
            return ScaleForDpi(designValue, scale);
        }

        private static int ScaleForDpi(int designValue, float scale)
            => (int)Math.Round(designValue * scale);

        private static float SystemDpiScale
        {
            get
            {
                try
                {
                    using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                        return graphics.DpiX / 96f;
                }
                catch { return 1f; }
            }
        }

        // ---- 按钮工厂 ----

        /// <summary>扁平主按钮:Accent 底白字,Hover 加亮。</summary>
        public static Button PrimaryButton(string text, DialogResult result = DialogResult.None)
        {
            var button = FlatButton(text, result);
            Action applyState = () =>
            {
                if (button.Enabled)
                {
                    button.BackColor = Action;
                    button.ForeColor = Surface;
                    button.FlatAppearance.BorderColor = Action;
                    button.FlatAppearance.MouseOverBackColor = ActionHover;
                    button.FlatAppearance.MouseDownBackColor = ActionHover;
                }
                else
                {
                    // WinForms only dims the text by default. Keep a disabled
                    // primary button readable instead of leaving dark text on
                    // the dark action background.
                    button.BackColor = SurfaceAlt;
                    button.ForeColor = TextDisabled;
                    button.FlatAppearance.BorderColor = BorderStrong;
                    button.FlatAppearance.MouseOverBackColor = SurfaceAlt;
                    button.FlatAppearance.MouseDownBackColor = SurfaceAlt;
                }
            };
            button.EnabledChanged += (sender, args) => applyState();
            applyState();
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
                MinimumSize = new Size(92, 34),
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
        public static UiCard Card(int cornerRadius = 8)
        {
            return new UiCard
            {
                CornerRadius = cornerRadius,
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
            else if (control is NumericUpDown number)
            {
                number.BorderStyle = BorderStyle.FixedSingle;
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
                Padding = new Padding(SpaceM, SpaceS, SpaceM, SpaceS),
                BackColor = Surface
            };
        }
    }

    /// <summary>圆角白卡 Panel，负责统一背景与软描边。</summary>
    internal class UiCard : Panel
    {
        private int _cornerRadius = 8;

        public int CornerRadius
        {
            get => _cornerRadius;
            set
            {
                _cornerRadius = Math.Max(0, value);
                Invalidate();
            }
        }

        public UiCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            g.Clear(Parent?.BackColor ?? UiTheme.WindowBg);
            using var path = CreateRoundedPath(rect, CornerRadius);
            using var brush = new SolidBrush(BackColor);
            g.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundedPath(rect, CornerRadius);
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawPath(pen, path);
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            var diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>保留 TabControl 索引契约的左侧文字导航。</summary>
    internal sealed class UiNavigationTabControl : TabControl
    {
        private readonly Font _selectedFont = UiTheme.FontBodyBold;

        public UiNavigationTabControl()
        {
            Alignment = TabAlignment.Left;
            Appearance = TabAppearance.Normal;
            DrawMode = TabDrawMode.OwnerDrawFixed;
            ItemSize = new Size(UiTheme.NotAutoScaled(44), UiTheme.NotAutoScaled(176));
            Multiline = true;
            SizeMode = TabSizeMode.Fixed;
            Font = UiTheme.FontBody;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ItemSize = new Size(UiTheme.NotAutoScaled(this, 44),
                UiTheme.NotAutoScaled(this, 176));
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            ItemSize = new Size(UiTheme.NotAutoScaled(this, 44),
                UiTheme.NotAutoScaled(this, 176));
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            Rectangle bounds = GetTabRect(e.Index);
            bool selected = e.Index == SelectedIndex;
            using (var background = new SolidBrush(selected
                ? UiTheme.Surface : UiTheme.WindowBg))
                e.Graphics.FillRectangle(background, bounds);

            var content = Rectangle.Inflate(bounds, -10, -5);
            if (selected)
            {
                using var marker = new SolidBrush(UiTheme.Action);
                e.Graphics.FillRectangle(marker, content.Left, content.Top + 7,
                    3, Math.Max(8, content.Height - 14));
            }

            var textBounds = new Rectangle(content.Left + 14, content.Top,
                Math.Max(0, content.Width - 18), content.Height);
            TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text,
                selected ? _selectedFont : Font, textBounds,
                selected ? UiTheme.TextPrimary : UiTheme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _selectedFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
