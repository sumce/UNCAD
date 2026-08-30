using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace UNCAD.UI
{
    public sealed class XLayoutMachineSummary
    {
        public XLayoutMachineSummary(string machineId)
        {
            MachineId = machineId ?? "";
        }

        public string MachineId { get; }
        public int CircuitCount { get; set; }
    }

    internal sealed class XLayoutSummaryForm : Form
    {
        public XLayoutSummaryForm(IReadOnlyList<XLayoutMachineSummary> summaries,
            int frameCount)
        {
            DialogLayout.Apply(this, "XLAYOUT · 排版统计", new Size(560, 420),
                new Size(460, 320), false);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 62,
                BackColor = Color.White,
                Padding = new Padding(16, 10, 16, 8)
            };
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "排版完成  ·  " + frameCount + " 个图框  /  "
                    + (summaries?.Count ?? 0) + " 个机台",
                Font = new Font("微软雅黑", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 43, 52),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "机台ID",
                DataPropertyName = nameof(XLayoutMachineSummary.MachineId),
                FillWeight = 68
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "回路数量",
                DataPropertyName = nameof(XLayoutMachineSummary.CircuitCount),
                FillWeight = 32,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight
                }
            });
            grid.DataSource = (summaries ?? Array.Empty<XLayoutMachineSummary>()).ToList();

            Button close = DialogLayout.CommandButton("关闭", DialogResult.Cancel);
            FlowLayoutPanel commands = DialogLayout.CommandBar();
            commands.Controls.Add(close);
            Controls.Add(grid);
            Controls.Add(commands);
            Controls.Add(header);
            AcceptButton = close;
            CancelButton = close;
            Shown += (sender, args) => close.Select();
        }
    }
}
