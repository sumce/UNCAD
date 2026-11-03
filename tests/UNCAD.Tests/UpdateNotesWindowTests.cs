using System;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 更新日志窗（D-038，P5 内容阅读）：每个版本可单独查看、
    /// 详情与 VersionChangeLog 同源；空内容必须拒绝构造。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class UpdateNotesWindowTests
    {
        [Fact]
        public void ReleasedNotesGiveEveryVersionItsOwnVisibleDetail()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    VersionChangeLogEntry latest = VersionChangeLog.Entries[0];
                    VersionChangeLogEntry previous = VersionChangeLog.Entries[1];
                    var window = new UpdateNotesWindow(new[] { latest, previous });
                    window.Show();
                    Pump(3);

                    Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                        "Fluent 主题字典必须按窗口合并");
                    Assert.Equal("已更新到 " + ProductMetadata.VersionText, window.Heading);
                    Assert.Equal("知道了", window.CloseButton.Content);

                    // 默认选中第一个（最新）版本，详情与该版本一致。
                    Assert.Equal(2, window.Notes.VersionList.Items.Count);
                    Assert.Same(latest, window.Notes.SelectedVersion);
                    Assert.Equal("版本 " + latest.Version, window.Notes.DetailVersion.Text);
                    Assert.Contains(latest.Changes[0], window.Notes.DetailChanges.Text);
                    Assert.Contains(latest.DateUtc, window.Notes.DetailDate.Text);

                    // 切到上一版本，详情跟随切换。
                    window.Notes.VersionList.SelectedIndex = 1;
                    Pump(2);
                    Assert.Same(previous, window.Notes.SelectedVersion);
                    Assert.Equal("版本 " + previous.Version, window.Notes.DetailVersion.Text);
                    Assert.Contains(previous.Changes[0], window.Notes.DetailChanges.Text);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void EmptyEntries_AreRejectedAtConstruction()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // WPF 控件构造需要 STA；空内容仍必须在构造期拒绝。
                    Assert.Throws<ArgumentException>(
                        () => new UpdateNotesWindow(new VersionChangeLogEntry[0]));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }
    }
}
