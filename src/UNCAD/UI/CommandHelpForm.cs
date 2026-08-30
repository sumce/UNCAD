using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>Read-only command reference rendered from the help catalog.</summary>
    internal sealed class CommandHelpForm : Form
    {
        public CommandHelpForm(IReadOnlyList<CommandHelpEntry> entries)
        {
            DialogLayout.Apply(this, "U1HELP · 命令帮助", new Size(1120, 650),
                new Size(760, 460));

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 58,
                BackColor = Color.White,
                Padding = new Padding(16, 9, 16, 7)
            };
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "UNCAD 命令帮助  ·  使用方法、功能和注意事项",
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
                MultiSelect = false,
                RowTemplate = { Height = 34 }
            };
            AddColumn(grid, "命令", nameof(CommandHelpEntry.Command), 14);
            AddColumn(grid, "名称", nameof(CommandHelpEntry.Name), 16);
            AddColumn(grid, "使用方法", nameof(CommandHelpEntry.Usage), 16);
            AddColumn(grid, "功能", nameof(CommandHelpEntry.Function), 30);
            AddColumn(grid, "注意事项", nameof(CommandHelpEntry.Notes), 24);
            grid.DataSource = (entries ?? Array.Empty<CommandHelpEntry>()).ToList();
            grid.CellFormatting += (sender, args) =>
            {
                if (args.RowIndex >= 0) args.CellStyle.WrapMode = DataGridViewTriState.True;
            };
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

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

        private static void AddColumn(DataGridView grid, string header, string property, float weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = property,
                FillWeight = weight,
                MinimumWidth = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }
    }
}
