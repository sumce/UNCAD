using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.UI
{
    public sealed class FillReviewForm : Form
    {
        private readonly FillReviewData _defaults;
        private readonly DataGridView _grid;
        private readonly TabControl _tabs;
        private readonly Label _count;
        private readonly Button _replaceItem;
        private readonly Button _resolveMissing;
        private readonly Button _removeItem;
        private readonly ToolTip _toolTips = new ToolTip();
        private readonly Label _catalogWarning = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 54,
            AutoEllipsis = true,
            UseMnemonic = false,
            Padding = new Padding(8, 7, 8, 5),
            ForeColor = UiTheme.WarningFg,
            BackColor = UiTheme.WarningBg,
            Visible = false
        };
        private readonly BoqCatalogIndex _catalog;
        private readonly FillPlanningOptions _planningOptions;
        private readonly bool _lockIdentity;
        private readonly TextBox _machine = Field();
        private readonly TextBox _region = Field();
        private readonly TextBox _circuit = Field();
        private readonly ComboBox _panel = new ComboBox { Width = 210, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly TextBox _fr = Field();
        private readonly TextBox _seq = Field();
        private readonly TextBox _originalCable = new TextBox
        {
            Name = "OriginalCableModel",
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = UiTheme.SurfaceAlt
        };
        private readonly TextBox _cable = Field();
        private readonly TextBox _cableMeters = Field();
        private readonly TextBox _diameter = Field();
        private readonly TextBox _downstream = Field();
        private readonly TextBox _upstream = Field();
        private readonly TextBox _detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
        private bool _synchronizing;

        public FillReviewForm(FillReviewData data, List<ListItem> catalogItems = null)
            : this(data, new BoqCatalogIndex(catalogItems), FillPlanningOptions.Default)
        {
        }

        public FillReviewForm(FillReviewData data, BoqCatalogIndex catalog,
            FillPlanningOptions planningOptions)
            : this(data, catalog, planningOptions, false)
        {
        }

        /// <summary>U1U keeps the persisted machine/device identity fixed while editing quantities.</summary>
        public FillReviewForm(FillReviewData data, BoqCatalogIndex catalog,
            FillPlanningOptions planningOptions, bool lockIdentity)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            _catalog = catalog ?? new BoqCatalogIndex(null);
            _planningOptions = planningOptions ?? FillPlanningOptions.Default;
            _lockIdentity = lockIdentity;
            _defaults = Data.Snapshot();

            DialogLayout.Apply(this, ProductMetadata.ProductName + " · 清单确认",
                new Size(1080, 700), new Size(900, 580));

            _cable.Name = "BoqCableModel";
            _cable.ReadOnly = true;
            _cable.BackColor = UiTheme.SurfaceAlt;
            _panel.Items.AddRange(new object[] { "", "I-Line盘", "母线插接口", "插座盘" });
            TabPage basicTab = BuildBasicTab();

            _grid = BuildGrid();
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(4, 4, 4, 2)
            };
            Button addManual = UiTheme.Button("从固定清单添加");
            _replaceItem = UiTheme.Button("替换为固定清单");
            _resolveMissing = UiTheme.Button("选择未匹配项");
            _removeItem = UiTheme.DangerButton("删除选中项");
            Button selectAll = UiTheme.Button("全部勾选");
            Button clearAll = UiTheme.Button("全部取消");
            Button restore = UiTheme.Button("恢复默认");
            _count = new Label { AutoSize = true, Padding = new Padding(12, 7, 0, 0) };
            addManual.Click += AddCatalogItem;
            _replaceItem.Click += ReplaceSelectedItem;
            _resolveMissing.Click += ResolveFirstUnmatched;
            _removeItem.Click += RemoveSelectedItem;
            selectAll.Click += (sender, args) => SetAll(true);
            clearAll.Click += (sender, args) => SetAll(false);
            restore.Click += (sender, args) => RestoreDefaults();
            toolbar.Controls.Add(addManual);
            toolbar.Controls.Add(_replaceItem);
            toolbar.Controls.Add(_resolveMissing);
            toolbar.Controls.Add(_removeItem);
            toolbar.Controls.Add(selectAll);
            toolbar.Controls.Add(clearAll);
            toolbar.Controls.Add(restore);
            toolbar.Controls.Add(_count);

            var listTab = new TabPage("清单选择") { Padding = new Padding(4) };
            listTab.Controls.Add(_grid);
            listTab.Controls.Add(_catalogWarning);
            listTab.Controls.Add(toolbar);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(basicTab);
            _tabs.TabPages.Add(listTab);

            Button ok = UiTheme.PrimaryButton("确认填充");
            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            ok.Click += Confirm;
            FlowLayoutPanel buttons = UiTheme.CommandBar();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            Controls.Add(_tabs);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;

            LoadFromData(Data);
            if (_lockIdentity)
            {
                _machine.ReadOnly = true;
                _machine.BackColor = UiTheme.SurfaceAlt;
                _circuit.ReadOnly = true;
                _circuit.BackColor = UiTheme.SurfaceAlt;
            }
            PopulateRows(Data.Items);
            _cable.TextChanged += CableModelChanged;
            _cableMeters.TextChanged += CableMetersChanged;
            _diameter.TextChanged += FlexibleConduitDiameterChanged;
            _grid.CurrentCellDirtyStateChanged += (sender, args) =>
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += GridValueChanged;
            _grid.SelectionChanged += (sender, args) => UpdateDeleteState();
            UpdateDeleteState();
        }

        public FillReviewData Data { get; private set; }

        private TabPage BuildBasicTab()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 7,
                Padding = new Padding(16, 16, 16, 8),
                AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            AddPair(layout, 0, "机台ID:", _machine, "区域:", _region);
            AddPair(layout, 1, "设备 / 回路:", _circuit, "盘柜类型:", _panel);
            AddPair(layout, 2, "FR:", _fr, "项目序号:", _seq);
            AddPair(layout, 3, "设备原电缆:", _originalCable, "清单电缆:", _cable);
            AddPair(layout, 4, "电缆长度 (m):", _cableMeters, "软管直径:", _diameter);
            AddPair(layout, 5, "下游轴位:", _downstream, "上游轴位:", _upstream);
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
            page.Controls.Add(new Label
            {
                Text = "来源：电缆型号决定软管直径；Ruanguan 决定软管长度；Device_Build20260716 动态状态决定是否输出插座。",
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = UiTheme.Accent,
                Padding = new Padding(14, 5, 14, 3)
            });
            return page;
        }

        private static DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = true,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            };
            UiTheme.StyleGrid(grid);
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.WindowBg;
            grid.RowTemplate.Height = 44;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Included", HeaderText = "生成", Width = 52 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "类别", Width = 80, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = 150, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "特征 / 型号", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Unit", HeaderText = "单位", Width = 58, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "数量 / 长度", Width = 92 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "项目编码", Width = 90, ReadOnly = true });
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
            _originalCable.Text = data.OriginalCableModel ?? "";
            _cable.Text = data.BoqCableModel ?? "";
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
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = item;
                ApplyCatalogState(row, item);
            }
            UpdateCount();
            UpdateCatalogWarning();
            if (Data.Items.Any(item => item.RequiresCatalogConfirmation))
                _tabs.SelectedIndex = 1;
        }

        private void CableModelChanged(object sender, EventArgs e)
        {
            if (_synchronizing) return;
            Data.SetCableModel(_cable.Text, _catalog);
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

        private void FlexibleConduitDiameterChanged(object sender, EventArgs e)
        {
            if (_synchronizing) return;
            FillReviewItem flexible = Data.SetFlexibleConduitDiameter(
                _diameter.Text, _catalog, _planningOptions);
            if (flexible != null) RefreshItem(flexible);
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

        private void AddCatalogItem(object sender, EventArgs e)
        {
            // 连续添加：对话框保持打开，每选中一项立即加入评审表，直到用户关闭。
            using (var form = new ManualListItemForm(_catalog.SelectableItems))
            {
                form.PickRequested += AddCatalogRow;
                if (form.ShowDialog(this) != DialogResult.OK) return;
                if (form.SelectedItem != null)
                    AddCatalogRow(form.SelectedItem, form.Quantity);
            }
        }

        private void AddCatalogRow(ListItem catalogItem, string quantity)
        {
            FillReviewItem item = Data.AddCatalogItem(catalogItem, quantity);
            int index = _grid.Rows.Add(item.Included,
                FillReviewData.CategoryName(item.Category), item.Name,
                item.Description, item.Unit, item.Quantity, item.Code);
            DataGridViewRow row = _grid.Rows[index];
            row.Tag = item;
            ApplyCatalogState(row, item);
            _tabs.SelectedIndex = 1;
            _grid.CurrentCell = row.Cells["Name"];
            UpdateCount();
            UpdateDeleteState();
        }

        private void ReplaceSelectedItem(object sender, EventArgs e)
        {
            if (_grid.CurrentRow?.Tag is FillReviewItem item) OpenCatalogPicker(item);
        }

        private void ResolveFirstUnmatched(object sender, EventArgs e)
        {
            FillReviewItem item = Data.Items.FirstOrDefault(candidate =>
                candidate.RequiresCatalogConfirmation);
            if (item == null) return;
            FocusReviewItem(item);
            OpenCatalogPicker(item);
        }

        private void OpenCatalogPicker(FillReviewItem item)
        {
            string category = FillReviewData.CategoryName(item.Category);
            using (var form = new ManualListItemForm(
                _catalog.SelectableItems, true, category))
            {
                if (form.ShowDialog(this) != DialogResult.OK
                    || form.SelectedItem == null) return;
                string selectedCategory = (form.SelectedItem.Category ?? "").Trim();
                if (item.Category != TableFillCategory.Manual
                    && !string.Equals(selectedCategory, category, StringComparison.Ordinal))
                {
                    MessageBox.Show(this, "只能选择“" + category + "”类别的固定清单项目。",
                        "固定清单类别不一致", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Data.ReplaceWithCatalogItem(item, form.SelectedItem, _catalog);
                if (item.Category == TableFillCategory.Cable)
                {
                    _synchronizing = true;
                    _cable.Text = Data.BoqCableModel;
                    _synchronizing = false;
                }
                RefreshItem(item);
                UpdateDeleteState();
            }
        }

        private void FocusReviewItem(FillReviewItem item)
        {
            _tabs.SelectedIndex = 1;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!ReferenceEquals(row.Tag, item)) continue;
                _grid.CurrentCell = row.Cells["Name"];
                break;
            }
        }

        private void RemoveSelectedItem(object sender, EventArgs e)
        {
            DataGridViewRow row = _grid.CurrentRow;
            if (!(row?.Tag is FillReviewItem item) || !Data.RemoveItem(item)) return;
            _grid.Rows.Remove(row);
            UpdateCount();
            UpdateCatalogWarning();
            UpdateDeleteState();
        }

        private void UpdateDeleteState()
        {
            if (_removeItem == null) return;
            bool hasItem = _grid?.CurrentRow?.Tag is FillReviewItem;
            _replaceItem.Enabled = hasItem;
            _removeItem.Enabled = hasItem;
        }

        private void SetAll(bool included)
        {
            _synchronizing = true;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!(row.Tag is FillReviewItem item)) continue;
                bool value = included && item.CatalogMatched;
                row.Cells["Included"].Value = value;
                item.Included = value;
            }
            _synchronizing = false;
            UpdateCount();
        }

        private void RestoreDefaults()
        {
            Data = _defaults.Snapshot();
            LoadFromData(Data);
            PopulateRows(Data.Items);
        }

        private void Confirm(object sender, EventArgs e)
        {
            // 用户可能取消勾选后立即按“确认填充”；先提交当前脏复选框，再以界面可见值重建最终选择。
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            _grid.EndEdit();
            foreach (DataGridViewRow row in _grid.Rows) ReadGridRow(row);
            if (string.IsNullOrWhiteSpace(_machine.Text) || string.IsNullOrWhiteSpace(_circuit.Text))
            {
                _tabs.SelectedIndex = 0;
                Control invalid = string.IsNullOrWhiteSpace(_machine.Text)
                    ? (Control)_machine : _circuit;
                invalid.Focus();
                MessageBox.Show(this, "机台ID和设备/回路不能为空。", "U1F",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!ValidQuantity(_cableMeters.Text))
            {
                _tabs.SelectedIndex = 0;
                _cableMeters.Focus();
                MessageBox.Show(this, "电缆长度必须是非负数字或留空。", "U1F",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string diameterText = _diameter.Text.Trim();
            if (diameterText.Length > 0
                && !ConduitDiameter.TryNormalize(diameterText, out _))
            {
                _tabs.SelectedIndex = 0;
                _diameter.Focus();
                _diameter.SelectAll();
                MessageBox.Show(this,
                    "软管直径格式无效，请输入数字、DN32、Φ32或32mm等格式。",
                    "U1F", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // 未匹配行的默认未勾选仅用于阻止写出，不代表用户已决定忽略。
            // 必须明确选择固定清单身份或删除该行，避免关键材料被静默遗漏。
            FillReviewItem unmatched = Data.Items.FirstOrDefault(item =>
                item.RequiresCatalogConfirmation);
            if (unmatched != null)
            {
                FocusReviewItem(unmatched);
                string category = FillReviewData.CategoryName(unmatched.Category);
                MessageBox.Show(this, "找不到对应型号的" + category + "："
                    + MissingSubject(unmatched) + "。\r\n\r\n将自动打开“" + category
                    + "”待选页，请从固定清单中选择对应项目。",
                    "固定清单未匹配", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                OpenCatalogPicker(unmatched);
                return;
            }

            for (int index = 0; index < Data.Items.Count; index++)
            {
                FillReviewItem item = Data.Items[index];
                if (!item.Included) continue;
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    _tabs.SelectedIndex = 1;
                    _grid.CurrentCell = _grid.Rows[index].Cells["Name"];
                    _grid.BeginEdit(true);
                    MessageBox.Show(this, "清单项名称不能为空。", "U1F",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (ValidQuantity(item.Quantity)) continue;
                _tabs.SelectedIndex = 1;
                _grid.CurrentCell = _grid.Rows[index].Cells["Quantity"];
                _grid.BeginEdit(true);
                MessageBox.Show(this, "“" + item.Name + "”的数量/长度必须是非负数字或留空。",
                    "U1F", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Data.SetCableMeters(_cableMeters.Text);
            if (!_lockIdentity) Data.Machine.MachineId = _machine.Text.Trim();
            Data.Machine.Region = _region.Text.Trim();
            if (!_lockIdentity) Data.Machine.CircuitName = _circuit.Text.Trim();
            Data.Machine.Next = _panel.Text.Trim();
            Data.Machine.Fr = _fr.Text.Trim();
            Data.Machine.Seq = _seq.Text.Trim();
            // Diameter edits are applied by FlexibleConduitDiameterChanged. Do not
            // re-create a deleted hose row here from the stale text-box value; the
            // presence of a Ruanguan block and the user's row deletion are resolved
            // by FillFeature before/after this dialog.
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
                row.Cells["Included"].Value = item.Included;
                row.Cells["Name"].Value = item.Name;
                row.Cells["Description"].Value = item.Description;
                row.Cells["Unit"].Value = item.Unit;
                row.Cells["Quantity"].Value = item.Quantity;
                row.Cells["Code"].Value = item.Code;
                ApplyCatalogState(row, item);
                _synchronizing = false;
                UpdateCount();
                UpdateCatalogWarning();
                break;
            }
        }

        private static void ApplyCatalogState(
            DataGridViewRow row, FillReviewItem item)
        {
            bool unmatched = item.RequiresCatalogConfirmation;
            row.DefaultCellStyle.BackColor = unmatched
                ? UiTheme.WarningBg
                : UiTheme.Surface;
            row.Cells["Included"].ReadOnly = unmatched;
            if (unmatched)
            {
                item.Included = false;
                row.Cells["Included"].Value = false;
            }
        }

        private string MissingSubject(FillReviewItem item)
        {
            if (item.Category == TableFillCategory.Cable)
            {
                string model = (Data.BoqCableModel ?? "").Trim();
                if (model.Length > 0) return model;
            }
            return string.IsNullOrWhiteSpace(item.Name)
                ? "未提供型号" : item.Name.Trim().Replace(@"\P", " / ");
        }

        private void UpdateCatalogWarning()
        {
            List<string> missing = FillAnomalyDetector.DetectCatalog(Data)
                .Select(anomaly => FillReviewData.CategoryName(anomaly.Category) + "："
                    + anomaly.Subject.Replace("\\P", " / "))
                .Distinct(StringComparer.Ordinal).ToList();
            _catalogWarning.Visible = missing.Count > 0;
            _resolveMissing.Enabled = missing.Count > 0;
            _catalogWarning.Text = missing.Count == 0 ? ""
                : "[BOQ-CATALOG-MISSING] 找不到对应型号："
                    + string.Join("；", missing)
                    + "。点击“选择未匹配项”会自动打开对应类别，或删除不需要的项目。";
            _catalogWarning.AccessibleDescription = _catalogWarning.Text;
            _toolTips.SetToolTip(_catalogWarning, _catalogWarning.Text);
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
            double parsedValue;
            bool parsed = double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parsedValue)
                || double.TryParse(text, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out parsedValue);
            return parsed && parsedValue >= 0 && !double.IsNaN(parsedValue)
                && !double.IsInfinity(parsedValue);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _toolTips.Dispose();
            base.Dispose(disposing);
        }

        private static string Cell(DataGridViewRow row, string name)
            => Convert.ToString(row.Cells[name].Value)?.Trim() ?? "";

        private static TextBox Field() => new TextBox { Dock = DockStyle.Fill };
        private static Label LabelFor(string text) => new Label
        {
            Text = text,
            ForeColor = UiTheme.TextSecondary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
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
