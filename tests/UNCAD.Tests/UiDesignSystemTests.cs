using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 2026-11-03 UI/UX 修缮（窗口自适应 + WPF 统一设计令牌）：
    ///   ① UiShell.ApplyFluentTheme 给每个窗口合并 FluentKit 设计令牌，并把窗口
    ///      尺寸钳制到工作区 92%、未定位窗口居中（SizeToContent 的边不碰）；
    ///   ② 11 个 U1SET 页面全部走 UncadCard 等令牌（卡片边距/内边距一致）；
    ///   ③ U1F 机台窗：相似建议挂 Popup 浮层不顶布局，回路列表带筛选与计数；
    ///   ④ 结果窗长文本列省略号 + 悬浮提示。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class UiDesignSystemTests
    {
        // ---------- 运行时：令牌真实生效 ----------

        [Fact]
        public void FluentKit_AppliesUniformCardSpacingOnBomPage()
        {
            RunSta(() =>
            {
                var kit = new FluentKit();
                Assert.True(kit.Contains("UncadCard"),
                    "FluentKit 字典里没有 UncadCard（XAML 未加载）");
                var window = new SettingsWindow(6);
                window.Show();
                Pump(3);
                List<Wpf.Ui.Controls.Card> cards = FindAll<Wpf.Ui.Controls.Card>(
                    window.BomPage);
                Assert.Equal(2, cards.Count);
                Assert.All(cards, card =>
                {
                    Assert.Equal(new Thickness(0, 0, 0, 12), card.Margin);
                    Assert.Equal(new Thickness(18, 16, 18, 16), card.Padding);
                });
                window.Close();
            });
        }

        [Fact]
        public void Window_FitsWorkAreaAndCentersWhenUnpositioned()
        {
            RunSta(() =>
            {
                var window = new MachinePickerWindow(
                    new[] { "M100" }, id => new List<Core.Excel.MachineRow>());
                // 超大尺寸 → 钳制回工作区（至少不大于工作区宽）。
                window.Width = SystemParameters.WorkArea.Width * 8;
                window.Height = SystemParameters.WorkArea.Height * 8;
                UiShell.ApplyFluentTheme(window);
                Assert.True(window.Width <= SystemParameters.WorkArea.Width + 0.5);
                Assert.True(window.Height <= SystemParameters.WorkArea.Height + 0.5);
                Assert.Equal(WindowStartupLocation.CenterScreen,
                    window.WindowStartupLocation);

                // SizeToContent=Height 的窗口（DeviceReserve）不碰高度。
                var contentWindow = new SettingsWindow(0);
                contentWindow.SizeToContent = SizeToContent.Height;
                double before = contentWindow.Height;
                UiShell.ApplyFluentTheme(contentWindow);
                Assert.Equal(SizeToContent.Height, contentWindow.SizeToContent);
                Assert.True(double.IsNaN(contentWindow.Height)
                    || contentWindow.Height == before
                    || contentWindow.Height <= before);
                contentWindow.Close();
                window.Close();
            });
        }

        // ---------- 运行时：U1F 筛选 / 浮层 ----------

        [Fact]
        public void CircuitFilter_FiltersGridAndUpdatesCount()
        {
            RunSta(() =>
            {
                var window = new MachinePickerWindow(
                    new[] { "M100" },
                    id => string.Equals(id, "M100", StringComparison.OrdinalIgnoreCase)
                        ? new List<Core.Excel.MachineRow>
                        {
                            new Core.Excel.MachineRow
                            {
                                Region = "LAB", MachineId = "M100",
                                CircuitName = "设备A", Next = "插座盘",
                                Detail = "U220 1P3W"
                            },
                            new Core.Excel.MachineRow
                            {
                                Region = "LAB", MachineId = "M100",
                                CircuitName = "设备B", Next = "I-Line盘",
                                Detail = "N480 3P4W"
                            }
                        }
                        : new List<Core.Excel.MachineRow>());
                window.Show();
                Pump(3);

                window.CommitMachineForTest("M100");
                Pump(2);
                Assert.Equal(2, VisibleCircuitCount(window));
                Assert.Contains("共 2 个回路", window.CircuitCountText.Text);

                window.CircuitFilterBox.Text = "I-Line";
                Pump(2);
                Assert.Equal(1, VisibleCircuitCount(window));
                Assert.Contains("显示 1 / 2", window.CircuitCountText.Text);

                window.CircuitFilterBox.Text = "";
                Pump(2);
                Assert.Equal(2, VisibleCircuitCount(window));
                Assert.Contains("共 2 个回路", window.CircuitCountText.Text);
                window.Close();
            });
        }

        [Fact]
        public void Suggestions_OpenAsPopupOverlay_NotPushingLayout()
        {
            RunSta(() =>
            {
                var window = new MachinePickerWindow(
                    new[] { "M100", "M101" }, id => new List<Core.Excel.MachineRow>());
                window.Show();
                Pump(3);

                window.MachineInput.Text = "M";
                Pump(2);
                Assert.Equal(Visibility.Visible, window.MachineSuggest.Visibility);
                Assert.True(window.SuggestPopup.IsOpen);

                // 浮层不占布局：清除输入后浮层关闭，摘要行保持原位可见。
                window.MachineInput.Clear();
                Pump(2);
                Assert.Equal(Visibility.Collapsed, window.MachineSuggest.Visibility);
                Assert.False(window.SuggestPopup.IsOpen);
                Assert.Equal("请输入机台 ID", window.MachineSummary.Text);
                window.Close();
            });
        }

        // ---------- 3.0.0.1 事故回归：页面宿主与命中 ----------

        [Fact]
        public void SettingsPages_HostedInContentFrame_NotInOverlay()
        {
            // 3.0.0.1 曾把页面挂到 Navi.ContentOverlay——那是全窗口浮层
            // （Snackbar 语义，模板里 RowSpan=2 压在内容区上方），整个 U1SET
            // 点不动。页面必须挂进模板的内容 Frame（可交互内容区）。
            RunSta(() =>
            {
                var window = new SettingsWindow(5);
                window.Show();
                Pump(4);
                window.ShowPage(5);

                var presenter = window.Navigation.Template?.FindName(
                    "PART_NavigationViewContentPresenter", window.Navigation)
                    as System.Windows.Controls.Frame;
                Assert.NotNull(presenter);
                Assert.Same(window.MachinePage, presenter.Content);
                Assert.Null(window.Navigation.ContentOverlay);
                window.ShowPage(6);
                Pump(3); // Frame 内容更新经调度器，泵一帧再断言。
                Assert.Same(window.BomPage, presenter.Content);
                window.Close();
            });
        }

        [Fact]
        public void SettingsPageButton_IsTopmostHitTarget()
        {
            // 模拟真实点击：命中测试落在刷新按钮中心，最顶层必须是按钮自己
            //（或其子元素），否则页面再好看也是"什么都点不动"。
            RunSta(() =>
            {
                var window = new SettingsWindow(5);
                window.Show();
                Pump(4);
                window.ShowPage(5);
                Pump(3);

                var button = window.MachinePage.RefreshButton;
                Pump(2);
                Assert.True(button.IsVisible && button.ActualWidth > 0);
                var rootElement = (System.Windows.UIElement)window.Content;
                var rootVisual = (Visual)window.Content;
                // HitTest 的点必须落在被测根视觉的坐标系里。
                Point relative = button.TranslatePoint(
                    new Point(button.ActualWidth / 2, button.ActualHeight / 2),
                    rootElement);
                bool hitButton = false;
                string hitChain = "";
                VisualTreeHelper.HitTest(rootVisual, null,
                    result =>
                    {
                        var node = result.VisualHit as DependencyObject;
                        hitChain = node?.GetType().Name ?? "null";
                        for (var walker = node; walker != null;
                            walker = VisualTreeHelper.GetParent(walker))
                        {
                            if (ReferenceEquals(walker, button))
                            {
                                hitButton = true;
                                return HitTestResultBehavior.Stop;
                            }
                        }
                        return HitTestResultBehavior.Continue;
                    },
                    new PointHitTestParameters(relative));
                Assert.True(hitButton,
                    "刷新按钮被更高层元素遮挡；命中顶层=" + hitChain
                    + "；按钮位置=" + relative);
                window.Close();
            });
        }

        // ---------- 源级契约 ----------

        [Fact]
        public void SourceContract_KitMergedPagesUseTokensWindowsAdapt()
        {
            string shell = ReadSource("UiShell.cs");
            Assert.Contains("new FluentKit()", shell);
            Assert.Contains("AdaptWindowToWorkArea", shell);
            Assert.Contains("SizeToContent", shell);
            Assert.Contains("WindowStartupLocation.CenterScreen", shell);

            // 12 个设置页 + 机台选择窗都挂了卡片令牌（观感一致）。
            string[] tokenPages =
            {
                "Settings/SettingsOverviewPage.xaml",
                "Settings/SettingsDrawPage.xaml",
                "Settings/SettingsStatisticsPage.xaml",
                "Settings/SettingsStylePage.xaml",
                "Settings/SettingsStartupPage.xaml",
                "Settings/SettingsMachinePage.xaml",
                "Settings/SettingsBomPage.xaml",
                "Settings/SettingsDownstreamPage.xaml",
                "Settings/SettingsColorPage.xaml",
                "Settings/SettingsOutputPage.xaml",
                // 3.6.0（D-057）：图框与排版
                "Settings/SettingsFramePage.xaml",
                "Settings/SettingsLogsPage.xaml"
            };
            foreach (string page in tokenPages)
            {
                Assert.Contains("DynamicResource UncadCard", ReadSource(page));
            }
        }

        [Fact]
        public void SourceContract_PickerOverlayAndFilterTrimmedResultGrids()
        {
            string picker = ReadSource("MachinePickerWindow.xaml");
            Assert.Contains("SuggestPopup", picker);
            Assert.Contains("CircuitFilterBox", picker);
            Assert.Contains("CircuitCountText", picker);
            Assert.Contains("TextTrimming", picker);
            string pickerCode = ReadSource("MachinePickerWindow.xaml.cs");
            Assert.Contains("ApplyCircuitFilter", pickerCode);
            Assert.Contains("UpdateCircuitCount", pickerCode);

            foreach (string window in new[]
                     {
                         "BatchUpdateResultWindow.xaml", "DwgExportResultWindow.xaml",
                         "PdfExportResultWindow.xaml", "XstsWindow.xaml",
                         "XLayoutSummaryWindow.xaml"
                     })
            {
                Assert.Contains("TextTrimming", ReadSource(window));
            }
        }

        // ---------- 辅助（与 MachinePickerWindowTests 同一套） ----------

        private static int VisibleCircuitCount(MachinePickerWindow window)
            => window.CircuitGrid.Items.Count;

        private static string SourcePath(string relative)
            => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "UNCAD", "UI", "Wpf", relative));

        private static string ReadSource(string relative)
            => File.ReadAllText(SourcePath(relative));

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
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }

        private static List<T> FindAll<T>(DependencyObject root) where T : class
        {
            var result = new List<T>();
            Collect(root, result);
            return result;
        }

        private static void Collect<T>(DependencyObject current, List<T> result)
            where T : class
        {
            if (current == null) return;
            if (current is T match) result.Add(match);
            System.Collections.IEnumerator children;
            try { children = System.Windows.LogicalTreeHelper.GetChildren(current)
                    .GetEnumerator(); }
            catch (Exception) { return; }
            while (true)
            {
                object child;
                try { if (!children.MoveNext()) break; child = children.Current; }
                catch (Exception) { break; }
                if (child is DependencyObject dependencyChild)
                    Collect(dependencyChild, result);
            }
        }
    }
}
