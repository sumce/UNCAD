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
    /// UNC_F 机台选择窗口 v3：
    ///   ① 机台ID 输入（前缀优先的相似建议，最多 8 个，回车/点击选择）
    ///   ② 选中机台后：顶部显示机台摘要（区域/机台ID/回路数），
    ///      中间列表显示全部回路（设备名称）及 电缆型号/软管Φ/详情/序号
    ///   ③ 底部实时预览：本次将写入表格与块属性的内容（单击回路即更新）
    /// </summary>
    public sealed class MachinePickerForm : Form
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

            DialogLayout.Apply(this, "UNC_F · " + Branding.Nameplate,
                new Size(820, 620), new Size(700, 520));

            // ① 机台ID 输入
            var mBox = new GroupBox
            {
                Text = "机台 ID",
                Dock = DockStyle.Top,
                Height = 54,
                Padding = new Padding(8, 21, 8, 6)
            };
            _machineInput = new TextBox { Dock = DockStyle.Fill, Font = new Font("微软雅黑", 10.5f) };
            mBox.Controls.Add(_machineInput);

            // 相似机台建议
            _machineSuggest = new ListBox
            {
                Dock = DockStyle.Top,
                Height = 72,
                Visible = false,
                Font = new Font("微软雅黑", 9f),
                IntegralHeight = false
            };

            // 机台摘要
            _machineSummary = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                ForeColor = Color.FromArgb(38, 91, 140),
                Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                Padding = new Padding(6, 4, 4, 0),
                Text = "请输入机台 ID"
            };

            // ② 回路列表
            var cBox = new GroupBox
            {
                Text = "设备 / 回路",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 26, 8, 6)
            };
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
                Font = new Font("微软雅黑", 9f)
            };
            _circuit.Columns.Add("设备 / 回路", 215);
            _circuit.Columns.Add("盘柜类型", 110);
            _circuit.Columns.Add("配电详情", 330);
            cBox.Controls.Add(_circuit);

            _circuitDetail = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("微软雅黑", 9f),
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle
            };

            // ③ 预览
            _preview = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(244, 248, 253),
                ForeColor = Color.FromArgb(30, 60, 100),
                Font = new Font("微软雅黑", 8.5f),
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle
            };

            var detailTab = new TabPage("回路详情") { Padding = new Padding(4) };
            detailTab.Controls.Add(_circuitDetail);
            var previewTab = new TabPage("写入预览") { Padding = new Padding(4) };
            previewTab.Controls.Add(_preview);
            var lowerTabs = new TabControl
            {
                Dock = DockStyle.Bottom,
                Height = 126,
                Font = new Font("微软雅黑", 9f)
            };
            lowerTabs.TabPages.Add(detailTab);
            lowerTabs.TabPages.Add(previewTab);

            // 按钮
            _ok = DialogLayout.CommandButton("确定");
            _ok.Enabled = false;
            Button cancel = DialogLayout.CommandButton("取消", DialogResult.Cancel);
            FlowLayoutPanel btnRow = DialogLayout.CommandBar();
            btnRow.Controls.Add(cancel);
            btnRow.Controls.Add(_ok);

            // 布局：Dock 按加入顺序的逆序生效，最后加入的 Top 在最上方
            Controls.Add(cBox);
            Controls.Add(lowerTabs);
            Controls.Add(btnRow);
            Controls.Add(_machineSummary);
            Controls.Add(_machineSuggest);
            Controls.Add(mBox);
            AcceptButton = _ok;
            CancelButton = cancel;

            // 事件
            _machineInput.TextChanged += (s, e) => UpdateSuggestions();
            _machineInput.KeyDown += OnMachineInputKeyDown;
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
        }

        /// <summary>选定机台 → 加载该机台回路（只做一次 Excel 扫描）。</summary>
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
                _machineSummary.ForeColor = Color.SteelBlue;
                return;
            }

            var rows = _lookup(mid);
            bool exact = rows.Count > 0
                && rows[0].MachineId.Equals(mid, StringComparison.OrdinalIgnoreCase);
            if (!exact)
            {
                _machineSummary.Text = "未找到机台 " + mid + "，请选择上方相似机台";
                _machineSummary.ForeColor = Color.Crimson;
                return;
            }

            _current.AddRange(rows);
            _machineSummary.Text = rows[0].Region + " ｜ 机台 " + rows[0].MachineId
                + " ｜ 共 " + rows.Count + " 个回路";
            _machineSummary.ForeColor = Color.SteelBlue;

            foreach (var r in _current)
            {
                var item = new ListViewItem(r.CircuitName) { Tag = r };
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
            if (type == "I-Line盘") return Color.FromArgb(32, 104, 164);
            if (type == "母线插接口") return Color.FromArgb(132, 82, 25);
            if (type == "插座盘") return Color.FromArgb(34, 122, 74);
            return Color.DimGray;
        }

        private void ResizeCircuitColumns()
        {
            int width = Math.Max(480, _circuit.ClientSize.Width - 6);
            int nameWidth = Math.Max(180, (int)(width * 0.32));
            int panelWidth = Math.Max(110, (int)(width * 0.18));
            _circuit.Columns[0].Width = nameWidth;
            _circuit.Columns[1].Width = panelWidth;
            _circuit.Columns[2].Width = Math.Max(180, width - nameWidth - panelWidth);
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
                MessageBox.Show("请先在列表中选择一个设备/回路。", "UNC_F",
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
