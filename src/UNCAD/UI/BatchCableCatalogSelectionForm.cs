using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>One unresolved cable model in a multi-frame U1U update.</summary>
    public sealed class BatchCableCatalogRequest
    {
        public BatchCableCatalogRequest(string key, string machineId, string deviceName,
            string originalModel, IEnumerable<ListItem> candidates)
        {
            Key = (key ?? "").Trim();
            MachineId = (machineId ?? "").Trim();
            DeviceName = (deviceName ?? "").Trim();
            OriginalModel = (originalModel ?? "").Trim();
            Candidates = (candidates ?? Enumerable.Empty<ListItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code)
                    && !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public string Key { get; }
        public string MachineId { get; }
        public string DeviceName { get; }
        public string OriginalModel { get; }
        public IReadOnlyList<ListItem> Candidates { get; }
    }

    /// <summary>Single dialog for resolving every unmatched cable before batch writes.</summary>
    public sealed class BatchCableCatalogSelectionForm : Form
    {
        private readonly IReadOnlyList<BatchCableCatalogRequest> _requests;
        private readonly DataGridView _grid;
        private readonly Button _confirm;

        public BatchCableCatalogSelectionForm(IEnumerable<BatchCableCatalogRequest> requests)
        {
            _requests = (requests ?? Enumerable.Empty<BatchCableCatalogRequest>()).ToList();
            DialogLayout.Apply(this, "U1U 批量电缆型号确认", new Size(980, 620),
                new Size(760, 460));

            var notice = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(10, 8, 10, 4),
                Text = "以下电缆型号无法直接匹配固定清单。请为每个图框选择型号；全部确认后才会写入 CAD 和 BOQ。",
                AutoEllipsis = true
            };
            _grid = new DataGridView
            {
                Name = "BatchCableCatalogGrid",
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
                Name = "Frame", HeaderText = "机台 / 设备", Width = 250, ReadOnly = true
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Original", HeaderText = "原始型号", Width = 220, ReadOnly = true
            });
            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "Catalog", HeaderText = "固定清单型号", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat,
                DisplayMember = nameof(ListItem.Alias)
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
            AcceptButton = _confirm;
            CancelButton = cancel;

            Populate();
        }

        public IReadOnlyDictionary<string, ListItem> Selections { get; private set; }

        private void Populate()
        {
            _grid.Rows.Clear();
            foreach (BatchCableCatalogRequest request in _requests)
            {
                int rowIndex = _grid.Rows.Add();
                DataGridViewRow row = _grid.Rows[rowIndex];
                row.Tag = request;
                row.Cells["Frame"].Value = request.MachineId + " / " + request.DeviceName;
                row.Cells["Original"].Value = request.OriginalModel.Length == 0
                    ? "未读取到" : request.OriginalModel;
                var cell = (DataGridViewComboBoxCell)row.Cells["Catalog"];
                foreach (ListItem item in request.Candidates)
                    cell.Items.Add(item);
                if (cell.Items.Count > 0) cell.Value = cell.Items[0];
            }
            _confirm.Enabled = _requests.Count > 0
                && _requests.All(request => request.Candidates.Count > 0);
        }

        private void Confirm(object sender, EventArgs e)
        {
            var result = new Dictionary<string, ListItem>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _grid.Rows)
            {
                BatchCableCatalogRequest request = row.Tag as BatchCableCatalogRequest;
                ListItem selected = row.Cells["Catalog"].Value as ListItem;
                if (request == null || selected == null)
                {
                    MessageBox.Show(this, "请为每个未匹配电缆选择固定清单型号。",
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
