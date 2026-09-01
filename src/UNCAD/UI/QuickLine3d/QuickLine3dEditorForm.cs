using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.QuickLine;
using UNCAD.Core.Text;

namespace UNCAD.UI.QuickLine3d
{
    /// <summary>
    /// 原生(OpenTK OpenGL)3D 距离编辑器,替代 WebView2 版本。
    /// 绘制模式:方向键/鼠标跟随选轴 + 命令行键入毫米值,回车连续画段;
    /// 编辑模式:点选线段后改距离。对外契约(Updates/CreatedSegments/Revision)
    /// 与旧 WebView2 版本一致。
    /// </summary>
    public sealed class QuickLine3dEditorForm : Form
    {
        private static readonly IReadOnlyDictionary<string, double> NoUpdates =
            new ReadOnlyDictionary<string, double>(
                new Dictionary<string, double>(StringComparer.Ordinal));

        private readonly QuickLineSceneState _state;
        private readonly QuickLineGlCanvas _canvas;
        private readonly TextBox _input;
        private readonly Label _hint;
        private readonly TableLayoutPanel _commandLine;
        private readonly ListView _list;
        private readonly Label _summary;
        private readonly Button _commit;
        private readonly Button _undo;
        private readonly Button _fit;
        private readonly ToolTip _toolTips = new ToolTip();

        public QuickLine3dEditorForm(QuickLineIsometricScene scene)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            _state = QuickLineSceneState.FromScene(scene);
            _state.Changed += RefreshUi;

            DialogLayout.Apply(this, Drawing
                ? "U1X 3D 快速绘图(东南等轴测)"
                : "U1LX 3D 距离编辑",
                new Size(1180, 760), new Size(900, 600), resizable: true);
            Name = nameof(QuickLine3dEditorForm);
            ShowInTaskbar = false;
            KeyPreview = true;

            Updates = NoUpdates;
            CreatedSegments = Array.Empty<QuickLineCreatedSegment>();

            _canvas = new QuickLineGlCanvas(_state);
            _input = new TextBox
            {
                Name = "QuickLineInput",
                Dock = DockStyle.Fill,
                Font = UiTheme.FontInput
            };
            _input.KeyDown += OnInputKeyDown;
            _hint = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.TextSecondary,
                AutoEllipsis = true
            };
            var commandLine = _commandLine = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ColumnCount = 3,
                BackColor = UiTheme.Surface,
                Padding = new Padding(UiTheme.SpaceM, UiTheme.SpaceS, UiTheme.SpaceM, UiTheme.SpaceS)
            };
            commandLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            commandLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            commandLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            commandLine.Controls.Add(new Label
            {
                Text = Drawing ? "长度(mm):" : "修改为(mm):",
                AutoSize = true,
                ForeColor = UiTheme.TextPrimary,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, UiTheme.SpaceS, 0)
            }, 0, 0);
            commandLine.Controls.Add(_input, 1, 0);
            commandLine.Controls.Add(_hint, 2, 0);

            _list = new ListView
            {
                Name = "QuickLineSegmentList",
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary
            };
            _list.Columns.Add("段", 60);
            _list.Columns.Add("轴", 44);
            _list.Columns.Add("长度(mm)", 96);
            _list.Columns.Add("状态", 72);
            _list.SelectedIndexChanged += (sender, args) =>
            {
                if (_list.SelectedItems.Count == 1 && !_state.Drawing)
                    _state.SelectSegment(Convert.ToString(_list.SelectedItems[0].Tag));
            };
            _summary = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(UiTheme.SpaceS)
            };

            var sidePanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 300,
                BackColor = UiTheme.WindowBg,
                Padding = new Padding(UiTheme.SpaceM)
            };
            var sideButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            _undo = UiTheme.Button("撤销上一段");
            _fit = UiTheme.Button("适配视野");
            _undo.Click += (sender, args) => { _state.UndoLastSegment(); _canvas.FitRoute(); };
            _fit.Click += (sender, args) => _canvas.FitRoute();
            sideButtons.Controls.Add(_undo);
            sideButtons.Controls.Add(_fit);
            sidePanel.Controls.Add(_list);
            sidePanel.Controls.Add(_summary);
            sidePanel.Controls.Add(sideButtons);

            _commit = UiTheme.PrimaryButton(Drawing ? "写回 CAD" : "确认修改", DialogResult.OK);
            _commit.Click += (sender, args) => Commit();
            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            var commandBar = UiTheme.CommandBar();
            commandBar.Controls.Add(cancel);
            commandBar.Controls.Add(_commit);

            Controls.Add(_canvas);
            Controls.Add(sidePanel);
            Controls.Add(commandLine);
            Controls.Add(commandBar);
            AcceptButton = null; // 命令行自己处理回车,避免提前关窗
            CancelButton = cancel;
            KeyDown += OnFormKeyDown;
            Shown += (sender, args) =>
            {
                _canvas.FitRoute();
                RefreshUi();
                _input.Focus();
            };
            RefreshUi();
        }

        /// <summary>编辑模式写回的距离表(段 id → 毫米)。</summary>
        public IReadOnlyDictionary<string, double> Updates { get; private set; }

        /// <summary>绘制模式写回的创建段列表。</summary>
        public IReadOnlyList<QuickLineCreatedSegment> CreatedSegments { get; private set; }

        public int Revision => _state.Revision;

        private bool Drawing => _state.Drawing;

        // ---------- 交互 ----------

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (!Drawing) return;
            if (QuickLineAxisInput.TryFromKey(e.KeyCode,
                    out QuickLineSpatialAxis axis, out int sign))
            {
                // 键盘快捷画法:沿用上一段的长度(首段 2000)。
                double lastLength = _state.Segments.Count > 0
                    ? _state.Segments[_state.Segments.Count - 1].DistanceMillimetres
                    : 2000.0;
                _state.ArmPreview(axis, sign, lastLength);
                _state.CommitArmedPreview();
                _canvas.FitRoute();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back && _input.Text.Length == 0 && Drawing)
            {
                // 输入框为空时按 Backspace = 撤销上一段(仿 CAD 命令行)。
                _state.UndoLastSegment();
                _canvas.FitRoute();
                e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Return) return;
            e.SuppressKeyPress = true;
            string text = _input.Text.Trim();
            if (text.Length == 0) return;
            if (!QuickLineMillimeterText.TryParseBareNumber(text, out double millimetres)
                && !double.TryParse(text, out millimetres))
            {
                _hint.Text = "请输入数字,例如 2000";
                return;
            }
            if (Drawing)
            {
                if (_state.PreviewSegment == null)
                {
                    _hint.Text = "请先用方向键(←↓↑→ / PgUp / PgDn)或鼠标选择方向";
                    return;
                }
                _state.CommitPreview(millimetres);
                _canvas.FitRoute();
            }
            else
            {
                if (!_state.SetSelectedDistance(millimetres))
                {
                    _hint.Text = "请先在列表或 3D 视图中选择要修改的线段";
                    return;
                }
            }
            _input.Text = "";
            _input.Focus();
        }

        private void Commit()
        {
            if (Drawing)
            {
                if (_state.Segments.Count == 0)
                {
                    MessageBox.Show(this, "还没有绘制任何线段。", "U1X",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                CreatedSegments = _state.BuildCreatedSegments();
            }
            else
            {
                Updates = _state.BuildUpdates();
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK)
            {
                DialogResult = DialogResult.Cancel;
                Updates = NoUpdates;
                CreatedSegments = Array.Empty<QuickLineCreatedSegment>();
            }
            base.OnFormClosing(e);
        }

        // ---------- UI 刷新 ----------

        private void RefreshUi()
        {
            if (IsDisposed) return;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (QuickLineMutableSegment segment in _state.Segments)
                {
                    var row = new ListViewItem(segment.Id) { Tag = segment.Id };
                    row.SubItems.Add(DisplayAxis(segment));
                    row.SubItems.Add(TextFormatter.FormatNum(segment.Completed
                        ? segment.DistanceMillimetres
                        : segment.DisplayDistanceMillimetres));
                    row.SubItems.Add(segment.Completed ? "已完成" : "未确认");
                    if (string.Equals(segment.Id, _state.SelectedSegmentId,
                        StringComparison.Ordinal)) row.Selected = true;
                    _list.Items.Add(row);
                }
            }
            finally { _list.EndUpdate(); }
            int done = _state.Segments.Count(item => item.Completed);
            _summary.Text = Drawing
                ? $"共 {_state.Segments.Count} 段(全部完成)。拖拽或点选方向手柄画段;Backspace 撤销。"
                : $"共 {_state.Segments.Count} 段,已完成 {done} 段;本次已修改 {_state.ModifiedSegmentIds.Count} 段。";
            _commandLine.Visible = !Drawing;
            _hint.Text = Drawing
                ? "在手柄上按下并拖拽松手,或点一下手柄再点一下确认;按住 Shift 微调,默认 100mm 吸附"
                : "点击线段后输入新距离,回车确认";
            _undo.Visible = Drawing;
            _commit.Enabled = Drawing || _state.ModifiedSegmentIds.Count > 0;
        }

        private static string DisplayAxis(QuickLineMutableSegment segment)
        {
            string axis = segment.Axis.ToString();
            return segment.DirectionSign < 0 ? axis + "-" : axis + "+";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _toolTips.Dispose();
            base.Dispose(disposing);
        }
    }
}
