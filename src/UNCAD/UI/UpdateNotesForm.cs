using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>Shows update notes after upgrading to a newer UNCAD version.</summary>
    internal sealed class UpdateNotesForm : DpiAwareForm
    {
        public UpdateNotesForm(IReadOnlyList<VersionChangeLogEntry> entries)
        {
            ValidateEntries(entries);

            DialogLayout.Apply(this, "UNCAD 已更新到 " + ProductMetadata.VersionText,
                new Size(720, 520), new Size(560, 380));
            BackColor = UiTheme.WindowBg;

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = UiTheme.Surface,
                Padding = new Padding(18, 10, 18, 8),
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "已更新到 " + ProductMetadata.VersionText,
                Font = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "选择左侧版本查看更新详情；U1A 可随时查看全部版本记录。",
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(0, 4, 0, 0)
            }, 0, 1);

            Control pages = CreateVersionPages(entries);

            Button close = UiTheme.PrimaryButton("知道了", DialogResult.Cancel);
            var commands = UiTheme.CommandBar();
            commands.Padding = new Padding(0, 7, 12, 2);
            commands.Controls.Add(close);

            Controls.Add(pages);
            Controls.Add(commands);
            Controls.Add(header);
            AcceptButton = close;
            CancelButton = close;
            Shown += (sender, args) =>
            {
                close.Select();
            };
        }

        internal static Control CreateVersionPages(
            IReadOnlyList<VersionChangeLogEntry> entries)
        {
            ValidateEntries(entries);
            var layout = new TableLayoutPanel
            {
                Name = "UpdateVersionPages",
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBg,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(UiTheme.SpaceM)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var versions = new ListBox
            {
                Name = "UpdateVersionList",
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.FontBody,
                IntegralHeight = false,
                Margin = new Padding(0, 0, UiTheme.SpaceM, 0)
            };
            var host = new Panel
            {
                Name = "UpdatePageHost",
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBg,
                Margin = new Padding(0)
            };
            foreach (VersionChangeLogEntry entry in entries)
            {
                versions.Items.Add(entry.Version);
                host.Controls.Add(BuildVersionPage(entry));
            }
            versions.SelectedIndexChanged += (sender, args) =>
                ShowVersionPage(host, Convert.ToString(versions.SelectedItem));
            layout.Controls.Add(versions, 0, 0);
            layout.Controls.Add(host, 1, 0);
            versions.SelectedIndex = 0;
            return layout;
        }

        private static Panel BuildVersionPage(VersionChangeLogEntry entry)
        {
            var page = new Panel
            {
                Name = "UpdateVersion_" + entry.Version.Replace('.', '_'),
                Tag = entry.Version,
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBg,
                Padding = new Padding(UiTheme.SpaceXL),
                Visible = false
            };
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, UiTheme.SpaceL)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = "版本 " + entry.Version,
                Font = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0)
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = "发布日期 " + entry.DateUtc + " · " + entry.Changes.Length
                    + " 项更新",
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, UiTheme.SpaceXS, 0, UiTheme.SpaceL)
            }, 0, 1);

            var details = new RichTextBox
            {
                Name = "UpdateDetails",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = false,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.WindowBg,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.FontBody,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                TabStop = false,
                Text = string.Join(Environment.NewLine + Environment.NewLine,
                    entry.Changes.Select(change => "· " + change))
            };
            page.Controls.Add(details);
            page.Controls.Add(header);
            return page;
        }

        private static void ShowVersionPage(Panel host, string version)
        {
            if (host == null || string.IsNullOrWhiteSpace(version)) return;
            foreach (Control page in host.Controls) page.Visible = false;
            Control selected = host.Controls.Cast<Control>()
                .FirstOrDefault(page => Equals(page.Tag, version));
            if (selected == null) return;
            selected.Visible = true;
            selected.BringToFront();
            RichTextBox details = selected.Controls.OfType<RichTextBox>()
                .FirstOrDefault();
            if (details == null) return;
            details.SelectionStart = 0;
            details.ScrollToCaret();
        }

        private static void ValidateEntries(IReadOnlyList<VersionChangeLogEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("更新日志内容为空。", nameof(entries));
        }
    }
}
