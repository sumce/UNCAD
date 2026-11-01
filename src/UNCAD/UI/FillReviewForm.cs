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
    public sealed class FillReviewForm : DpiAwareForm
    {
        private readonly FillReviewData _defaults;
        private readonly DataGridView _grid;
        private readonly TabControl _tabs;
        private readonly Label _count;
        private readonly Button _replaceItem;
        private readonly Button _resolveMissing;
        private readonly Button _removeItem;
        private readonly ToolTip _toolTips = new ToolTip();
        // 未匹配型号提示：统一用警告横幅（底色 + 左侧色条 + 自动换行），
        // 不再用固定高度 Label，避免高 DPI 下文字被裁掉。
        private readonly UiBanner _catalogWarning =
            UiKit.Banner(UiTone.Warning, "");
        private readonly UiProgress _progress = UiKit.Progress();
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
        private readonly TextBox _measuredCable = Field();
        private readonly TextBox _deviceReserve = Field();
        private readonly TextBox _panelReserve = Field();
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

            _catalogWarning.Visible = false;
            _grid = BuildGrid();
            var toolbar = UiKit.Toolbar();
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
            toolbar.AddAction(addManual);
            toolbar.AddAction(_replaceItem);
            toolbar.AddAction(_resolveMissing);
            toolbar.AddAction(_removeItem);
            toolbar.AddSeparator();
            toolbar.AddAction(selectAll);
            toolbar.AddAction(clearAll);
            toolbar.AddAction(restore);
            toolbar.AddAction(_count);
            // 进度条：已勾选行数 / 总行数，让“将生成多少”不止是文字。
            _progress.Width = UiTheme.NotAutoScaled(160);
            _progress.Minimum = 0;
            _progress.Maximum = 1;
            _progress.Margin = new Padding(UiTheme.SpaceM, 14, 0, 0);
            toolbar.AddAction(_progress);

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
            Controls.Add(UiKit.PageHeader("清单确认",
                "核对机台/回路身份与清单行；未匹配型号必须显式替换或删除后再确认。"));
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
            _deviceReserve.TextChanged += DeviceReserveChanged;
            _diameter.TextChanged += FlexibleConduitDiameterChanged;
            _grid.CurrentCellDirtyStateChanged += (sender, args) =>
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += GridValueChanged;
            _grid.SelectionChanged += (sender, args) => UpdateDeleteState();
            DpiChanged += (sender, args) => UiTheme.FixGridRows(_grid, 44);
            UpdateDeleteState();
        }

        public FillReviewData Data { get; private set; }

        /// <summary>基础信息：按“身份 / 电缆与预留 / 软管与轴位”三张卡片组织。</summary>
        private TabPage BuildBasicTab()
        {
            var page = new TabPage("基础信息")
            {
                Padding = new Padding(0),
                BackColor = UiTheme.WindowBg,
                UseVisualStyleBackColor = false
            };
            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.WindowBg
            };
            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = UiTheme.WindowBg,
                Padding = new Padding(UiTheme.SpaceXL, UiTheme.SpaceL,
                    UiTheme.SpaceXL, UiTheme.SpaceXL),
                Margin = new Padding(0)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.Controls.Add(UiKit.Banner(UiTone.Info,
                "来源：电缆型号决定软管直径；Ruanguan 决定软管长度；"
                + "Device_Build20260716 动态状态决定是否输出插座。"), 0, 0);
            stack.Controls.Add(UiKit.Section("机台与回路")
                .AddBodyTop(PairGrid(
                    "机台ID", _machine, "区域", _region,
                    "设备 / 回路", _circuit, "盘柜类型", _panel,
                    "FR", _fr, "项目序号", _seq)), 0, 1);
            stack.Controls.Add(UiKit.Section("电缆与预留",
                "电缆总长 = 实测 + 盘内/箱内预留 + 设备内预留（毫米）；只读值是图框/统计结果。")
                .AddBodyTop(PairGrid(
                    "设备原电缆", _originalCable, "清单电缆", _cable,
                    "实测电缆 (m)", _measuredCable, "电缆长度 (m)", _cableMeters,
                    "设备内预留 (mm)", _deviceReserve, "盘内/箱内 (mm)", _panelReserve)), 0, 2);
            stack.Controls.Add(UiKit.Section("软管与轴位")
                .AddBodyTop(PairGrid(
                    "软管直径", _diameter, "下游轴位", _downstream,
                    "", new Label(), "上游轴位", _upstream))
                .AddBodyTop(BuildDetailRow()), 0, 3);
            scroll.Controls.Add(stack);
            page.Controls.Add(scroll);
            return page;
        }

        private Control BuildDetailRow()
        {
            _detail.Dock = DockStyle.Fill;
            _detail.Height = 84;
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, UiTheme.SpaceXS, 0, 0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            Label label = LabelFor("配电详情");
            label.TextAlign = ContentAlignment.TopRight;
            label.Padding = new Padding(0, 6, 0, 0);
            layout.Controls.Add(label, 0, 0);
            layout.Controls.Add(_detail, 1, 0);
            return layout;
        }

        /// <summary>两列“标签 + 输入”网格：标签自适应，输入按百分比拉伸。</summary>
        private static TableLayoutPanel PairGrid(params object[] pairs)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int index = 0; index + 3 < pairs.Length; index += 4)
            {
                int row = index / 4;
                grid.RowCount = row + 1;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                string rightLabel = Convert.ToString(pairs[index + 2]);
                grid.Controls.Add(LabelFor(Convert.ToString(pairs[index])), 0, row);
                grid.Controls.Add(PrepareEditor((Control)pairs[index + 1],
                    new Padding(0, 4, UiTheme.SpaceXL, 4)), 1, row);
                if (rightLabel.Length > 0)
                    grid.Controls.Add(LabelFor(rightLabel), 2, row);
                grid.Controls.Add(PrepareEditor((Control)pairs[index + 3],
                    new Padding(0, 4, 0, 4)), 3, row);
            }
            return grid;
        }

        private static Control PrepareEditor(Control control, Padding margin)
        {
            if (control == null) return new Label();
            control.Margin = margin;
            if (control is TextBox || control is TableLayoutPanel || control is Panel)
                control.Dock = DockStyle.Fill;
            else
                control.Anchor = AnchorStyles.Left;
            return control;
        }

        private static DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                // The review grid is a fixed-format checklist. Letting a user
                // drag one row, or letting wrapped text auto-grow it, makes the
                // table drift from the CAD BOQ layout and from row to row.
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            };
            UiTheme.StyleGrid(grid);
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.WindowBg;
            grid.RowTemplate.Height = UiTheme.NotAutoScaled(44);
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Included", HeaderText = "生成", Width = UiTheme.NotAutoScaled(52) });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "类别", Width = UiTheme.NotAutoScaled(80), ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = UiTheme.NotAutoScaled(150), ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "特征 / 型号", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = UiTheme.NotAutoScaled(260), ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Unit", HeaderText = "单位", Width = UiTheme.NotAutoScaled(58), ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "数量 / 长度", Width = UiTheme.NotAutoScaled(92) });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "项目编码", Width = UiTheme.NotAutoScaled(90), ReadOnly = true });
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
            _measuredCable.Text = data.MeasuredCableMeters ?? "";
            _deviceReserve.Text = data.DeviceReserveMm ?? "";
            _panelReserve.Text = data.PanelReserveMm ?? "";
            _measuredCable.ReadOnly = true;
            _measuredCable.BackColor = UiTheme.SurfaceAlt;
            _panelReserve.ReadOnly = true;
            _panelReserve.BackColor = UiTheme.SurfaceAlt;
            _deviceReserve.Enabled = data.CableReserveEditable;
            _deviceReserve.BackColor = data.CableReserveEditable
                ? UiTheme.Surface : UiTheme.SurfaceAlt;
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
            UiTheme.FixGridRows(_grid, 44);
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

        private void DeviceReserveChanged(object sender, EventArgs e)
        {
            if (_synchronizing) return;
            Data.DeviceReserveMm = _deviceReserve.Text;
            Data.SyncCableReserveMeters();
            _synchronizing = true;
            _cableMeters.Text = Data.CableMeters ?? "";
            _synchronizing = false;
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
            UiTheme.FixGridRows(_grid, 44);
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
            ListItem selected = ShowWinFormsCatalogPicker(category);
            if (selected == null) return;
            ApplyCatalogReplacement(item, category, selected);
        }

        private void ApplyCatalogReplacement(FillReviewItem item, string category, ListItem selected)
        {
            string selectedCategory = (selected.Category ?? "").Trim();
            if (item.Category != TableFillCategory.Manual
                && !string.Equals(selectedCategory, category, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "只能选择“" + category + "”类别的固定清单项目。",
                    "固定清单类别不一致", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Data.ReplaceWithCatalogItem(item, selected, _catalog);
            if (item.Category == TableFillCategory.Cable)
            {
                _synchronizing = true;
                _cable.Text = Data.BoqCableModel;
                _synchronizing = false;
            }
            RefreshItem(item);
            UpdateDeleteState();
        }

        private ListItem ShowWinFormsCatalogPicker(string category)
        {
            using (var form = new ManualListItemForm(_catalog.SelectableItems, true, category))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.SelectedItem == null)
                    return null;
                return form.SelectedItem;
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
            UiTheme.FixGridRows(_grid, 44);
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
            Data.DeviceReserveMm = (_deviceReserve.Text ?? "").Trim();
            if (Data.CableReserveEditable && Data.DeviceReserveMm.Length > 0
                && (!double.TryParse(Data.DeviceReserveMm,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double reserve)
                    || reserve < 0d || reserve > 100000d))
            {
                _tabs.SelectedIndex = 0;
                _deviceReserve.Focus();
                _deviceReserve.SelectAll();
                MessageBox.Show(this, "设备内预留必须是 0~100000 的毫米数。", "U1U",
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
            _count.ForeColor = selected == Data.Items.Count
                ? UiTheme.SuccessFg : UiTheme.TextSecondary;
            _progress.Maximum = Math.Max(1, Data.Items.Count);
            _progress.Value = Math.Min(selected, _progress.Maximum);
            _progress.Tone = selected == Data.Items.Count ? UiTone.Success : UiTone.Info;
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

    }
}
