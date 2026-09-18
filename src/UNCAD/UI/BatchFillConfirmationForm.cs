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

            var header = UiTheme.Header(
                "将按图框边界批量更新 " + rows.Count + " 个已填图框",
                "所有图框将在一个事务中写入，任一失败则整批回滚。");

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
                BackgroundColor = UiTheme.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowTemplate = { Height = UiTheme.NotAutoScaled(28) }
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "图框",
                DataPropertyName = nameof(BatchFillConfirmationRow.FrameHandle),
                FillWeight = 16
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "机台 ID",
                DataPropertyName = nameof(BatchFillConfirmationRow.MachineId),
                FillWeight = 22
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "设备 / 回路",
                DataPropertyName = nameof(BatchFillConfirmationRow.DeviceName),
                FillWeight = 28
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Device",
                DataPropertyName = nameof(BatchFillConfirmationRow.DeviceState),
                FillWeight = 18
            });
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
            UiTheme.StyleGrid(grid);

            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            Button confirm = UiTheme.PrimaryButton("确认更新", DialogResult.OK);
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Controls.Add(cancel);
            commands.Controls.Add(confirm);

            Controls.Add(grid);
            Controls.Add(commands);
            Controls.Add(header);
            AcceptButton = confirm;
            CancelButton = cancel;
            Shown += (sender, args) => confirm.Select();
        }
    }
}
