using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// Read-only command reference rendered from the help catalog.
    /// 统一用 UiDialogFrame（页头/内容/命令栏）+ UiKit 表格，并支持按关键字过滤；
    /// 帮助内容仍只来自 CommandHelpCatalog，窗体不维护第二份文案。
    /// </summary>
    internal sealed class CommandHelpForm : DpiAwareForm
    {
        private readonly DataGridView _grid;
        private readonly Label _count;

        public CommandHelpForm(IReadOnlyList<CommandHelpEntry> entries)
        {
            DialogLayout.Apply(this, "U1HELP · 命令帮助", new Size(1160, 680),
                new Size(820, 500));

            var frame = new UiDialogFrame(this, "UNCAD 命令帮助",
                "使用方法、功能和注意事项；按命令或关键字搜索。");
            IReadOnlyList<CommandHelpEntry> catalog =
                entries ?? Array.Empty<CommandHelpEntry>();

            _grid = new DataGridView
            {
                Name = "CommandHelpGrid",
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
            UiKit.StyleTable(_grid, readOnly: true);
            AddColumn(_grid, "命令", nameof(CommandHelpEntry.Command), 14);
            AddColumn(_grid, "名称", nameof(CommandHelpEntry.Name), 16);
            AddColumn(_grid, "使用方法", nameof(CommandHelpEntry.Usage), 16);
            AddColumn(_grid, "功能", nameof(CommandHelpEntry.Function), 30);
            AddColumn(_grid, "注意事项", nameof(CommandHelpEntry.Notes), 24);
            foreach (CommandHelpEntry entry in catalog)
            {
                int index = _grid.Rows.Add(entry.Command, entry.Name, entry.Usage,
                    entry.Function, entry.Notes);
                _grid.Rows[index].Tag = entry;
            }
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

            var toolbar = UiKit.Toolbar();
            var search = new TextBox
            {
                Name = "CommandHelpSearch",
                Width = UiTheme.NotAutoScaled(260),
                Margin = new Padding(0, 0, UiTheme.SpaceS, 0)
            };
            UiTheme.StyleInput(search);
            search.TextChanged += (sender, args) => ApplyFilter(search.Text);
            _count = new Label
            {
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(UiTheme.SpaceS, 8, 0, 0)
            };
            toolbar.AddAction(new Label
            {
                Text = "搜索",
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, 8, UiTheme.SpaceS, 0)
            });
            toolbar.AddAction(search);
            toolbar.AddAction(_count);

            // 先加 Fill 的表格，再加 Top 工具栏：Dock 逆序生效，工具栏固定在顶部。
            frame.Body.Controls.Add(_grid);
            frame.Body.Controls.Add(toolbar);
            Button close = frame.AddSecondary("关闭", DialogResult.Cancel);
            frame.Wire(close, close);
            AcceptButton = close;
            CancelButton = close;
            Shown += (sender, args) => close.Select();
            ApplyFilter("");
        }

        private void ApplyFilter(string query)
        {
            string keyword = (query ?? "").Trim();
            int visible = 0;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!(row.Tag is CommandHelpEntry entry)) continue;
                bool match = keyword.Length == 0
                    || Contains(entry.Command, keyword)
                    || Contains(entry.Name, keyword)
                    || Contains(entry.Usage, keyword)
                    || Contains(entry.Function, keyword)
                    || Contains(entry.Notes, keyword);
                row.Visible = match;
                if (match) visible++;
            }
            _count.Text = keyword.Length == 0
                ? "共 " + _grid.Rows.Count + " 条命令"
                : "匹配 " + visible + " / " + _grid.Rows.Count + " 条命令";
        }

        private static bool Contains(string value, string keyword)
            => (value ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void AddColumn(DataGridView grid, string header, string property,
            float weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = property,
                FillWeight = weight,
                MinimumWidth = UiTheme.NotAutoScaled(90),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }
    }
}
