using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// U1F 机台选择窗口 v3：
    ///   ① 机台ID 输入（前缀优先的相似建议，最多 8 个，回车/点击选择）
    ///   ② 选中机台后：顶部显示机台摘要（区域/机台ID/回路数），
    ///      中间列表显示全部回路（设备名称）及 电缆型号/软管Φ/详情/序号
    ///   ③ 底部实时预览：本次将写入表格与块属性的内容（单击回路即更新）
    /// </summary>
    public sealed class MachinePickerForm : DpiAwareForm
    {
        private readonly TextBox _machineInput;
        private readonly ListBox _machineSuggest;
        private readonly Label _machineSummary;
        private readonly ListView _circuit;
        private readonly TextBox _circuitDetail;
        private readonly TextBox _preview;
        private readonly Button _ok;
        private readonly TableLayoutPanel _layout;
        private readonly TableLayoutPanel _machineInputPanel;
        private readonly TableLayoutPanel _circuitPanel;
        private readonly TabControl _lowerTabs;
        private bool _applyingLayout;
        private readonly List<string> _machineIds;
        private readonly Func<string, List<MachineRow>> _lookup;
        private readonly Func<MachineRow, string> _previewBuilder;
        private readonly List<MachineRow> _current = new List<MachineRow>();

        public MachinePickerForm(IList<string> machineIds,
            Func<string, List<MachineRow>> lookup,
            Func<MachineRow, string> previewBuilder = null)
        {
            _machineIds = new List<string>(machineIds);
            _lookup = lookup;
            _previewBuilder = previewBuilder;

            DialogLayout.Apply(this, "U1F · " + Branding.Nameplate,
                new Size(820, 620), new Size(700, 520));

            // ① 机台ID 输入
            _machineInputPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8, 4, 8, 6),
                Margin = Padding.Empty,
                MinimumSize = new Size(0, 70)
            };
            _machineInputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _machineInputPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            _machineInputPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            var machineInputLabel = new Label
            {
                Text = "机台 ID",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiTheme.FontBody,
                Margin = Padding.Empty
            };
            _machineInput = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                MinimumSize = new Size(0, 32)
            };
            UiTheme.StyleInput(_machineInput);
            _machineInput.Font = UiTheme.FontInput;
            _machineInputPanel.Controls.Add(machineInputLabel, 0, 0);
            _machineInputPanel.Controls.Add(_machineInput, 0, 1);

            // 相似机台建议
            _machineSuggest = new ListBox
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Visible = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                IntegralHeight = false,
                Margin = new Padding(8, 0, 8, 6),
                MinimumSize = Size.Empty
            };

            // 机台摘要
            _machineSummary = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ForeColor = UiTheme.Accent,
                Font = UiTheme.FontBodyBold,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(0, 0, 0, 4),
                MinimumSize = new Size(0, 30),
                Text = "请输入机台 ID"
            };

            // ② 回路列表
            _circuitPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8, 4, 8, 6),
                Margin = Padding.Empty,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.Surface,
                MinimumSize = new Size(0, 160)
            };
            _circuitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _circuitPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            _circuitPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            var circuitLabel = UiTheme.SectionHeader("设备 / 回路");
            circuitLabel.Dock = DockStyle.Fill;
            circuitLabel.AutoSize = false;
            circuitLabel.TextAlign = ContentAlignment.MiddleLeft;
            circuitLabel.Margin = Padding.Empty;
            _circuit = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.FixedSingle,
                MultiSelect = false,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary
            };
            _circuit.Columns.Add("设备 / 回路", UiTheme.NotAutoScaled(215));
            _circuit.Columns.Add("盘柜类型", UiTheme.NotAutoScaled(110));
            _circuit.Columns.Add("配电详情", UiTheme.NotAutoScaled(330));
            _circuitPanel.Controls.Add(circuitLabel, 0, 0);
            _circuitPanel.Controls.Add(_circuit, 0, 1);

            _circuitDetail = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = UiTheme.Surface,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle
            };

            // ③ 预览
            _preview = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = UiTheme.AccentSoft,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.FontCaption,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle
            };

            var detailTab = new TabPage("回路详情") { Padding = new Padding(4) };
            detailTab.Controls.Add(_circuitDetail);
            var previewTab = new TabPage("写入预览") { Padding = new Padding(4) };
            previewTab.Controls.Add(_preview);
            _lowerTabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.FontBody,
                Margin = Padding.Empty,
                MinimumSize = new Size(0, 150)
            };
            _lowerTabs.TabPages.Add(detailTab);
            _lowerTabs.TabPages.Add(previewTab);

            // 按钮
            _ok = UiTheme.PrimaryButton("确定");
            _ok.Enabled = false;
            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            FlowLayoutPanel btnRow = UiTheme.CommandBar();
            btnRow.Dock = DockStyle.Fill;
            btnRow.AutoSize = false;
            btnRow.MinimumSize = new Size(0, 52);
            btnRow.Padding = Padding.Empty;
            btnRow.Margin = Padding.Empty;
            btnRow.Controls.Add(cancel);
            btnRow.Controls.Add(_ok);

            // 内容行按实际尺寸展开，列表和详情共享全部剩余空间。
            _layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent,
                AutoScroll = true,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 174f));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
            _layout.Controls.Add(_machineInputPanel, 0, 0);
            _layout.Controls.Add(_machineSuggest, 0, 1);
            _layout.Controls.Add(_machineSummary, 0, 2);
            _layout.Controls.Add(_circuitPanel, 0, 3);
            _layout.Controls.Add(_lowerTabs, 0, 4);
            _layout.Controls.Add(btnRow, 0, 5);
            Controls.Add(_layout);
            AcceptButton = _ok;
            CancelButton = cancel;

            // 事件
            _machineInput.TextChanged += (s, e) => UpdateSuggestions();
            _machineInput.KeyDown += OnMachineInputKeyDown;
            _machineSuggest.FontChanged += (s, e) => ResizeSuggestionList();
            _machineSuggest.MouseClick += (s, e) => CommitMachine();
            _machineSuggest.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    CommitMachine();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _machineSuggest.Visible = false;
                    _machineInput.Focus();
                    e.SuppressKeyPress = true;
                }
            };
            _circuit.SelectedIndexChanged += (s, e) => UpdatePreview();
            _circuit.Resize += (s, e) => ResizeCircuitColumns();
            Shown += (s, e) =>
            {
                ApplyStableLayout();
                ResizeCircuitColumns();
            };
            DpiChanged += (s, e) =>
            {
                try { BeginInvoke((MethodInvoker)ApplyStableLayout); }
                catch (InvalidOperationException) { }
            };
            Resize += (s, e) => ApplyStableLayout();
            _circuit.DoubleClick += (s, e) => Confirm();
            _ok.Click += (s, e) => Confirm();

            ApplyStableLayout();
        }

        private void OnMachineInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (_machineSuggest.Visible && _machineSuggest.Items.Count > 0)
                    _machineSuggest.SelectedIndex = 0;
                CommitMachine();
            }
            else if (e.KeyCode == Keys.Down && _machineSuggest.Visible && _machineSuggest.Items.Count > 0)
            {
                _machineSuggest.SelectedIndex = 0;
                _machineSuggest.Focus();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _machineInput.Clear();
                e.SuppressKeyPress = true;
            }
        }

        /// <summary>输入变化 → 相似机台建议（前缀优先，含包含匹配，最多 8 个）。</summary>
        private void UpdateSuggestions()
        {
            string kw = _machineInput.Text.Trim();
            if (kw.Length == 0)
            {
                _machineSuggest.Visible = false;
                _machineSuggest.Items.Clear();
                SetSuggestionRowHeight(0);
                return;
            }
            var list = _machineIds
                .Where(id => id.StartsWith(kw, StringComparison.OrdinalIgnoreCase))
                .Concat(_machineIds
                    .Where(id => id.IndexOf(kw, StringComparison.OrdinalIgnoreCase) > 0
                              && !id.StartsWith(kw, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
            _machineSuggest.Items.Clear();
            foreach (var id in list) _machineSuggest.Items.Add(id);
            if (list.Any(id => string.Equals(id, kw, StringComparison.OrdinalIgnoreCase)))
            {
                CommitMachine();
                return;
            }
            _machineSuggest.Visible = list.Count > 0;
            ResizeSuggestionList();
        }

        private void ResizeSuggestionList()
        {
            if (_machineSuggest.Items.Count > 0)
            {
                int preferredHeight = _machineSuggest.PreferredHeight;
                _machineSuggest.MinimumSize = new Size(0, preferredHeight);
                _machineSuggest.Height = preferredHeight;
                SetSuggestionRowHeight(preferredHeight + _machineSuggest.Margin.Top
                    + _machineSuggest.Margin.Bottom);
            }
            else
            {
                SetSuggestionRowHeight(0);
            }
        }

        private void SetSuggestionRowHeight(int height)
        {
            if (_layout == null || _layout.RowStyles.Count < 2) return;
            _layout.RowStyles[1].SizeType = SizeType.Absolute;
            _layout.RowStyles[1].Height = Math.Max(0, height);
            _layout.PerformLayout();
        }

        /// <summary>
        /// Keeps the fixed-format regions from collapsing when WinForms measures
        /// an empty ListView or a hidden suggestion row. The outer panel scrolls
        /// when a very small/high-DPI window cannot show all regions at once.
        /// </summary>
        private void ApplyStableLayout()
        {
            if (_layout == null || IsDisposed || _applyingLayout) return;
            _applyingLayout = true;
            try
            {
                // Absolute rows are scaled by WinForms during a DPI change. Do
                // not write the design pixels back here or a 150/200% dialog
                // immediately clips its input box again. Only raise a row when
                // the current font metrics prove that the content needs more.
                int inputHeight = Math.Max((int)Math.Round(_layout.RowStyles[0].Height),
                    _machineInput.PreferredHeight + _machineInputPanel.Padding.Vertical
                    + 30);
                _layout.RowStyles[0].SizeType = SizeType.Absolute;
                _layout.RowStyles[0].Height = inputHeight;

                int summaryHeight = Math.Max((int)Math.Round(_layout.RowStyles[2].Height),
                    _machineSummary.PreferredSize.Height + 4);
                _layout.RowStyles[2].SizeType = SizeType.Absolute;
                _layout.RowStyles[2].Height = summaryHeight;

                int circuitMinimum = Math.Max(1, _circuitPanel.MinimumSize.Height);
                int tabsMinimum = Math.Max(1, _lowerTabs.MinimumSize.Height);
                float fixedHeight = _layout.RowStyles[0].Height
                    + _layout.RowStyles[1].Height
                    + _layout.RowStyles[2].Height
                    + _layout.RowStyles[5].Height;
                int availableHeight = Math.Max(0,
                    _layout.ClientSize.Height - (int)Math.Round(fixedHeight));
                int lowerHeight = Math.Max(tabsMinimum,
                    (int)Math.Round(availableHeight * 0.32));
                int circuitHeight = Math.Max(circuitMinimum,
                    availableHeight - lowerHeight);
                _layout.RowStyles[3].SizeType = SizeType.Absolute;
                _layout.RowStyles[3].Height = circuitHeight;
                _layout.RowStyles[4].SizeType = SizeType.Absolute;
                _layout.RowStyles[4].Height = lowerHeight;
                SetSuggestionRowHeight(_machineSuggest.Visible
                    ? _machineSuggest.Height + _machineSuggest.Margin.Top
                        + _machineSuggest.Margin.Bottom : 0);
            }
            finally
            {
                _applyingLayout = false;
            }
        }

        /// <summary>选定机台后按需查询该机台的 SQLite 快照回路。</summary>
        private void CommitMachine()
        {
            string mid = (_machineSuggest.SelectedItem as string) ?? _machineInput.Text.Trim();
            _machineInput.Text = mid;
            _machineSuggest.Visible = false;
            _machineSuggest.Items.Clear();

            _current.Clear();
            _circuit.Items.Clear();
            UpdatePreview();
            _ok.Enabled = false;

            if (mid.Length == 0)
            {
                _machineSummary.Text = "请输入机台 ID";
                _machineSummary.ForeColor = UiTheme.Accent;
                return;
            }

            var rows = _lookup(mid);
            bool exact = rows.Count > 0
                && rows[0].MachineId.Equals(mid, StringComparison.OrdinalIgnoreCase);
            if (!exact)
            {
                _machineSummary.Text = "未找到机台 " + mid + "，请选择上方相似机台";
                _machineSummary.ForeColor = UiTheme.DangerFg;
                return;
            }

            _current.AddRange(rows);
            _machineSummary.Text = rows[0].Region + " ｜ 机台 " + rows[0].MachineId
                + " ｜ 共 " + rows.Count + " 个回路";
            _machineSummary.ForeColor = UiTheme.Accent;

            foreach (var r in _current)
            {
                string displayName = string.IsNullOrWhiteSpace(r.CircuitName)
                    ? r.MachineId : r.CircuitName;
                var item = new ListViewItem(displayName) { Tag = r };
                item.SubItems.Add(DisplayPanelType(r.Next));
                item.SubItems.Add(r.Detail);
                item.UseItemStyleForSubItems = false;
                item.SubItems[1].ForeColor = PanelTypeColor(r.Next);
                _circuit.Items.Add(item);
            }
            if (_circuit.Items.Count > 0) _circuit.Items[0].Selected = true;
        }

        /// <summary>底部预览：显示本次将写入的内容。</summary>
        private void UpdatePreview()
        {
            var row = _circuit.SelectedItems.Count > 0 ? _circuit.SelectedItems[0].Tag as MachineRow : null;
            _ok.Enabled = row != null;
            if (row == null)
            {
                _circuitDetail.Text = "";
                _preview.Text = "选择机台与回路后，此处预览将写入的内容。";
                return;
            }
            _circuitDetail.Text = BuildCircuitDetail(row);
            _preview.Text = _previewBuilder != null ? _previewBuilder(row) : row.ToString();
        }

        private static string DisplayPanelType(string value)
        {
            string type = (value ?? "").Trim();
            return type.Length == 0 || type == "-" ? "未指定" : type;
        }

        private static Color PanelTypeColor(string value)
        {
            string type = DisplayPanelType(value);
            if (type == "I-Line盘") return UiTheme.Accent;
            if (type == "母线插接口") return UiTheme.WarningFg;
            if (type == "插座盘") return UiTheme.SuccessFg;
            return UiTheme.TextSecondary;
        }

        private void ResizeCircuitColumns()
        {
            // ListView 列宽不随容器自动布局；按当前字体/DPI 计算优选宽度，
            // 空间不足时再严格按实际可用宽度重新分配，不依赖特定窗口比例。
            int width = Math.Max(3, _circuit.ClientSize.Width
                - UiTheme.NotAutoScaled(_circuit, 6));
            int[] widths = CalculateCircuitColumnWidths(width, GetDpiScale(_circuit));
            for (int i = 0; i < widths.Length; i++)
                _circuit.Columns[i].Width = widths[i];
        }

        internal static int[] CalculateCircuitColumnWidths(int availableWidth,
            float dpiScale)
        {
            int width = Math.Max(3, availableWidth);
            int preferredName = ScaleColumn(180, dpiScale);
            int preferredPanel = ScaleColumn(110, dpiScale);
            int preferredDetail = ScaleColumn(180, dpiScale);
            int preferredTotal = preferredName + preferredPanel + preferredDetail;

            if (width < preferredTotal)
            {
                int name = Math.Max(1, (int)Math.Round(width * 0.32));
                int panel = Math.Max(1, (int)Math.Round(width * 0.18));
                return new[] { name, panel, Math.Max(1, width - name - panel) };
            }

            int extra = width - preferredTotal;
            int expandedName = preferredName + (int)Math.Round(extra * 0.32);
            int expandedPanel = preferredPanel + (int)Math.Round(extra * 0.18);
            return new[]
            {
                expandedName,
                expandedPanel,
                Math.Max(1, width - expandedName - expandedPanel)
            };
        }

        private static int ScaleColumn(int designValue, float dpiScale)
            => Math.Max(1, (int)Math.Round(designValue * Math.Max(1f, dpiScale)));

        private static float GetDpiScale(Control control)
        {
            try
            {
                if (control != null && control.DeviceDpi > 0)
                    return control.DeviceDpi / 96f;
            }
            catch { }
            return 1f;
        }

        private static string BuildCircuitDetail(MachineRow row)
        {
            return "盘柜类型: " + DisplayPanelType(row.Next)
                + "    区域: " + (row.Region ?? "")
                + "    项目序号: " + (row.Seq ?? "") + Environment.NewLine
                + "FR: " + (row.Fr ?? "") + Environment.NewLine
                + "配电详情: " + (row.Detail ?? "") + Environment.NewLine
                + "下游轴位: " + (row.DownstreamAxis ?? "")
                + "    上游轴位: " + (row.UpstreamAxis ?? "")
                + "    电缆: " + (row.Cable ?? "")
                + "    软管: " + (string.IsNullOrWhiteSpace(row.Dia) ? "" : "Φ" + row.Dia);
        }

        private void Confirm()
        {
            if (_circuit.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先在列表中选择一个设备/回路。", "U1F",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        public MachineRow Selected =>
            _circuit.SelectedItems.Count > 0 ? _circuit.SelectedItems[0].Tag as MachineRow : null;
    }
}
