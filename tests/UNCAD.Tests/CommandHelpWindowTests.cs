using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 命令帮助窗（D-038，P5 内容阅读：左侧命令列表 + 右侧说明）：
    /// 结构、主题、搜索过滤与选中联动。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class CommandHelpWindowTests
    {
        [Fact]
        public void Window_ListsCatalogAndShowsSelectedEntryDetail()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new CommandHelpWindow(CommandHelpCatalog.All);
                    window.Show();
                    Pump(3);

                    Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                        "Fluent 主题字典必须按窗口合并");
                    Assert.Equal(CommandHelpCatalog.All.Count, window.CommandList.Items.Count);
                    Assert.Equal("共 " + CommandHelpCatalog.All.Count + " 条命令",
                        window.Count.Text);
                    Assert.True(window.SearchBox != null, "必须提供搜索框");
                    Assert.Equal("关闭", window.CloseButton.Content);
                    Assert.True(window.CloseButton.IsCancel, "关闭必须是取消按钮（Esc 触发）");

                    // 选中第一条：右侧必须同步展示同一条目的全部字段。
                    CommandHelpEntry first = CommandHelpCatalog.All[0];
                    Assert.Same(first, window.CommandList.SelectedItem);
                    Assert.Equal(first.Command, window.DetailCommand.Text);
                    Assert.Equal(first.Name, window.DetailName.Text);
                    Assert.Equal(first.Usage, window.DetailUsage.Text);
                    Assert.Equal(first.Function, window.DetailFunction.Text);
                    Assert.Equal(first.Notes, window.DetailNotes.Text);
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
        public void Search_FiltersAcrossAllFieldsWithMatchCount()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new CommandHelpWindow(CommandHelpCatalog.All);
                    window.Show();
                    Pump(2);

                    // 搜索命中所有字段（与旧窗语义一致，不区分大小写）。
                    string keyword = "标注";
                    int expected = CommandHelpCatalog.All.Count(entry =>
                        (entry.Command ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                        || (entry.Name ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                        || (entry.Usage ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                        || (entry.Function ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                        || (entry.Notes ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                    Assert.True(expected > 0, "测试关键字必须至少命中一条");

                    window.ApplyFilter(keyword);
                    Pump(2);
                    Assert.Equal(expected, window.CommandList.Items.Count);
                    Assert.Equal("匹配 " + expected + " / " + CommandHelpCatalog.All.Count
                        + " 条命令", window.Count.Text);
                    Assert.NotNull(window.CommandList.SelectedItem);

                    // 清空关键字回到全量。
                    window.ApplyFilter("");
                    Assert.Equal(CommandHelpCatalog.All.Count, window.CommandList.Items.Count);
                    window.Close();
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
