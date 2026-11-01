using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UNCAD.UI
{
    /// <summary>语义色调：所有提示、徽标和横幅只允许这五种。</summary>
    internal enum UiTone
    {
        Neutral,
        Info,
        Success,
        Warning,
        Danger
    }

    /// <summary>语义色调对应的底色/前景/描边三元组，禁止在窗体里另写色值。</summary>
    internal static class UiTonePalette
    {
        public static Color Background(UiTone tone)
        {
            switch (tone)
            {
                case UiTone.Info: return UiTheme.InfoBg;
                case UiTone.Success: return UiTheme.SuccessBg;
                case UiTone.Warning: return UiTheme.WarningBg;
                case UiTone.Danger: return UiTheme.DangerBg;
                default: return UiTheme.SurfaceAlt;
            }
        }

        public static Color Foreground(UiTone tone)
        {
            switch (tone)
            {
                case UiTone.Info: return UiTheme.InfoFg;
                case UiTone.Success: return UiTheme.SuccessFg;
                case UiTone.Warning: return UiTheme.WarningFg;
                case UiTone.Danger: return UiTheme.DangerFg;
                default: return UiTheme.TextSecondary;
            }
        }

        public static Color Border(UiTone tone)
        {
            switch (tone)
            {
                case UiTone.Info: return UiTheme.InfoBorder;
                case UiTone.Success: return UiTheme.SuccessBorder;
                case UiTone.Warning: return UiTheme.WarningBorder;
                case UiTone.Danger: return UiTheme.DangerBorder;
                default: return UiTheme.Border;
            }
        }
    }

    /// <summary>
    /// 自建组件库入口。窗体只允许从这里取页头、区块、字段行、横幅、徽标、
    /// 工具栏、空状态、表格、开关与进度条，保证边距、字号和语义色一致。
    /// 所有尺寸写 96-DPI 设计值，由 WinForms 自动缩放。
    /// </summary>
    internal static class UiKit
    {
        public static UiPageHeader PageHeader(string title, string subtitle = null)
            => new UiPageHeader(title, subtitle);

        public static UiSectionCard Section(string title, string subtitle = null,
            int cornerRadius = UiTheme.RadiusCard)
            => new UiSectionCard(title, subtitle, cornerRadius);

        public static UiFieldRow Field(string label, Control editor, string hint = null)
            => new UiFieldRow(label, editor, hint);

        public static UiBanner Banner(UiTone tone, string text)
            => new UiBanner(tone, text);

        public static UiStatusPill StatusPill(string text, UiTone tone = UiTone.Neutral)
            => new UiStatusPill(text, tone);

        public static UiToolbar Toolbar() => new UiToolbar();

        public static UiEmptyState EmptyState(string title, string detail = null)
            => new UiEmptyState(title, detail);

        public static UiToggle Toggle(string text) => new UiToggle(text);

        public static UiProgress Progress() => new UiProgress();

        /// <summary>表格只读封装：统一表头、斑马行与选中色。</summary>
        public static void StyleTable(DataGridView grid, bool readOnly = true)
        {
            if (grid == null) return;
            grid.ReadOnly = readOnly;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoGenerateColumns = false;
            if (grid.AutoSizeColumnsMode == DataGridViewAutoSizeColumnsMode.None)
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.BorderStyle = BorderStyle.None;
            UiTheme.StyleGrid(grid);
        }

        /// <summary>固定行高（DataGridView 行高不在自动缩放覆盖范围内）。</summary>
        public static void FixRows(DataGridView grid, int designHeight = 40)
            => UiTheme.FixGridRows(grid, designHeight);

        /// <summary>区块之间的堆叠容器：自动滚动 + 统一页边距。</summary>
        public static Panel ScrollStack(out TableLayoutPanel stack)
        {
            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.WindowBg
            };
            stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = UiTheme.WindowBg,
                Padding = new Padding(UiTheme.SpaceXL, UiTheme.SpaceL,
                    UiTheme.SpaceXL, UiTheme.SpaceXL),
                Margin = new Padding(0)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroll.Controls.Add(stack);
            return scroll;
        }
    }

    /// <summary>页头：主标题 + 说明 + 右侧动作区。所有窗体同一层级只用一次。</summary>
    internal sealed class UiPageHeader : Panel
    {
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly FlowLayoutPanel _actions;

        public UiPageHeader(string title, string subtitle = null)
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.Transparent;
            Padding = new Padding(0, 0, 0, UiTheme.SpaceL);
            Margin = new Padding(0);

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var text = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = string.IsNullOrWhiteSpace(subtitle) ? 1 : 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            text.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _title = new Label
            {
                Text = title ?? "",
                AutoSize = true,
                Font = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0)
            };
            text.Controls.Add(_title, 0, 0);
            _subtitle = new Label
            {
                Text = subtitle ?? "",
                AutoSize = true,
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, UiTheme.SpaceXS, 0, 0),
                Visible = !string.IsNullOrWhiteSpace(subtitle)
            };
            if (!string.IsNullOrWhiteSpace(subtitle)) text.Controls.Add(_subtitle, 0, 1);

            _actions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(UiTheme.SpaceM, 0, 0, 0)
            };
            grid.Controls.Add(text, 0, 0);
            grid.Controls.Add(_actions, 1, 0);
            Controls.Add(grid);
        }

        public string Title
        {
            get => _title.Text;
            set => _title.Text = value ?? "";
        }

        public string Subtitle
        {
            get => _subtitle.Text;
            set
            {
                _subtitle.Text = value ?? "";
                _subtitle.Visible = !string.IsNullOrWhiteSpace(value);
            }
        }

        public FlowLayoutPanel Actions => _actions;

        public UiPageHeader AddAction(Control action)
        {
            if (action != null) _actions.Controls.Add(action);
            return this;
        }
    }

    /// <summary>
    /// 区块卡片：标题 + 可选说明 + 正文。正文只放一种信息层级，
    /// 不要把整页内容塞进一个卡片。默认 8px 圆角与既有 UiCard 契约一致。
    /// </summary>
    internal sealed class UiSectionCard : UiCard
    {
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly FlowLayoutPanel _actions;
        private readonly TableLayoutPanel _layout;

        public UiSectionCard(string title, string subtitle = null,
            int cornerRadius = UiTheme.RadiusCard)
        {
            CornerRadius = cornerRadius;
            BackColor = UiTheme.Surface;
            Padding = new Padding(UiTheme.SpaceL, UiTheme.SpaceM,
                UiTheme.SpaceL, UiTheme.SpaceL);
            Margin = new Padding(0, 0, 0, UiTheme.SpaceM);
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, UiTheme.SpaceS)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titles = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = string.IsNullOrWhiteSpace(subtitle) ? 1 : 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            titles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _title = new Label
            {
                Text = title ?? "",
                AutoSize = true,
                Font = UiTheme.FontSection,
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0)
            };
            titles.Controls.Add(_title, 0, 0);
            _subtitle = new Label
            {
                Text = subtitle ?? "",
                AutoSize = true,
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, UiTheme.SpaceXS, 0, 0),
                Visible = !string.IsNullOrWhiteSpace(subtitle)
            };
            if (!string.IsNullOrWhiteSpace(subtitle)) titles.Controls.Add(_subtitle, 0, 1);

            _actions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(UiTheme.SpaceM, 0, 0, 0)
            };
            header.Controls.Add(titles, 0, 0);
            header.Controls.Add(_actions, 1, 0);

            Body = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _layout.Controls.Add(header, 0, 0);
            _layout.Controls.Add(Body, 0, 1);
            Controls.Add(_layout);
        }

        /// <summary>正文容器：调用方按行添加字段或控件。</summary>
        public TableLayoutPanel Body { get; }

        public FlowLayoutPanel Actions => _actions;

        public UiSectionCard AddAction(Control action)
        {
            if (action != null) _actions.Controls.Add(action);
            return this;
        }

        /// <summary>正文放一整块控件（表格、列表等），Dock=Fill。</summary>
        public UiSectionCard AddBody(Control content)
        {
            if (content == null) return this;
            content.Dock = DockStyle.Fill;
            content.Margin = new Padding(0);
            Body.RowCount++;
            Body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Body.Controls.Add(content, 0, Body.RowCount - 1);
            return this;
        }

        /// <summary>正文放一整块 Dock=Top 的自动高度内容（说明、开关行、小表格）。</summary>
        public UiSectionCard AddBodyTop(Control content)
        {
            if (content == null) return this;
            content.Dock = DockStyle.Top;
            content.Margin = new Padding(0, UiTheme.SpaceXS, 0, 0);
            Body.RowCount++;
            Body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Body.Controls.Add(content, 0, Body.RowCount - 1);
            return this;
        }

        /// <summary>正文加一行“标签 + 输入控件”。</summary>
        public UiSectionCard AddField(string label, Control editor, string hint = null)
        {
            UiFieldRow row = UiKit.Field(label, editor, hint);
            AddBody(row);
            return this;
        }

        /// <summary>正文加普通说明文字。</summary>
        public UiSectionCard AddNote(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return this;
            AddBody(new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, UiTheme.SpaceXS, 0, 0)
            });
            return this;
        }
    }

    /// <summary>
    /// 字段行：标签 / 输入 / 说明 / 错误。错误态统一走红字，不再弹 MessageBox 做行内校验。
    /// </summary>
    internal sealed class UiFieldRow : TableLayoutPanel
    {
        private readonly Label _label;
        private readonly Label _hint;
        private bool _hintAdded;
        private Control _editor;

        public UiFieldRow(string label, Control editor, string hint = null)
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ColumnCount = 2;
            RowCount = string.IsNullOrWhiteSpace(hint) ? 1 : 2;
            BackColor = Color.Transparent;
            Margin = new Padding(0, 0, 0, UiTheme.SpaceS);
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _label = new Label
            {
                Text = label ?? "",
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, UiTheme.SpaceM, 6)
            };
            Editor = editor;
            _hint = new Label
            {
                Text = hint ?? "",
                AutoSize = true,
                Visible = !string.IsNullOrWhiteSpace(hint),
                Font = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, 0, 0, UiTheme.SpaceXS)
            };
            Controls.Add(_label, 0, 0);
            Controls.Add(Editor, 1, 0);
            if (_hint.Visible)
            {
                SetColumnSpan(_hint, 2);
                Controls.Add(_hint, 0, 1);
                _hintAdded = true;
            }
        }

        public Control Editor
        {
            get => _editor;
            set
            {
                _editor = value;
                if (_editor == null) return;
                if (_editor is TextBox || _editor is TableLayoutPanel
                    || _editor is Panel || _editor is ListBox)
                    _editor.Dock = DockStyle.Fill;
                else
                    _editor.Anchor = AnchorStyles.Left;
                _editor.Margin = new Padding(0, 4, 0, 4);
            }
        }

        public string LabelText
        {
            get => _label.Text;
            set => _label.Text = value ?? "";
        }

        /// <summary>说明/错误标签（测试与无障碍检查用）。</summary>
        public Label HintLabel => _hint;

        /// <summary>行内错误：红字提示，输入保留。</summary>
        public void SetError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                ClearError();
                return;
            }
            _hint.Text = message;
            _hint.ForeColor = UiTheme.DangerFg;
            _hint.Visible = true;
            if (!_hintAdded)
            {
                RowCount = Math.Max(RowCount, 2);
                SetColumnSpan(_hint, 2);
                Controls.Add(_hint, 0, 1);
                _hintAdded = true;
            }
        }

        public void ClearError()
        {
            _hint.ForeColor = UiTheme.TextSecondary;
            _hint.Text = "";
            _hint.Visible = false;
        }
    }

    /// <summary>提示横幅：底色 + 左侧色条 + 文本。警告/危险必须给出下一步动作。</summary>
    internal sealed class UiBanner : Panel
    {
        private readonly Label _text;
        private UiTone _tone;

        public UiBanner(UiTone tone, string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(UiTheme.SpaceL, UiTheme.SpaceS,
                UiTheme.SpaceM, UiTheme.SpaceS);
            Margin = new Padding(0, 0, 0, UiTheme.SpaceM);
            _tone = tone;
            _text = new Label
            {
                Text = text ?? "",
                AutoSize = true,
                Font = UiTheme.FontBody,
                UseMnemonic = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            Controls.Add(_text);
            ApplyTone();
        }

        public UiTone Tone
        {
            get => _tone;
            set
            {
                _tone = value;
                ApplyTone();
            }
        }

        public override string Text
        {
            get => _text.Text;
            set
            {
                _text.Text = value ?? "";
                UpdateWrap();
            }
        }

        public Label TextLabel => _text;

        private void ApplyTone()
        {
            BackColor = UiTonePalette.Background(_tone);
            _text.ForeColor = UiTonePalette.Foreground(_tone);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateWrap();
        }

        private void UpdateWrap()
        {
            int available = ClientSize.Width - Padding.Horizontal - 4;
            if (available <= 0) return;
            _text.MaximumSize = new Size(available, 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var border = new Pen(UiTonePalette.Border(_tone)))
                e.Graphics.DrawRectangle(border, rect);
            using (var bar = new SolidBrush(UiTonePalette.Foreground(_tone)))
                e.Graphics.FillRectangle(bar, 0, 0, 3, Height);
        }
    }

    /// <summary>状态徽标：小圆角药丸，只表达状态，不承担点击。</summary>
    internal sealed class UiStatusPill : Control
    {
        private string _text;
        private UiTone _tone;

        public UiStatusPill(string text, UiTone tone = UiTone.Neutral)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            _text = text ?? "";
            _tone = tone;
            AutoSize = true;
            BackColor = Color.Transparent;
            Font = UiTheme.FontBodyBold;
            Margin = new Padding(0, 2, UiTheme.SpaceS, 2);
        }

        public UiTone Tone
        {
            get => _tone;
            set
            {
                _tone = value;
                Invalidate();
            }
        }

        public override string Text
        {
            get => _text;
            set
            {
                _text = value ?? "";
                AutoSize = true;
                Invalidate();
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font);
            return new Size(text.Width + UiTheme.SpaceM * 2,
                text.Height + UiTheme.SpaceXS * 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiTheme.CreateRoundedPath(rect,
                Math.Min(UiTheme.RadiusPill, Height / 2)))
            {
                using (var fill = new SolidBrush(UiTonePalette.Background(_tone)))
                    e.Graphics.FillPath(fill, path);
                using (var border = new Pen(UiTonePalette.Border(_tone)))
                    e.Graphics.DrawPath(border, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, rect,
                UiTonePalette.Foreground(_tone),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>工具栏：窗口内一行动作，与底部命令栏区分（工具栏靠内容，命令栏靠窗口底部）。</summary>
    internal sealed class UiToolbar : FlowLayoutPanel
    {
        public UiToolbar()
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = false;
            BackColor = UiTheme.Surface;
            Padding = new Padding(UiTheme.SpaceM, UiTheme.SpaceS,
                UiTheme.SpaceM, UiTheme.SpaceS);
            Margin = new Padding(0);
        }

        public UiToolbar AddAction(Control action)
        {
            if (action == null) return this;
            action.Margin = new Padding(0, 0, UiTheme.SpaceS, 0);
            Controls.Add(action);
            return this;
        }

        public UiToolbar AddSeparator()
        {
            var separator = new Panel
            {
                Width = 1,
                Height = 22,
                BackColor = UiTheme.Border,
                Margin = new Padding(UiTheme.SpaceS, 4, UiTheme.SpaceS, 0)
            };
            Controls.Add(separator);
            return this;
        }
    }

    /// <summary>空状态：告诉用户“为什么这里是空的、下一步做什么”。</summary>
    internal sealed class UiEmptyState : Panel
    {
        public UiEmptyState(string title, string detail = null)
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = UiTheme.SurfaceAlt;
            Padding = new Padding(UiTheme.SpaceXL);
            Margin = new Padding(0, UiTheme.SpaceS, 0, UiTheme.SpaceS);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label
            {
                Text = title ?? "",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0)
            }, 0, 0);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                layout.Controls.Add(new Label
                {
                    Text = detail,
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = UiTheme.FontCaption,
                    ForeColor = UiTheme.TextSecondary,
                    Margin = new Padding(0, UiTheme.SpaceXS, 0, 0)
                }, 0, 1);
            }
            Controls.Add(layout);
        }
    }

    /// <summary>
    /// 对话框骨架：页头 / 内容区 / 底部按钮区。按钮顺序固定“取消在左、确认在右”，
    /// 危险动作（删除、覆盖）必须用 DangerButton 并放在隔离位置。
    /// </summary>
    internal sealed class UiDialogFrame
    {
        public UiDialogFrame(Form form, string title, string subtitle = null)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            Form = form;
            Header = UiTheme.Header(title, subtitle);
            Body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBg,
                Padding = new Padding(UiTheme.SpaceL)
            };
            Footer = UiTheme.CommandBar();
            form.Controls.Add(Body);
            form.Controls.Add(Footer);
            form.Controls.Add(Header);
        }

        public Form Form { get; }
        public Panel Header { get; }
        public Panel Body { get; }
        public FlowLayoutPanel Footer { get; }

        public Button AddPrimary(string text, DialogResult result = DialogResult.None)
        {
            Button button = UiTheme.PrimaryButton(text, result);
            button.Margin = new Padding(UiTheme.SpaceS, 0, 0, 0);
            Footer.Controls.Add(button);
            return button;
        }

        public Button AddSecondary(string text, DialogResult result = DialogResult.None)
        {
            Button button = UiTheme.Button(text, result);
            button.Margin = new Padding(UiTheme.SpaceS, 0, 0, 0);
            Footer.Controls.Add(button);
            return button;
        }

        public Button AddDanger(string text)
        {
            Button button = UiTheme.DangerButton(text);
            button.Margin = new Padding(0, 0, UiTheme.SpaceM, 0);
            Footer.Controls.Add(button);
            return button;
        }

        /// <summary>挂接 Enter/Esc 行为。</summary>
        public void Wire(Button accept, Button cancel)
        {
            if (accept != null) Form.AcceptButton = accept;
            if (cancel != null) Form.CancelButton = cancel;
        }
    }

    /// <summary>
    /// 开关：自绘药丸轨道 + 圆点。派生自 CheckBox 以保持既有检查契约
    /// （FindAll&lt;CheckBox&gt;、Checked 事件、AccessibleName）。
    /// </summary>
    internal sealed class UiToggle : CheckBox
    {
        public UiToggle() : this("")
        {
        }

        public UiToggle(string text)
        {
            Text = text ?? "";
            AutoSize = true;
            Appearance = Appearance.Normal;
            Font = UiTheme.FontBody;
            ForeColor = UiTheme.TextPrimary;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, UiTheme.SpaceXXS, UiTheme.SpaceL, UiTheme.SpaceXXS);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font);
            int trackHeight = Math.Max(16, Font.Height);
            return new Size(trackHeight * 2 + UiTheme.SpaceS + text.Width + 2,
                Math.Max(trackHeight, text.Height) + 4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int trackHeight = Math.Max(16, Font.Height);
            int trackWidth = trackHeight * 2;
            int top = Math.Max(0, (Height - trackHeight) / 2);
            var track = new Rectangle(0, top, trackWidth, trackHeight);
            Color trackColor = !Enabled ? UiTheme.SurfaceSunken
                : Checked ? UiTheme.Accent : UiTheme.BorderStrong;
            using (var path = UiTheme.CreateRoundedPath(track, trackHeight / 2))
            {
                using (var fill = new SolidBrush(trackColor))
                    e.Graphics.FillPath(fill, path);
                using (var border = new Pen(!Enabled ? UiTheme.Border : trackColor))
                    e.Graphics.DrawPath(border, path);
            }

            int knobSize = trackHeight - 4;
            int knobX = Checked ? track.Right - knobSize - 2 : track.Left + 2;
            var knob = new Rectangle(knobX, track.Top + 2, knobSize, knobSize);
            using (var knobBrush = new SolidBrush(UiTheme.Surface))
                e.Graphics.FillEllipse(knobBrush, knob);
            using (var knobPen = new Pen(UiTheme.Border))
                e.Graphics.DrawEllipse(knobPen, knob);

            int textLeft = track.Right + UiTheme.SpaceS;
            var textRect = new Rectangle(textLeft, 0,
                Math.Max(0, Width - textLeft), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect,
                Enabled ? UiTheme.TextPrimary : UiTheme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics,
                    Rectangle.Inflate(textRect, -1, -3));
        }
    }

    /// <summary>细进度条：只表达确定性进度；不确定进度不要伪装成百分比。</summary>
    internal sealed class UiProgress : Control
    {
        private int _minimum;
        private int _maximum = 100;
        private int _value;
        private UiTone _tone = UiTone.Info;

        public UiProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
                true);
            Height = 6;
            BackColor = Color.Transparent;
            Margin = new Padding(0, UiTheme.SpaceXS, 0, UiTheme.SpaceXS);
        }

        public int Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                if (_maximum <= _minimum) _maximum = _minimum + 1;
                Invalidate();
            }
        }

        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(_minimum + 1, value);
                Invalidate();
            }
        }

        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Max(_minimum, Math.Min(_maximum, value));
                Invalidate();
            }
        }

        public UiTone Tone
        {
            get => _tone;
            set
            {
                _tone = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new Rectangle(0, 0, Width - 1, Height - 1);
            if (track.Width <= 0 || track.Height <= 0) return;
            int radius = Math.Min(track.Height / 2, 3);
            using (var path = UiTheme.CreateRoundedPath(track, radius))
            {
                using (var fill = new SolidBrush(UiTheme.SurfaceSunken))
                    e.Graphics.FillPath(fill, path);
            }
            int range = Math.Max(1, _maximum - _minimum);
            double ratio = (_value - _minimum) / (double)range;
            int barWidth = (int)Math.Round(track.Width * ratio);
            if (barWidth <= 0) return;
            var bar = new Rectangle(track.X, track.Y, barWidth, track.Height);
            using (var path = UiTheme.CreateRoundedPath(bar, radius))
            {
                using (var fill = new SolidBrush(UiTonePalette.Foreground(_tone)))
                    e.Graphics.FillPath(fill, path);
            }
        }
    }
}
