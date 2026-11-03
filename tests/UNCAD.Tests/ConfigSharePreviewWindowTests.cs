using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 配置分享预览窗（3.0 WPF 版，P3 事务确认）：断言从旧
    /// UiKitTests.ConfigSharePreviewForm_ShowsChangesAndKeepsCancelAvailable
    /// 保义迁移——取消始终可用、变更列表按（键/当前值/导入值）展示、
    /// 没有可应用项时“导入”不可用。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class ConfigSharePreviewWindowTests
    {
        [Fact]
        public void ShowsChangesAndKeepsCancelAvailable()
        {
            RunSta(() =>
            {
                ConfigSharePreviewWindow window = Window(Preview(
                    items: new Dictionary<string, string> { { "UNC_UNL_HEIGHT", "220" } },
                    changes: new List<ConfigShareChange>
                    {
                        new ConfigShareChange("UNC_UNL_HEIGHT", "180", "220")
                    }));
                window.Show();
                Pump(2);

                Assert.Equal("取消", Convert.ToString(window.CancelButton.Content));
                string import = Convert.ToString(window.ImportButton.Content);
                Assert.StartsWith("导入", import);
                Assert.True(window.ImportButton.IsVisible);

                System.Collections.IList rows = window.ConfigShareChanges.Items;
                Assert.Single(rows);
                var row = (ConfigShareRow)rows[0];
                Assert.Equal("UNC_UNL_HEIGHT", row.Key);
                Assert.Equal("180", row.Current);
                Assert.Equal("220", row.Shared);
                window.Close();
            });
        }

        [Fact]
        public void EmptyCurrentValueRendersAsNotSet()
        {
            RunSta(() =>
            {
                ConfigSharePreviewWindow window = Window(Preview(
                    items: new Dictionary<string, string> { { "UNC_UNL_HEIGHT", "180" } },
                    changes: new List<ConfigShareChange>
                    {
                        new ConfigShareChange("UNC_UNL_HEIGHT", "", "180")
                    }));
                window.Show();
                Pump(2);
                var row = (ConfigShareRow)window.ConfigShareChanges.Items[0];
                Assert.Equal("（未设置）", row.Current);
                window.Close();
            });
        }

        [Fact]
        public void HidesImport_WhenNoApplicableItems()
        {
            RunSta(() =>
            {
                // 没有可应用项时不允许“导入”，但仍然允许关闭窗口。
                ConfigSharePreviewWindow window = Window(Preview(
                    items: new Dictionary<string, string>(),
                    changes: new List<ConfigShareChange>()));
                window.Show();
                Pump(2);
                Assert.Equal(System.Windows.Visibility.Collapsed,
                    window.ImportButton.Visibility);
                Assert.True(window.CancelButton.IsVisible);
                // 与本机一致：显示空状态而不是空表格。
                Assert.Equal(System.Windows.Visibility.Visible,
                    window.EmptyPanel.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed,
                    window.ConfigShareChanges.Visibility);
                window.Close();
            });
        }

        [Fact]
        public void RejectsNullPreview()
        {
            RunSta(() =>
            {
                Assert.Throws<ArgumentNullException>(() =>
                {
                    var window = new ConfigSharePreviewWindow(null);
                    window.Close();
                });
            });
        }

        // ---------- 辅助 ----------

        private static ConfigSharePreviewWindow Window(ConfigSharePreview preview)
            => new ConfigSharePreviewWindow(preview);

        private static ConfigSharePreview Preview(
            Dictionary<string, string> items, List<ConfigShareChange> changes)
            => new ConfigSharePreview("UNCAD Pro", "2.10.0", 1,
                DateTimeOffset.Now, items, changes, Array.Empty<string>());

        private static void RunSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
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
