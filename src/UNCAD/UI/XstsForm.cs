using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Stat;

namespace UNCAD.UI
{
    /// <summary>Review window for XSTS circuit coverage before/after Excel export.</summary>
    public sealed class XstsForm : Form
    {
        public XstsForm(XstsReport report, string outputPath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            DialogLayout.Apply(this, "XSTS · 机台回路统计", new Size(820, 540),
                new Size(680, 420));
            Panel header = UiTheme.Header("机台回路统计 · " + report.Machines.Count
                + " 个机台 / " + report.FrameCount + " 张图纸",
                "已框选回路 " + report.SelectedCircuitCount + " / 这些机台应有 "
                + report.ExpectedCircuitCount + " · 缺少 " + report.MissingCircuitCount + " 个回路");
            var grid = new DataGridView
            {
                Name = "XstsGrid",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = UiTheme.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "机台 ID", DataPropertyName = nameof(XstsMachineSummary.MachineId), FillWeight = 25 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "已选回路", DataPropertyName = nameof(XstsMachineSummary.SelectedCircuitCount), FillWeight = 15 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "应有回路", DataPropertyName = nameof(XstsMachineSummary.ExpectedCircuitCount), FillWeight = 15 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "缺少回路", DataPropertyName = nameof(XstsMachineSummary.MissingText), FillWeight = 45 });
            grid.DataSource = report.Machines.ToList();
            UiTheme.StyleGrid(grid);
            var pathLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(outputPath) ? "未导出 Excel" : "Excel: " + outputPath,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(UiTheme.SpaceS, UiTheme.SpaceS, UiTheme.SpaceS, 0)
            };
            Button close = UiTheme.Button("关闭", DialogResult.Cancel);
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Controls.Add(close);
            Controls.Add(grid);
            Controls.Add(pathLabel);
            Controls.Add(commands);
            Controls.Add(header);
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
