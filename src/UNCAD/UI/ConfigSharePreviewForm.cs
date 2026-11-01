using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 配置分享码预览窗：展示来源产品/版本/生成时间，以及“将变更”的配置项。
    /// 只有用户在这里点“导入”才会写配置；取消不改变本机任何设置。
    /// </summary>
    internal sealed class ConfigSharePreviewForm : DpiAwareForm
    {
        public ConfigSharePreviewForm(ConfigSharePreview preview)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            DialogLayout.Apply(this, ProductMetadata.ProductName + " · 配置分享预览",
                new Size(760, 560), new Size(640, 460), false);

            var frame = new UiDialogFrame(this, "配置分享预览",
                "确认要导入的配置项；相同值的键不会出现在变更列表里。");

            string created = preview.CreatedUtc.LocalDateTime.ToString(
                "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var summary = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, UiTheme.SpaceM)
            };
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            summary.Controls.Add(Metric("来源", preview.Product + " " + preview.Version), 0, 0);
            summary.Controls.Add(Metric("生成时间", created), 1, 0);
            summary.Controls.Add(Metric("可应用配置", preview.Items.Count + " 项"), 0, 1);
            summary.Controls.Add(Metric("文件格式", "分享码 v" + preview.FormatVersion), 1, 1);

            var grid = new DataGridView
            {
                Name = "ConfigShareChanges",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.None
            };
            UiKit.StyleTable(grid, readOnly: true);
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "配置键",
                Width = UiTheme.NotAutoScaled(190),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "本机当前值",
                Width = UiTheme.NotAutoScaled(130),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "将导入值",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = UiTheme.NotAutoScaled(160),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            foreach (ConfigShareChange change in preview.Changes)
            {
                int index = grid.Rows.Add(change.Key,
                    change.CurrentValue.Length == 0 ? "（未设置）" : change.CurrentValue,
                    change.SharedValue);
                grid.Rows[index].Tag = change;
            }
            UiTheme.FixGridRows(grid, 30);

            var empty = UiKit.EmptyState("分享码与本机配置一致",
                "没有需要变更的配置项；导入不会改动本机设置。");
            empty.Dock = DockStyle.Fill;
            empty.Visible = preview.Changes.Count == 0;
            grid.Visible = preview.Changes.Count > 0;

            var listHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };
            listHost.Controls.Add(empty);
            listHost.Controls.Add(grid);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(summary, 0, 0);
            body.Controls.Add(listHost, 0, 1);

            Button cancel = frame.AddSecondary("取消", DialogResult.Cancel);
            Button import = frame.AddPrimary("导入 " + preview.Changes.Count + " 项");
            import.Enabled = preview.Items.Count > 0;
            frame.Wire(import, cancel);
            if (!import.Enabled)
            {
                // 没有可应用项时不允许“导入”，但仍然允许关闭窗口。
                import.Visible = false;
            }

            frame.Body.Controls.Add(body);
            frame.Body.Controls.Add(new UiBanner(UiTone.Info,
                "分享码不包含授权账号、时钟水位线和界面记忆；导入后请在 U1SET 中按需复核。"));
            AcceptButton = import;
            CancelButton = cancel;
            Shown += (sender, args) => cancel.Select();
        }

        private static Control Metric(string label, string value)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, UiTheme.SpaceM, UiTheme.SpaceS)
            };
            panel.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0)
            }, 0, 0);
            panel.Controls.Add(new Label
            {
                Text = value ?? "",
                AutoSize = true,
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0, UiTheme.SpaceXXS, 0, 0)
            }, 0, 1);
            return panel;
        }
    }
}
