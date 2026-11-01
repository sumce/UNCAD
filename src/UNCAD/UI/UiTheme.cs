using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UNCAD.UI
{
    /// <summary>
    /// 统一 UI 主题:色板、字体、间距与控件工厂。
    /// 所有窗体必须从这里取样式,禁止直接 <c>Color.FromArgb</c>。
    ///
    /// 设计方向（2.10.0 商务体系）：
    /// - 主色是深墨蓝 Ink，只用于“要执行的动作”（主按钮、选中标记），不铺大面积；
    /// - 强调色是钢蓝 Accent，用于链接、信息强调和图标；
    /// - 中性面是暖灰，比纯灰更稳重，卡片用白色 + 1px 描边形成层次；
    /// - 语义色（警告/危险/成功/信息）必须成对使用底色与前景色，禁止只改前景。
    /// </summary>
    internal static class UiTheme
    {
        // ---- 中性面（暖灰）----
        public static readonly Color WindowBg = Color.FromArgb(0xF4, 0xF4, 0xF2);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(0xF8, 0xF8, 0xF6);
        public static readonly Color SurfaceSunken = Color.FromArgb(0xEF, 0xEF, 0xEB);
        public static readonly Color Border = Color.FromArgb(0xE4, 0xE2, 0xDD);
        public static readonly Color BorderStrong = Color.FromArgb(0xCF, 0xCC, 0xC4);

        // ---- 文本 ----
        public static readonly Color TextPrimary = Color.FromArgb(0x1B, 0x1F, 0x24);
        public static readonly Color TextSecondary = Color.FromArgb(0x5B, 0x64, 0x6D);
        public static readonly Color TextDisabled = Color.FromArgb(0xA3, 0xA7, 0xAB);

        // ---- 主色：深墨蓝（动作层）----
        public static readonly Color Ink = Color.FromArgb(0x14, 0x30, 0x4A);
        public static readonly Color InkHover = Color.FromArgb(0x1D, 0x41, 0x62);
        public static readonly Color Action = Ink;
        public static readonly Color ActionHover = InkHover;
        public static readonly Color SelectSoft = Color.FromArgb(0xE9, 0xEE, 0xF3);

        // ---- 强调色：钢蓝（信息层）----
        public static readonly Color Accent = Color.FromArgb(0x2E, 0x6B, 0x96);
        public static readonly Color AccentHover = Color.FromArgb(0x23, 0x5A, 0x80);
        public static readonly Color AccentSoft = Color.FromArgb(0xEA, 0xF2, 0xF8);
        /// <summary>Accent 的深色变体，供 Ribbon 图标区分同色系命令。</summary>
        public static readonly Color AccentDark = Color.FromArgb(0x16, 0x4B, 0x6E);
        /// <summary>聚焦描边：输入框、开关和按钮获得焦点时的唯一高亮色。</summary>
        public static readonly Color FocusBorder = Color.FromArgb(0x6E, 0x9C, 0xC0);

        // ---- 语义色 ----
        public static readonly Color WarningBg = Color.FromArgb(0xFB, 0xF2, 0xDF);
        public static readonly Color WarningFg = Color.FromArgb(0x8A, 0x5A, 0x12);
        public static readonly Color WarningBorder = Color.FromArgb(0xEB, 0xD9, 0xB4);
        public static readonly Color DangerBg = Color.FromArgb(0xFB, 0xED, 0xEB);
        public static readonly Color DangerFg = Color.FromArgb(0xA3, 0x2B, 0x27);
        public static readonly Color DangerBorder = Color.FromArgb(0xEF, 0xCF, 0xCA);
        public static readonly Color SuccessFg = Color.FromArgb(0x1F, 0x7A, 0x4C);
        public static readonly Color SuccessBg = Color.FromArgb(0xEA, 0xF5, 0xEE);
        public static readonly Color SuccessBorder = Color.FromArgb(0xC9, 0xE3, 0xD4);
        public static readonly Color InfoFg = Color.FromArgb(0x23, 0x55, 0x6E);
        public static readonly Color InfoBg = AccentSoft;
        public static readonly Color InfoBorder = Color.FromArgb(0xC9, 0xDC, 0xEA);
        /// <summary>卡片投影（极轻，仅用于卡片浮层感，不做发光）。</summary>
        public static readonly Color Shadow = Color.FromArgb(16, 0x20, 0x2C, 0x3A);

        // ---- 品牌(仅 AboutForm / BrandMarkControl)----
        public static readonly Color BrandBlue = Color.FromArgb(0x00, 0x70, 0xAD);
        public static readonly Color BrandOrange = Color.FromArgb(0xEC, 0x7E, 0x22);

        // ---- 字体（字号阶梯）----
        private const string FontFamily = "微软雅黑";
        /// <summary>商标字标使用的西文字体：与中文正文分开，避免改变品牌外观。</summary>
        public const string BrandFontFamily = "Segoe UI";
        public static Font FontBody => new Font(FontFamily, 9.25f);
        public static Font FontBodyBold => new Font(FontFamily, 9.25f, FontStyle.Bold);
        public static Font FontCaption => new Font(FontFamily, 8.25f);
        public static Font FontSection => new Font(FontFamily, 10.5f, FontStyle.Bold);
        public static Font FontInput => new Font(FontFamily, 10.5f);
        public static Font FontHeader => new Font(FontFamily, 12f, FontStyle.Bold);
        public static Font FontTitle => new Font(FontFamily, 14f, FontStyle.Bold);
        public static Font FontDisplay => new Font(FontFamily, 18f, FontStyle.Bold);

        // ---- 间距(4pt 基线)----
        public const int SpaceXXS = 2;
        public const int SpaceXS = 4;
        public const int SpaceS = 8;
        public const int SpaceM = 12;
        public const int SpaceL = 16;
        public const int SpaceXL = 24;
        public const int SpaceXXL = 32;
        public static Padding CardPadding => new Padding(18, 16, 18, 16);
        public static Padding FormPadding => new Padding(SpaceM);

        // ---- 圆角 ----
        public const int RadiusInput = 6;
        public const int RadiusCard = 8;
        public const int RadiusCardLarge = 14;
        public const int RadiusPill = 10;

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

        // ---- 通用绘制 ----

        /// <summary>圆角矩形路径；所有自绘控件共用同一套圆角定义。</summary>
        internal static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>把一种颜色按比例混入另一种颜色，用于悬停/按下态，避免维护第二份色值。</summary>
        internal static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)Math.Round(from.A + (to.A - from.A) * amount),
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        /// <summary>
        /// 给自绘或扁平控件加统一的键盘焦点描边。焦点态只允许一种表达：
        /// 1px FocusBorder 内描边，不再叠加系统虚线框。
        /// </summary>
        public static void FocusHighlight(Control control)
        {
            if (control == null) return;
            control.GotFocus += (sender, args) => control.Invalidate();
            control.LostFocus += (sender, args) => control.Invalidate();
            control.Paint += (sender, args) =>
            {
                if (!control.Focused || control.Width < 2 || control.Height < 2) return;
                using (var pen = new Pen(FocusBorder))
                    args.Graphics.DrawRectangle(pen, 0, 0,
                        control.Width - 1, control.Height - 1);
            };
        }

        // ---- 按钮工厂 ----

        /// <summary>扁平主按钮:Ink 底白字,Hover 加亮。</summary>
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
            FocusHighlight(button);
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
            button.FlatAppearance.MouseDownBackColor = SurfaceSunken;
            FocusHighlight(button);
            return button;
        }

        /// <summary>危险按钮:白底红字,Hover 浅红。危险动作必须与主按钮视觉隔离。</summary>
        public static Button DangerButton(string text)
        {
            var button = FlatButton(text);
            button.BackColor = Surface;
            button.ForeColor = DangerFg;
            button.FlatAppearance.BorderColor = DangerBorder;
            button.FlatAppearance.MouseOverBackColor = DangerBg;
            button.FlatAppearance.MouseDownBackColor = DangerBg;
            FocusHighlight(button);
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

        /// <summary>圆角白卡容器（默认 8px 圆角，保持既有窗体视觉基线）。</summary>
        public static UiCard Card(int cornerRadius = RadiusCard)
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
                Font = FontSection,
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
                Padding = new Padding(SpaceXL, SpaceM, SpaceXL, SpaceM),
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
                    Font = FontBody,
                    ForeColor = TextSecondary,
                    Margin = new Padding(0, SpaceXS, 0, 0)
                });
            }
            layout.Paint += (sender, args) =>
            {
                using (var pen = new Pen(Border))
                    args.Graphics.DrawLine(pen, 0, layout.Height - 1,
                        layout.Width, layout.Height - 1);
            };
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
            else if (control is ListBox listBox)
            {
                listBox.BorderStyle = BorderStyle.FixedSingle;
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
            grid.DefaultCellStyle.SelectionBackColor = SelectSoft;
            grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
        }

        /// <summary>
        /// Locks every existing data row to one DPI-aware height.  RowTemplate
        /// only affects rows created later, and DataGridView can otherwise keep a
        /// previously auto-sized height after a cell value changes.
        /// </summary>
        public static void FixGridRows(DataGridView grid, int designHeight)
        {
            if (grid == null) return;
            grid.AllowUserToResizeRows = false;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            int height = NotAutoScaled(grid, designHeight);
            grid.RowTemplate.Height = height;
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                row.Resizable = DataGridViewTriState.False;
                row.MinimumHeight = height;
                row.Height = height;
            }
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
                Padding = new Padding(SpaceXL, SpaceS, SpaceXL, SpaceS),
                BackColor = Surface
            };
        }
    }

    /// <summary>圆角白卡 Panel，负责统一背景与软描边。</summary>
    internal class UiCard : Panel
    {
        private int _cornerRadius = UiTheme.RadiusCard;

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
            using var path = UiTheme.CreateRoundedPath(rect, CornerRadius);
            using var brush = new SolidBrush(BackColor);
            g.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = UiTheme.CreateRoundedPath(rect, CornerRadius);
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawPath(pen, path);
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
            ItemSize = new Size(UiTheme.NotAutoScaled(52), UiTheme.NotAutoScaled(208));
            Multiline = true;
            SizeMode = TabSizeMode.Fixed;
            Font = UiTheme.FontBody;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ItemSize = new Size(UiTheme.NotAutoScaled(this, 52),
                UiTheme.NotAutoScaled(this, 208));
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            ItemSize = new Size(UiTheme.NotAutoScaled(this, 52),
                UiTheme.NotAutoScaled(this, 208));
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
