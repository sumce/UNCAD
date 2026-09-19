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
            var machineInputPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8, 4, 8, 8),
                Margin = Padding.Empty
            };
            machineInputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            machineInputPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            machineInputPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var machineInputLabel = new Label
            {
                Text = "机台 ID",
                AutoSize = true,
                Font = UiTheme.FontBody,
                Margin = new Padding(0, 0, 0, 4)
            };
            _machineInput = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            UiTheme.StyleInput(_machineInput);
            _machineInput.Font = UiTheme.FontInput;
            machineInputPanel.Controls.Add(machineInputLabel, 0, 0);
            machineInputPanel.Controls.Add(_machineInput, 0, 1);

            // 相似机台建议
            _machineSuggest = new ListBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Visible = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                IntegralHeight = false,
                Margin = new Padding(8, 0, 8, 8)
            };

            // 机台摘要
            _machineSummary = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                ForeColor = UiTheme.Accent,
                Font = UiTheme.FontBodyBold,
                Margin = new Padding(8, 0, 8, 8),
                Text = "请输入机台 ID"
            };

            // ② 回路列表
            var circuitPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8, 4, 8, 6),
                Margin = Padding.Empty,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.Surface
            };
            circuitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            circuitPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            circuitPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            var circuitLabel = UiTheme.SectionHeader("设备 / 回路");
            circuitLabel.Margin = new Padding(0, 0, 0, 4);
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
            circuitPanel.Controls.Add(circuitLabel, 0, 0);
            circuitPanel.Controls.Add(_circuit, 0, 1);

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
            var lowerTabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.FontBody,
                Margin = new Padding(0, 8, 0, 0)
            };
            lowerTabs.TabPages.Add(detailTab);
            lowerTabs.TabPages.Add(previewTab);

            // 按钮
            _ok = UiTheme.PrimaryButton("确定");
            _ok.Enabled = false;
            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            FlowLayoutPanel btnRow = UiTheme.CommandBar();
            btnRow.Dock = DockStyle.Fill;
            btnRow.Margin = Padding.Empty;
            btnRow.Controls.Add(cancel);
            btnRow.Controls.Add(_ok);

            // 内容行按实际尺寸展开，列表和详情共享全部剩余空间。
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 68f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(machineInputPanel, 0, 0);
            layout.Controls.Add(_machineSuggest, 0, 1);
            layout.Controls.Add(_machineSummary, 0, 2);
            layout.Controls.Add(circuitPanel, 0, 3);
            layout.Controls.Add(lowerTabs, 0, 4);
            layout.Controls.Add(btnRow, 0, 5);
            Controls.Add(layout);
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
            Shown += (s, e) => ResizeCircuitColumns();
            _circuit.DoubleClick += (s, e) => Confirm();
            _ok.Click += (s, e) => Confirm();
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
