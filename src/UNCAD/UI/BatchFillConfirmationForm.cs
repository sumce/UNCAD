using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace UNCAD.UI
{
    /// <summary>One row shown before a multi-frame U1U update is committed.</summary>
    public sealed class BatchFillConfirmationRow
    {
        public BatchFillConfirmationRow(string frameHandle, string machineId,
            string deviceName, string deviceState, int rowCount)
        {
            FrameHandle = frameHandle ?? "";
            MachineId = machineId ?? "";
            DeviceName = deviceName ?? "";
            DeviceState = deviceState ?? "";
            RowCount = rowCount;
        }

        public string FrameHandle { get; }
        public string MachineId { get; }
        public string DeviceName { get; }
        public string DeviceState { get; }
        public int RowCount { get; }
    }

    /// <summary>Scrollable confirmation window for the complete multi-frame U1U plan.</summary>
    public sealed class BatchFillConfirmationForm : DpiAwareForm
    {
        public BatchFillConfirmationForm(IReadOnlyList<BatchFillConfirmationRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            DialogLayout.Apply(this, "U1U 批量确认", new Size(980, 640),
                new Size(820, 500));

            var frame = new UiDialogFrame(this,
                "将按图框边界批量更新 " + rows.Count + " 个已填图框",
                "所有图框将在一个事务中写入，任一失败则整批回滚；请逐行核对机台和回路。");

            var grid = new DataGridView
            {
                Name = "BatchFillConfirmationGrid",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.None
            };
            UiKit.StyleTable(grid, readOnly: true);
            AddColumn(grid, "图框", nameof(BatchFillConfirmationRow.FrameHandle), 16);
            AddColumn(grid, "机台 ID", nameof(BatchFillConfirmationRow.MachineId), 22);
            AddColumn(grid, "设备 / 回路", nameof(BatchFillConfirmationRow.DeviceName), 28);
            AddColumn(grid, "Device", nameof(BatchFillConfirmationRow.DeviceState), 18);
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "清单项",
                DataPropertyName = nameof(BatchFillConfirmationRow.RowCount),
                FillWeight = 16,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight
                }
            });
            grid.DataSource = rows.ToList();
            UiTheme.FixGridRows(grid, 30);

            Button confirm = frame.AddPrimary("确认更新", DialogResult.OK);
            Button cancel = frame.AddSecondary("取消", DialogResult.Cancel);
            frame.Body.Controls.Add(grid);
            frame.Wire(confirm, cancel);
            AcceptButton = confirm;
            CancelButton = cancel;
            Shown += (sender, args) => confirm.Select();
        }

        private static void AddColumn(DataGridView grid, string header,
            string property, float weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = property,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }
    }
}
