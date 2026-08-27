using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Fill;
using UNCAD.Infra;

namespace UNCAD.UI
{
    public sealed class FillReviewForm : Form
    {
        private readonly FillReviewData _defaults;
        private readonly DataGridView _grid;
        private readonly Label _count;
        private readonly TextBox _machine = Field();
        private readonly TextBox _region = Field();
        private readonly TextBox _circuit = Field();
        private readonly ComboBox _panel = new ComboBox { Width = 210, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly TextBox _fr = Field();
        private readonly TextBox _seq = Field();
        private readonly TextBox _cable = Field();
        private readonly TextBox _cableMeters = Field();
        private readonly TextBox _diameter = Field();
        private readonly TextBox _downstream = Field();
        private readonly TextBox _upstream = Field();
        private readonly TextBox _detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
        private bool _synchronizing;

        public FillReviewForm(FillReviewData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            _defaults = FillReviewData.Create(Data.Machine,
                Data.Items.Select(item => item.ToTableRow()));

            Text = "UNC_FILL 填充确认 · " + Branding.Nameplate;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(980, 620);
            MinimumSize = new Size(820, 540);
            Font = new Font("微软雅黑", 9f);
            Padding = new Padding(8);

            _panel.Items.AddRange(new object[] { "", "I-Line盘", "母线插接口", "插座盘" });
            TabPage basicTab = BuildBasicTab();

            _grid = BuildGrid();
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 38, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, Padding = new Padding(4, 4, 4, 2)
            };
            Button selectAll = CommandButton("全部勾选");
            Button clearAll = CommandButton("全部取消");
            Button restore = CommandButton("恢复默认");
            _count = new Label { AutoSize = true, Padding = new Padding(12, 7, 0, 0) };
            selectAll.Click += (sender, args) => SetAll(true);
            clearAll.Click += (sender, args) => SetAll(false);
            restore.Click += (sender, args) => RestoreDefaults();
            toolbar.Controls.Add(selectAll);
            toolbar.Controls.Add(clearAll);
            toolbar.Controls.Add(restore);
            toolbar.Controls.Add(_count);

            var listTab = new TabPage("清单选择") { Padding = new Padding(4) };
            listTab.Controls.Add(_grid);
            listTab.Controls.Add(toolbar);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(basicTab);
            tabs.TabPages.Add(listTab);

            var ok = new Button { Text = "确认填充", Width = 96, Height = 30 };
            var cancel = new Button { Text = "取消", Width = 88, Height = 30, DialogResult = DialogResult.Cancel };
            ok.Click += Confirm;
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 6, 4, 2)
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            Controls.Add(tabs);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;

            LoadFromData(Data);
            PopulateRows(Data.Items);
            _cable.TextChanged += CableModelChanged;
            _cableMeters.TextChanged += CableMetersChanged;
            _grid.CurrentCellDirtyStateChanged += (sender, args) =>
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += GridValueChanged;
        }

        public FillReviewData Data { get; private set; }

        private TabPage BuildBasicTab()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 7,
                Padding = new Padding(16, 16, 16, 8), AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            AddPair(layout, 0, "机台ID:", _machine, "区域:", _region);
            AddPair(layout, 1, "设备 / 回路:", _circuit, "盘柜类型:", _panel);
            AddPair(layout, 2, "FR:", _fr, "项目序号:", _seq);
            AddPair(layout, 3, "电缆型号:", _cable, "电缆长度 (m):", _cableMeters);
            AddPair(layout, 4, "软管直径:", _diameter, "下游轴位:", _downstream);
            layout.Controls.Add(LabelFor("上游轴位:"), 0, 5);
            layout.Controls.Add(_upstream, 1, 5);
            layout.SetColumnSpan(_upstream, 3);
            Label detailLabel = LabelFor("配电详情:");
            detailLabel.TextAlign = ContentAlignment.TopRight;
            detailLabel.Padding = new Padding(0, 6, 0, 0);
            layout.Controls.Add(detailLabel, 0, 6);
            layout.Controls.Add(_detail, 1, 6);
            layout.SetColumnSpan(_detail, 3);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var page = new TabPage("基础信息") { Padding = new Padding(4) };
            page.Controls.Add(layout);
            return page;
        }

        private static DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = true, AutoGenerateColumns = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
                BorderStyle = BorderStyle.FixedSingle, BackgroundColor = SystemColors.Window,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            };
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.RowTemplate.Height = 44;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Included", HeaderText = "生成", Width = 52 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "类别", Width = 80, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "特征 / 型号", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Unit", HeaderText = "单位", Width = 58 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "数量 / 长度", Width = 92 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "项目编码", Width = 90 });
            return grid;
        }

        private void LoadFromData(FillReviewData data)
        {
            _synchronizing = true;
            _machine.Text = data.Machine.MachineId ?? "";
            _region.Text = data.Machine.Region ?? "";
            _circuit.Text = data.Machine.CircuitName ?? "";
            _panel.Text = data.Machine.Next ?? "";
            _fr.Text = data.Machine.Fr ?? "";
            _seq.Text = data.Machine.Seq ?? "";
            _cable.Text = data.Machine.Cable ?? "";
            _cableMeters.Text = data.CableMeters ?? "";
            _diameter.Text = data.Machine.Dia ?? "";
            _downstream.Text = data.Machine.DownstreamAxis ?? "";
            _upstream.Text = data.Machine.UpstreamAxis ?? "";
            _detail.Text = data.Machine.Detail ?? "";
            _synchronizing = false;
        }

        private void PopulateRows(IEnumerable<FillReviewItem> items)
        {
            _grid.Rows.Clear();
            foreach (FillReviewItem item in items)
            {
                int index = _grid.Rows.Add(item.Included, FillReviewData.CategoryName(item.Category),
                    item.Name, item.Description, item.Unit, item.Quantity, item.Code);
                _grid.Rows[index].Tag = item;
            }
            UpdateCount();
        }

        private void CableModelChanged(object sender, EventArgs e)
        {
            if (_synchronizing) return;
            Data.SetCableModel(_cable.Text);
            FillReviewItem cable = Data.CableItem();
            if (cable != null) RefreshItem(cable);
        }

        private void CableMetersChanged(object sender, EventArgs e)
        {
            if (_synchronizing) return;
            Data.SetCableMeters(_cableMeters.Text);
            FillReviewItem cable = Data.CableItem();
            if (cable != null) RefreshItem(cable);
        }

        private void GridValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_synchronizing || e.RowIndex < 0) return;
            ReadGridRow(_grid.Rows[e.RowIndex]);
            UpdateCount();
            FillReviewItem item = _grid.Rows[e.RowIndex].Tag as FillReviewItem;
            if (item != null && item.Category == TableFillCategory.Cable
                && e.ColumnIndex == _grid.Columns["Quantity"].Index)
            {
                _synchronizing = true;
                _cableMeters.Text = item.Quantity ?? "";
                Data.CableMeters = item.Quantity ?? "";
                _synchronizing = false;
            }
        }

        private void SetAll(bool included)
        {
            _synchronizing = true;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                row.Cells["Included"].Value = included;
                if (row.Tag is FillReviewItem item) item.Included = included;
            }
            _synchronizing = false;
            UpdateCount();
        }

        private void RestoreDefaults()
        {
            Data = FillReviewData.Create(_defaults.Machine,
                _defaults.Items.Select(item => item.ToTableRow()));
            LoadFromData(Data);
            PopulateRows(Data.Items);
        }

        private void Confirm(object sender, EventArgs e)
        {
            _grid.EndEdit();
            foreach (DataGridViewRow row in _grid.Rows) ReadGridRow(row);
            if (string.IsNullOrWhiteSpace(_machine.Text) || string.IsNullOrWhiteSpace(_circuit.Text))
            {
                MessageBox.Show(this, "机台ID和设备/回路不能为空。", "UNC_FILL",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!ValidQuantity(_cableMeters.Text))
            {
                MessageBox.Show(this, "电缆长度必须是数字或留空。", "UNC_FILL",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            foreach (FillReviewItem item in Data.Items.Where(item => item.Included))
            {
                if (!ValidQuantity(item.Quantity))
                {
                    MessageBox.Show(this, "“" + item.Name + "”的数量/长度必须是数字或留空。",
                        "UNC_FILL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            Data.SetCableMeters(_cableMeters.Text);
            Data.Machine.MachineId = _machine.Text.Trim();
            Data.Machine.Region = _region.Text.Trim();
            Data.Machine.CircuitName = _circuit.Text.Trim();
            Data.Machine.Next = _panel.Text.Trim();
            Data.Machine.Fr = _fr.Text.Trim();
            Data.Machine.Seq = _seq.Text.Trim();
            Data.Machine.Cable = _cable.Text.Trim();
            Data.Machine.Dia = _diameter.Text.Trim();
            Data.Machine.DownstreamAxis = _downstream.Text.Trim();
            Data.Machine.UpstreamAxis = _upstream.Text.Trim();
            Data.Machine.Detail = _detail.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ReadGridRow(DataGridViewRow row)
        {
            if (!(row.Tag is FillReviewItem item)) return;
            item.Included = Convert.ToBoolean(row.Cells["Included"].Value ?? false);
            item.Name = Cell(row, "Name");
            item.Description = Cell(row, "Description");
            item.Unit = Cell(row, "Unit");
            item.Quantity = Cell(row, "Quantity");
            item.Code = Cell(row, "Code");
        }

        private void RefreshItem(FillReviewItem item)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!ReferenceEquals(row.Tag, item)) continue;
                _synchronizing = true;
                row.Cells["Description"].Value = item.Description;
                row.Cells["Quantity"].Value = item.Quantity;
                _synchronizing = false;
                break;
            }
        }

        private void UpdateCount()
        {
            int selected = Data.Items.Count(item => item.Included);
            _count.Text = "将生成 " + selected + " / " + Data.Items.Count + " 行";
        }

        private static bool ValidQuantity(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) return true;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out _);
        }

        private static string Cell(DataGridViewRow row, string name)
            => Convert.ToString(row.Cells[name].Value)?.Trim() ?? "";

        private static TextBox Field() => new TextBox { Dock = DockStyle.Fill };
        private static Button CommandButton(string text) => new Button { Text = text, AutoSize = true, Height = 27 };
        private static Label LabelFor(string text) => new Label
        {
            Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight
        };

        private static void AddPair(TableLayoutPanel layout, int row, string leftLabel,
            Control left, string rightLabel, Control right)
        {
            layout.Controls.Add(LabelFor(leftLabel), 0, row);
            layout.Controls.Add(left, 1, row);
            layout.Controls.Add(LabelFor(rightLabel), 2, row);
            layout.Controls.Add(right, 3, row);
        }
    }
}
