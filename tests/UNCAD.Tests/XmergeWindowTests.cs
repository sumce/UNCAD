using System;
using System.IO;
using System.Linq;
using System.Threading;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF Xmerge 选择窗（D-038，P2 模式）：递归扫描只收 DWG、去重排序、
    /// 移除选中、空列表不得确认（权威校验在按钮里）。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class XmergeWindowTests : IDisposable
    {
        private readonly string _root = CreateTempRoot();

        [Fact]
        public void Window_StartsEmptyAndBlocksConfirmWithoutFiles()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new XmergeWindow();
                    window.Show();
                    Pump(3);

                    Assert.Empty(window.Files);
                    Assert.Equal("将文件或文件夹拖到此处", window.HintText.Text);
                    Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                        "Fluent 主题字典必须按窗口合并");
                    Assert.True(window.MergeButton.IsDefault, "开始合并必须是默认按钮");
                    Assert.True(window.CancelButton.IsCancel, "取消必须是取消按钮");

                    // 空列表：确认被拦下，窗口不带着 Confirmed 关闭。
                    Click(window.MergeButton);
                    Pump(2);
                    Assert.False(window.Confirmed);
                    Assert.True(window.WarningBar.IsOpen);
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
        public void AddPaths_ScansFoldersRecursivelyAndKeepsDwgOnly()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    string first = WriteDwg("top.dwg");
                    string second = WriteDwg(Path.Combine("sub", "nested.dwg"));
                    WriteDwg("ignored.txt");
                    var window = new XmergeWindow();
                    window.Show();
                    Pump(2);

                    window.AddPaths(new[] { _root, first });
                    Pump(2);

                    Assert.Equal(2, window.Files.Count);
                    Assert.Contains(second, window.Files, StringComparer.OrdinalIgnoreCase);
                    Assert.Equal("已添加 2 个 DWG 文件", window.HintText.Text);

                    Click(window.MergeButton);
                    Pump(2);
                    Assert.True(window.Confirmed);
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
        public void RemoveSelected_RemovesOnlyChosenFiles()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    string first = WriteDwg("first.dwg");
                    string second = WriteDwg("second.dwg");
                    var window = new XmergeWindow();
                    window.Show();
                    Pump(2);

                    window.AddPaths(new[] { first, second });
                    window.FileList.SelectedItem = window.Files[0];
                    Click(window.RemoveButton);
                    Pump(2);

                    Assert.Single(window.Files);
                    Assert.Contains(second, window.Files, StringComparer.OrdinalIgnoreCase);
                    Assert.Equal("已添加 1 个 DWG 文件", window.HintText.Text);
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
        public void ClosingDuringInitialScan_IgnoresLateDiscoveryCleanly()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    WriteDwg("queued.dwg");
                    var window = new XmergeWindow(new[] { _root });
                    window.Show();
                    // Close immediately while Loaded async scan may still be running.
                    window.Close();
                    Pump(5);
                    Assert.False(window.Confirmed);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private string WriteDwg(string relative)
        {
            string path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "stub");
            return path;
        }

        private static string CreateTempRoot()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "UNCAD.XmergeWindowTests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Click(System.Windows.Controls.Primitives.ButtonBase button)
        {
            // Wpf.Ui 的 Button 不是原生 Button，AutomationPeer 不认；
            // 直接抬 ClickEvent 路由事件，与用户点击走同一条处理链。
            Assert.NotNull(button);
            button.RaiseEvent(new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }
    }
}
