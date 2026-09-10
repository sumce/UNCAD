using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>One unresolved material in a multi-frame U1U update, with category-limited candidates.</summary>
    internal sealed class BatchCatalogRequest
    {
        public BatchCatalogRequest(string key, string machineId, string deviceName,
            string category, string originalModel, IEnumerable<ListItem> candidates)
        {
            Key = (key ?? "").Trim();
            MachineId = (machineId ?? "").Trim();
            DeviceName = (deviceName ?? "").Trim();
            Category = (category ?? "").Trim();
            OriginalModel = (originalModel ?? "").Trim();
            Candidates = (candidates ?? Enumerable.Empty<ListItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code)
                    && !string.IsNullOrWhiteSpace(item.Name)
                    && string.Equals(item.Category?.Trim(), Category, StringComparison.Ordinal))
                .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public string Key { get; }
        public string MachineId { get; }
        public string DeviceName { get; }
        public string Category { get; }
        public string OriginalModel { get; }
        public IReadOnlyList<ListItem> Candidates { get; }
    }

    /// <summary>Collects explicit catalog selections from passed-in requests before batch writes.</summary>
    internal sealed class BatchCatalogSelectionForm : Form
    {
        private readonly IReadOnlyList<BatchCatalogRequest> _requests;
        private readonly DataGridView _grid;
        private readonly Button _confirm;

        public BatchCatalogSelectionForm(IEnumerable<BatchCatalogRequest> requests)
        {
            _requests = (requests ?? Enumerable.Empty<BatchCatalogRequest>()).ToList();
            DialogLayout.Apply(this, "U1U 未匹配规格选择", new Size(1060, 620),
                new Size(840, 460));

            var notice = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(
                    10, 8,
                    10, 4),
                Text = "未匹配项目 " + _requests.Count + " 项，替代规格待确认。",
                AutoEllipsis = true
            };
            _grid = new DataGridView
            {
                Name = "BatchCatalogGrid",
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnEnter,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                BackgroundColor = UiTheme.Surface,
                GridColor = UiTheme.Border
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Frame", HeaderText = "机台 / 回路", Width = 250, ReadOnly = true
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Category", HeaderText = "项目", Width = 110, ReadOnly = true
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Original", HeaderText = "原型号 / 配电信息", Width = 220, ReadOnly = true
            });
            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "Catalog", HeaderText = "替代固定清单规格", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 150,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat,
                DisplayMember = nameof(ListItem.Alias),
                ValueMember = nameof(ListItem.Code),
                ValueType = typeof(string)
            });

            _confirm = UiTheme.PrimaryButton("确认全部选择");
            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            _confirm.Click += Confirm;
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Controls.Add(cancel);
            commands.Controls.Add(_confirm);

            Controls.Add(_grid);
            Controls.Add(notice);
            Controls.Add(commands);
            UiTheme.StyleGrid(_grid);
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.CurrentCellDirtyStateChanged += (sender, args) =>
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += (sender, args) => UpdateConfirmation();
            AcceptButton = _confirm;
            CancelButton = cancel;

            Populate();
        }

        public IReadOnlyDictionary<string, ListItem> Selections { get; private set; }

        private void Populate()
        {
            _grid.Rows.Clear();
            foreach (BatchCatalogRequest request in _requests)
            {
                int rowIndex = _grid.Rows.Add();
                DataGridViewRow row = _grid.Rows[rowIndex];
                row.Tag = request;
                row.Cells["Frame"].Value = request.MachineId + " / " + request.DeviceName;
                row.Cells["Category"].Value = request.Category;
                row.Cells["Original"].Value = request.OriginalModel.Length == 0
                    ? "未读取到" : request.OriginalModel;
                var cell = (DataGridViewComboBoxCell)row.Cells["Catalog"];
                foreach (ListItem item in request.Candidates)
                    cell.Items.Add(item);
            }
            UpdateConfirmation();
        }

        private void UpdateConfirmation()
        {
            _confirm.Enabled = _requests.Count > 0
                && _grid.Rows.Count == _requests.Count
                && _grid.Rows.Cast<DataGridViewRow>().All(row =>
                    SelectedItem(row) != null);
        }

        private static ListItem SelectedItem(DataGridViewRow row)
        {
            if (!(row.Tag is BatchCatalogRequest request)
                || !(row.Cells["Catalog"].Value is string code)) return null;
            return request.Candidates.FirstOrDefault(item =>
                string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        private void Confirm(object sender, EventArgs e)
        {
            _grid.EndEdit();
            var result = new Dictionary<string, ListItem>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _grid.Rows)
            {
                BatchCatalogRequest request = row.Tag as BatchCatalogRequest;
                ListItem selected = SelectedItem(row);
                if (request == null || selected == null)
                {
                    MessageBox.Show(this, "仍有未匹配项目未选择替代规格。",
                        "U1U", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                result[request.Key] = selected;
            }
            Selections = result;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
