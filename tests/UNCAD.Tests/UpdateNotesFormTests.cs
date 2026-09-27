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
        public void ReleasedNotesHaveVisibleNonEmptyBody()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    VersionChangeLogEntry latest = VersionChangeLog.Entries[0];
                    using var form = new UpdateNotesForm(new[] { latest });
                    form.CreateControl();
                    form.PerformLayout();

                    RichTextBox body = form.Controls.OfType<RichTextBox>().Single();
                    Assert.True(body.Width > 0 && body.Height > 0, body.Bounds.ToString());
                    Assert.Contains(latest.Version, body.Text);
                    Assert.Contains(latest.Changes[0], body.Text);

                    TableLayoutPanel header = form.Controls.OfType<TableLayoutPanel>().Single();
                    Label title = header.Controls.OfType<Label>()
                        .Single(label => label.Text.StartsWith("已更新到 "));
                    Label subtitle = header.Controls.OfType<Label>()
                        .Single(label => label.Text.StartsWith("以下是本次更新"));
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
