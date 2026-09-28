using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class UpdateNotesFormTests
    {
        [Fact]
        public void ReleasedNotesGiveEveryVersionItsOwnVisiblePage()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    VersionChangeLogEntry latest = VersionChangeLog.Entries[0];
                    VersionChangeLogEntry previous = VersionChangeLog.Entries[1];
                    using var form = new UpdateNotesForm(new[] { latest, previous });
                    form.Show();
                    form.PerformLayout();
                    Application.DoEvents();

                    TableLayoutPanel pages = form.Controls
                        .OfType<TableLayoutPanel>()
                        .Single(panel => panel.Name == "UpdateVersionPages");
                    ListBox versions = pages.Controls.OfType<ListBox>().Single();
                    Panel host = pages.Controls.OfType<Panel>()
                        .Single(panel => panel.Name == "UpdatePageHost");
                    Assert.Equal(new[] { latest.Version, previous.Version }, versions.Items
                        .Cast<string>());

                    for (int index = 0; index < versions.Items.Count; index++)
                    {
                        versions.SelectedIndex = index;
                        Application.DoEvents();
                        VersionChangeLogEntry entry = index == 0 ? latest : previous;
                        Panel page = host.Controls.OfType<Panel>()
                            .Single(control => Equals(control.Tag, entry.Version));
                        Assert.True(page.Visible);
                        RichTextBox body = page.Controls.OfType<RichTextBox>().Single();
                        Assert.True(body.Width > 0 && body.Height > 0,
                            entry.Version + ": " + body.Bounds);
                        Assert.Contains(entry.Changes[0], body.Text);
                        Assert.Contains(page.Controls.OfType<TableLayoutPanel>()
                            .SelectMany(panel => panel.Controls.OfType<Label>()),
                            label => label.Text == "版本 " + entry.Version);
                    }

                    TableLayoutPanel header = form.Controls.OfType<TableLayoutPanel>()
                        .Single(panel => panel.Controls.OfType<Label>()
                            .Any(label => label.Text.StartsWith("已更新到 ")));
                    Label title = header.Controls.OfType<Label>()
                        .Single(label => label.Text.StartsWith("已更新到 "));
                    Label subtitle = header.Controls.OfType<Label>()
                        .Single(label => label.Text.StartsWith("选择左侧版本"));
                    Assert.True(title.Width > 0 && title.Height > 0, title.Bounds.ToString());
                    Assert.True(subtitle.Top >= title.Bottom,
                        title.Bounds + " overlaps " + subtitle.Bounds);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }
    }
}
