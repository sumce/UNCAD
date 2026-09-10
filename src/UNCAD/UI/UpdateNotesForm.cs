using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>Shows update notes after upgrading to a newer UNCAD version.</summary>
    internal sealed class UpdateNotesForm : Form
    {
        public UpdateNotesForm(IReadOnlyList<VersionChangeLogEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("更新日志内容为空。", nameof(entries));

            DialogLayout.Apply(this, "UNCAD 已更新到 " + ProductMetadata.VersionText,
                new Size(560, 460), new Size(460, 320));
            BackColor = UiTheme.WindowBg;

            var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = UiTheme.Surface,
                Padding = new Padding(18, 12, 18, 8) };
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = "已更新到 " + ProductMetadata.VersionText,
                Font = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            });
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "以下是本次更新带来的变化（U1A 可随时查看版本信息）。",
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(0, 4, 0, 0)
            });

            var list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(16, 10, 10, 6)
            };
            foreach (VersionChangeLogEntry entry in entries)
            {
                list.Controls.Add(BuildEntry(entry));
                list.Controls.Add(new Panel { Dock = DockStyle.Top, Width = 8,
                    Height = 8, Margin = new Padding(0) });
            }

            Button close = UiTheme.PrimaryButton("知道了", DialogResult.Cancel);
            var commands = UiTheme.CommandBar();
            commands.Padding = new Padding(0, 7, 12, 2);
            commands.Controls.Add(close);

            Controls.Add(list);
            Controls.Add(commands);
            Controls.Add(header);
            AcceptButton = close;
            CancelButton = close;
            Shown += (sender, args) => close.Select();
        }

        private static Control BuildEntry(VersionChangeLogEntry entry)
        {
            var block = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = 480, Padding = new Padding(0), Margin = new Padding(0, 0, 6, 0) };
            var title = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Text = entry.Version + "（" + entry.DateUtc + "）",
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.BrandBlue,
                Margin = new Padding(0, 0, 0, 4)
            };
            block.Controls.Add(title);
            for (int index = entry.Changes.Length - 1; index >= 0; index--)
            {
                var line = new Label
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Text = "· " + entry.Changes[index],
                    Font = UiTheme.FontBody,
                    ForeColor = UiTheme.TextPrimary,
                    Margin = new Padding(8, 0, 0, 3)
                };
                block.Controls.Add(line);
            }
            return block;
        }
    }
}
