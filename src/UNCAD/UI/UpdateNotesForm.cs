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
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("更新日志内容为空。", nameof(entries));

            DialogLayout.Apply(this, "UNCAD 已更新到 " + ProductMetadata.VersionText,
                new Size(560, 460), new Size(460, 320));
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
                Text = "以下是本次更新带来的变化（U1A 可随时查看版本信息）。",
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(0, 4, 0, 0)
            }, 0, 1);

            var notes = new RichTextBox
            {
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
                Text = FormatEntries(entries)
            };

            Button close = UiTheme.PrimaryButton("知道了", DialogResult.Cancel);
            var commands = UiTheme.CommandBar();
            commands.Padding = new Padding(0, 7, 12, 2);
            commands.Controls.Add(close);

            Controls.Add(notes);
            Controls.Add(commands);
            Controls.Add(header);
            AcceptButton = close;
            CancelButton = close;
            Shown += (sender, args) =>
            {
                notes.SelectionStart = 0;
                notes.ScrollToCaret();
                close.Select();
            };
        }

        internal static string FormatEntries(IReadOnlyList<VersionChangeLogEntry> entries)
        {
            return string.Join(Environment.NewLine + Environment.NewLine,
                entries.Select(entry => entry.Version + "（" + entry.DateUtc + "）"
                    + Environment.NewLine + string.Join(Environment.NewLine,
                        entry.Changes.Select(change => "· " + change))));
        }
    }
}
