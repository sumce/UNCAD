using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UNCAD.Infra;
using UNCAD.UI;
using Wpf.Ui.Controls;
using Xunit;
using DataGrid = System.Windows.Controls.DataGrid;
using TextBlock = System.Windows.Controls.TextBlock;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 设置中心（D-038，P1 导航枢纽）：NavigationView 侧边导航承载旧
    /// UnifiedSettingsForm 的 11 页；断言从旧 UnifiedSettingsFormTests 逐条保义
    /// 迁移（页结构、字段存在、范围、保存/校验语义），WPF 本身 DPI 无关故不再
    /// 断言 WinForms 缩放；About/OnlineLicense 旧断言已由对应窗体自己的测试接管。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class SettingsWindowTests
    {
        // ---------- 导航结构 ----------

        [Fact]
        public void Navigation_PreservesElevenPageTitlesAndInitialSelection()
        {
            RunSta(() =>
            {
                // 11 个导航项与旧 11 页一一对应，顺序不变。
                string[] titles = SettingsWindow.PageTitles;
                Assert.Equal(new[]
                {
                    "概览", "绘制标注", "统计汇总", "文字样式", "界面与启动",
                    "机台数据", "清单与表格", "Downstream 自动添加", "颜色",
                    "输出与分享", "日志与记录"
                }, titles);

                for (int index = 0; index < titles.Length; index++)
                {
                    var window = new SettingsWindow(index);
                    window.Show();
                    Pump(3);
                    var items = window.Navigation.MenuItems.OfType<NavigationViewItem>()
                        .ToList();
                    Assert.Equal(11, items.Count);
                    Assert.Equal(titles, items.Select(item =>
                        Convert.ToString(item.Content)).ToArray());
                    // 构造参数决定初始导航页（与旧 SelectedIndex 语义一致），越界钳回。
                    Assert.Equal(index, window.SelectedPageIndex);
                    window.Close();
                }

                var clampedHigh = new SettingsWindow(99);
                clampedHigh.Show();
                Pump(2);
                Assert.Equal(10, clampedHigh.SelectedPageIndex);
                clampedHigh.Close();

                var clampedLow = new SettingsWindow(-3);
                clampedLow.Show();
                Pump(2);
                Assert.Equal(0, clampedLow.SelectedPageIndex);
                clampedLow.Close();
            });
        }

        // ---------- 3.0.0.4 回归：侧边栏真实点击必须切换页面 ----------

        [Fact]
        public void Sidebar_RealClick_SwitchesPage()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(0);
                window.Show();
                Pump(3);
                Assert.Equal(0, window.SelectedPageIndex);

                NavigationViewItem item = window.Navigation.MenuItems
                    .OfType<NavigationViewItem>()
                    .First(candidate => Convert.ToString(candidate.Content) == "统计汇总");
                Assert.True(item.IsVisible, "侧边栏项必须可见");

                // 模拟真实点击：鼠标点击最终触发 ButtonBase.OnClick()（库在
                // NavigationViewItem.OnClick 里通知 NavigationView 并 base.OnClick
                // 抬 Click 事件）。库的 AutomationPeer 不支持 Invoke 模式
                // （4.0.2 只支持 ItemContainer），所以用反射调 OnClick——与
                // 鼠标点击同一条完整路径。旧实现订阅 ItemInvoked 并按项断言
                // OriginalSource，但 4.0.2 raise 时 OriginalSource 是
                // NavigationView 自己，永远拿不到被点项（2026-11-04 用户报告
                // "侧边栏点不动"）。
                System.Reflection.MethodInfo onClick = item.GetType().GetMethod(
                    "OnClick",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(onClick);
                onClick.Invoke(item, Array.Empty<object>());
                Pump(3);

                Assert.Equal(2, window.SelectedPageIndex);
                window.Close();
            });
        }

        // ---------- 各页字段与范围 ----------

        [Fact]
        public void StatisticsPage_UsesFlatCardsAndKeepsAllControls()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(2);
                window.Show();
                Pump(3);
                SettingsStatisticsPage page = window.StatPage;
                Assert.Equal(2, window.SelectedPageIndex);

                List<Wpf.Ui.Controls.Card> cards = FindAll<Wpf.Ui.Controls.Card>(page);
                Assert.Equal(3, cards.Count);
                List<ToggleSwitch> switches = FindAll<ToggleSwitch>(page);
                Assert.Equal(6, switches.Count);
                Assert.Contains(switches, toggle =>
                    Convert.ToString(toggle.Content) == "尺寸标注文字 (DIMENSION)");
                Assert.Equal(6, switches.Count(toggle => toggle.IsVisible));

                List<NumberBox> numbers = FindAll<NumberBox>(page);
                Assert.Equal(2, numbers.Count);
                Assert.All(numbers, box =>
                {
                    Assert.Equal(1, box.Minimum);
                    Assert.Equal(100000, box.Maximum);
                });
                window.Close();
            });
        }

        [Fact]
        public void StartupPage_ExposesSplashSwitchAndVersion()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(4);
                window.Show();
                Pump(3);
                Assert.Equal(4, window.SelectedPageIndex);
                List<ToggleSwitch> switches = FindAll<ToggleSwitch>(window.StartupPage);
                Assert.Contains(switches, toggle =>
                    Convert.ToString(toggle.Content).Contains("启动"));
                Assert.Contains(FindAll<TextBlock>(window.StartupPage), block =>
                    (block.Text ?? "").Contains(ProductMetadata.VersionLabel));

                // 文字样式页没有开关（启动开关已独立成页）。
                Assert.Empty(FindAll<ToggleSwitch>(window.StylePage));
                window.Close();
            });
        }

        [Fact]
        public void DrawPage_ListsOnlyRigidCatalogModels()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(1);
                window.Show();
                Pump(3);
                SettingsDrawPage page = window.DrawPage;
                Assert.Equal(1, window.SelectedPageIndex);

                ComboBox diameter = page.ConduitDiameter;
                Assert.NotNull(diameter);
                Assert.True(diameter.IsEditable);
                Assert.Equal(new[] { "20", "25", "38", "51" },
                    diameter.Items.Cast<ComboBoxItem>()
                        .Select(item => Convert.ToString(item.Content)).ToArray());
                Assert.DoesNotContain("75", diameter.Items.Cast<ComboBoxItem>()
                    .Select(item => Convert.ToString(item.Content)));
                Assert.DoesNotContain("100", diameter.Items.Cast<ComboBoxItem>()
                    .Select(item => Convert.ToString(item.Content)));

                // 数值范围与旧 NumericUpDown 一致。
                Assert.Equal(1, page.UnlHeight.Minimum);
                Assert.Equal(100000, page.UnlHeight.Maximum);
                Assert.Equal(0, page.UnlOffset.Minimum);
                Assert.Equal(1, page.UnrDiameter.Minimum);

                // 2.10.0 已移除“默认规格”：U1Q1/U1Q2/U1Q4 各自传固定规格，改了不生效。
                Assert.DoesNotContain(FindAll<TextBlock>(page),
                    block => block.Text == "默认规格");
                Assert.DoesNotContain(FindAll<System.Windows.Controls.TextBox>(page),
                    box => (box.Text ?? "").Contains("桥架"));
                Assert.Equal("2000mm", page.UnlText.Text);
                Assert.Contains(FindAll<TextBlock>(page), block => block.Text == "文字高度");
                window.Close();
            });
        }

        [Fact]
        public void MachinePage_ShowsDataSourceSnapshotAndPicker()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(5);
                window.Show();
                Pump(3);
                SettingsMachinePage page = window.MachinePage;
                Assert.Equal(5, window.SelectedPageIndex);
                // D-041：数据源卡 + 快照状态卡，两张卡片。
                Assert.Equal(2, FindAll<Wpf.Ui.Controls.Card>(page).Count);
                Assert.Equal("浏览…", Convert.ToString(page.BrowseExcelButton.Content));
                Assert.Equal("刷新", Convert.ToString(page.RefreshButton.Content));
                Assert.False(string.IsNullOrWhiteSpace(
                    page.MachineSnapshotStatus.Text));
                Assert.False(string.IsNullOrWhiteSpace(page.SourceTypeText.Text));
                window.Close();
            });
        }

        [Fact]
        public void BomPage_ShowsTableParametersAndIndependentAutofillCategories()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(6);
                window.Show();
                Pump(3);
                SettingsBomPage page = window.BomPage;
                Assert.Equal(6, window.SelectedPageIndex);
                Assert.Equal(2, FindAll<Wpf.Ui.Controls.Card>(page).Count);

                // 表格参数 5 项 + 电缆盘内/箱内预留 3 项（2.10.0 可配置）。
                List<NumberBox> numbers = FindAll<NumberBox>(page);
                Assert.Equal(8, numbers.Count);
                Assert.Equal(1, page.FillClearRows.Minimum);
                Assert.Equal(100, page.FillClearRows.Maximum);
                Assert.Equal(1, page.FillTableRow.Minimum);
                Assert.Equal(1000, page.FillTableRow.Maximum);
                Assert.All(numbers, box => Assert.True(box.Minimum >= 0));
                List<TextBlock> labels = FindAll<TextBlock>(page);
                Assert.Contains(labels, block => block.Text == "每次清空数据行数");
                Assert.Contains(labels, block => block.Text == "软管手动数量（m）");
                Assert.Contains(labels, block => block.Text == "I-Line 盘内预留（mm）");
                Assert.Contains(numbers, box => box.Value == 3400);
                Assert.Contains(numbers, box => box.Value == 1000);
                Assert.Contains(numbers, box => box.Value == 500);
                Assert.All(new[] { page.CableReserveILine, page.CableReserveSocket,
                        page.CableReserveBusbar }, box =>
                {
                    Assert.Equal(0, box.Minimum);
                    Assert.Equal(100000, box.Maximum);
                });

                // 8 个自动填充类别 + 整机回路校验 + 设备预留自动识别（D-043）。
                List<ToggleSwitch> autoFill = FindAll<ToggleSwitch>(page);
                Assert.Equal(10, autoFill.Count);
                List<ToggleSwitch> categoryBoxes = autoFill
                    .Where(toggle => !Convert.ToString(toggle.Content)
                        .StartsWith("整机回路校验")
                        && !Convert.ToString(toggle.Content)
                            .StartsWith("设备内预留自动识别")).ToList();
                Assert.Equal(new[]
                {
                    "电缆", "断路器", "电盘", "软管",
                    "线管", "桥架", "插座盘", "插接箱"
                }, categoryBoxes.Select(toggle => Convert.ToString(toggle.Content))
                    .ToArray());
                Assert.Contains(autoFill, toggle =>
                    Convert.ToString(toggle.Content).StartsWith("整机回路校验"));
                Assert.DoesNotContain(autoFill, toggle =>
                    Convert.ToString(toggle.Content).Contains("未匹配"));
                window.Close();
            });
        }

        [Fact]
        public void DownstreamPage_KeepsRatingsAndRulesTogether()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(7);
                window.Show();
                Pump(3);
                SettingsDownstreamPage page = window.DownstreamPage;
                Assert.Equal(7, window.SelectedPageIndex);
                Assert.Equal(3, FindAll<Wpf.Ui.Controls.Card>(page).Count);
                List<NumberBox> numbers = FindAll<NumberBox>(page);
                Assert.Equal(2, numbers.Count);
                Assert.Equal(1, page.FillDownstreamAmps.Minimum);
                Assert.Equal(1000, page.FillDownstreamAmps.Maximum);
                Assert.Equal(1, page.FillDownstreamPanelAmps.Minimum);
                Assert.Equal(10000, page.FillDownstreamPanelAmps.Maximum);
                // 独立开关（与通用类别开关解耦）
                List<ToggleSwitch> switches = FindAll<ToggleSwitch>(page);
                Assert.Equal(2, switches.Count);
                Assert.Contains(switches, toggle =>
                    Convert.ToString(toggle.Content).Contains("socket box"));
                Assert.Contains(switches, toggle =>
                    Convert.ToString(toggle.Content).Contains("I-line_Panel"));
                List<TextBlock> labels = FindAll<TextBlock>(page);
                Assert.Contains(labels, block => block.Text == "插座箱额定电流（A）");
                Assert.Contains(labels, block => block.Text == "电盘额定电流（A）");
                Assert.Contains(labels, block => block.Text == "I-line_Panel");
                window.Close();
            });
        }

        [Fact]
        public void ColorPage_ShowsTwoAciPickers()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(8);
                window.Show();
                Pump(3);
                SettingsColorPage page = window.ColorPage;
                Assert.Equal(8, window.SelectedPageIndex);
                ComboBox[] colors = { page.DeviceColorBox, page.UpstreamColorBox };
                Assert.All(colors, box =>
                {
                    Assert.False(box.IsEditable);
                    List<CadColorChoice> choices = box.Items.Cast<CadColorChoice>().ToList();
                    Assert.Equal(9, choices.Count);
                    Assert.Contains(choices, choice =>
                        choice.DisplayName.Contains("ACI 3"));
                    Assert.Contains(choices, choice =>
                        choice.DisplayName.Contains("ACI 6"));
                });
                window.Close();
            });
        }

        [Fact]
        public void OutputPage_ExposesDirectoryStructureAndConfigShareActions()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(9);
                window.Show();
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                Pump(3);
                SettingsOutputPage page = window.OutputPage;
                Assert.Equal(9, window.SelectedPageIndex);
                // D-042：输出页不再有布局开关（按图纸/分目录两级分层已删除）。
                List<ToggleSwitch> switches = FindAll<ToggleSwitch>(page);
                Assert.Empty(switches);
                Assert.Contains("机台一个文件夹", page.DirectoryHintText.Text
                    + Convert.ToString(FindAll<System.Windows.Controls.TextBlock>(page)
                        .First(block => block.Text != null && block.Text.Contains("机台"))
                        .Text));
                List<string> buttons = FindAll<Wpf.Ui.Controls.Button>(page)
                    .Select(button => Convert.ToString(button.Content)).ToList();
                                // 概览就绪仪表盘（2026-11-03）：状态卡与工作流指引 + 跳转按钮。
                Assert.False(string.IsNullOrWhiteSpace(window.OverviewPage.DataSourceText.Text));
                Assert.False(string.IsNullOrWhiteSpace(window.OverviewPage.OutputText.Text));
                Assert.False(string.IsNullOrWhiteSpace(window.OverviewPage.DataSourceDetailText.Text));
                Assert.False(string.IsNullOrWhiteSpace(window.OverviewPage.OutputDetailText.Text));
                Assert.Contains("复制配置到剪贴板", buttons);
                Assert.Contains("从剪贴板导入", buttons);
                Assert.Contains("打开目录", buttons);
                Assert.Contains("浏览...", buttons);
                window.Close();
            });
        }

        [Fact]
        public void LogsPage_ExposesLevelRetentionAndRecentOperationTable()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(10);
                window.Show();
                Pump(3);
                SettingsLogsPage page = window.LogsPage;
                Assert.Equal(10, window.SelectedPageIndex);
                Assert.Equal(new[] { "INFO", "WARN", "ERROR" },
                    page.LogLevel.Items.Cast<ComboBoxItem>()
                        .Select(item => Convert.ToString(item.Content)).ToArray());
                Assert.Equal(1, page.LogRetentionDays.Minimum);
                Assert.Equal(3650, page.LogRetentionDays.Maximum);
                Assert.Equal(1, page.HistoryRetentionMonths.Minimum);
                Assert.Equal(120, page.HistoryRetentionMonths.Maximum);
                Assert.Equal(2, FindAll<NumberBox>(page).Count);
                DataGrid grid = page.OperationLogGrid;
                Assert.True(grid.Columns.Count >= 4);
                List<string> buttons = FindAll<Wpf.Ui.Controls.Button>(page)
                    .Select(button => Convert.ToString(button.Content)).ToList();
                Assert.Contains("打开日志目录", buttons);
                Assert.Contains("打开操作记录目录", buttons);
                Assert.Contains("刷新记录", buttons);
                window.Close();
            });
        }

        [Fact]
        public void KeyControls_RemainVisibleAtWindowMinimumSize()
        {
            // 旧高 DPI 用例的保义迁移：WPF 布局与 DPI 无关，改为最小窗口下关键控件可见且有尺寸。
            RunSta(() =>
            {
                var window = new SettingsWindow(9);
                window.Show();
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                Pump(4);
                foreach (string text in new[]
                    { "复制配置到剪贴板", "从剪贴板导入", "打开目录" })
                {
                    Wpf.Ui.Controls.Button button = FindAll<Wpf.Ui.Controls.Button>(
                            window.OutputPage)
                        .FirstOrDefault(candidate => Convert.ToString(candidate.Content) == text);
                    Assert.NotNull(button);
                    Assert.True(button.IsVisible && button.ActualWidth > 0
                        && button.ActualHeight > 0,
                        text + " 不可见或无尺寸");
                }

                window.ShowPage(10);
                Pump(3);
                Assert.Equal(10, window.SelectedPageIndex);
                Assert.Contains(LogLevelItems(window.LogsPage), item => item == "INFO");
                Assert.All(new[] { window.LogsPage.LogRetentionDays,
                        window.LogsPage.HistoryRetentionMonths }, box =>
                {
                    Assert.True(box.IsVisible && box.ActualWidth > 0 && box.ActualHeight > 0);
                });
                window.Close();
            });
        }

        private static List<string> LogLevelItems(SettingsLogsPage page)
            => page.LogLevel.Items.Cast<ComboBoxItem>()
                .Select(item => Convert.ToString(item.Content)).ToList();

        // ---------- 保存 / 校验语义 ----------

        [Fact]
        public void Confirm_KeepsWindowOpenAndLocatesOffendingField()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(0);
                window.Show();
                Pump(3);

                // 1) 管径不是数值：定位到绘制页，输入保留，窗不关。
                window.DrawPage.ConduitDiameter.Text = "abc";
                window.TryConfirm();
                Assert.False(window.Confirmed);
                Assert.Equal(1, window.SelectedPageIndex);
                Assert.True(window.Validation.IsOpen);
                Assert.Contains("1到1000毫米", window.Validation.Message);
                Assert.Equal("abc", window.DrawPage.ConduitDiameter.Text);

                // 2) 固定清单没有的线管型号（75 只有软管，没有线管）。
                window.DrawPage.ConduitDiameter.Text = "75";
                window.TryConfirm();
                Assert.False(window.Confirmed);
                Assert.Contains("固定清单", window.Validation.Message);

                // 3) 机台数据不存在：定位到机台页。
                window.DrawPage.ConduitDiameter.Text = "20";
                window.MachinePage.FillExcelPath.Text =
                    @"Z:\UNCAD\not\exists\machine.xlsx";
                window.TryConfirm();
                Assert.False(window.Confirmed);
                Assert.Equal(5, window.SelectedPageIndex);
                Assert.Contains("机台数据 Excel 不存在", window.Validation.Message);

                // 4) 网络地址不要求文件存在（D-041）：直接通过校验。
                window.MachinePage.FillExcelPath.Text =
                    "https://example.test/machines.xlsx";
                window.TryConfirm();
                Assert.True(window.Confirmed);

                // 5) 全部通过才置 Confirmed（此处不落盘：Save 由调用方执行）。
                window.MachinePage.FillExcelPath.Text = "";
                window.TryConfirm();
                Assert.True(window.Confirmed);
                window.Close();
            });
        }

        // ---------- 源级契约（接线与配置键零增减） ----------

        [Fact]
        public void ConfigKeys_ExactSameSetAsWinFormsVersion()
        {
            string source = ReadSource("SettingsWindow.xaml.cs");
            var found = new SortedSet<string>(Regex.Matches(source,
                    @"ConfigKeys\.(\w+)").Cast<Match>()
                .Select(match => match.Groups[1].Value));
            // 旧 UnifiedSettingsForm.LoadValues/Save 引用的 53 个键一个不增、一个不减。
            string[] expected =
            {
                "UnlText", "UnlHeight", "UnlPos", "UnlOffset",
                "UnqHeight", "UnqLineOff", "UnqTextOff", "UnqSide",
                // D-050（2026-11-09）：U1C 标注几何拆出独立键；2026-11-15 默认
                // 改为与桥架同车道（上侧 15），独立键本身不变。
                "ConduitDiameter", "ConduitLineOff", "ConduitTextOff", "ConduitSide",
                "UnrDiameter",
                "UnaddHeight", "UnaddMmPerGrid", "UnaddTextEnabled",
                "UnaddMTextEnabled", "UnaddDimensionEnabled", "UnaddCableEnabled",
                "UnaddBridgeEnabled", "UnaddConduitEnabled",
                "StartupSplashEnabled",
                "StyleName", "StyleFont", "StyleBigFont", "StyleWidth",
                "FillExcelPath", "FillTableRow", "FillClearRows", "FillTextHeight",
                "FillFlexibleConduitMeters", "FillCableOwnerReserve",
                "FillDownstreamSocketAmps", "FillDownstreamPanelAmps",
                "FillDownstreamSocketEnabled", "FillDownstreamPanelEnabled",
                "SubmitFolder", "FillDeviceColorIndex", "FillUpstreamColorIndex",
                "FillAutofillCable", "FillAutofillBreaker", "FillAutofillPanel",
                "FillAutofillFlexibleConduit", "FillAutofillRigidConduit",
                "FillAutofillBridge", "FillAutofillOutletPanel",
                "FillAutofillBusPlugBox",
                "CableReserveILine", "CableReserveSocket", "CableReserveBusbar",
                "FillDeviceReserveAutoDetect", "FillRequireAllCircuits",
                "LogLevel", "LogRetentionDays", "HistoryRetentionMonths"
            };
            string[] missing = expected.Except(found).ToArray();
            string[] added = found.Except(expected).ToArray();
            Assert.True(missing.Length == 0,
                "迁移丢了配置键: " + string.Join(",", missing));
            Assert.True(added.Length == 0,
                "迁移新增了配置键: " + string.Join(",", added));
        }

        [Fact]
        public void SettingsSource_HasNoRemoteSourceOrLedgerWiring()
        {
            string source = ReadSource("SettingsWindow.xaml.cs")
                + ReadSource("ConfigSharePreviewWindow.xaml.cs")
                + string.Concat(Directory.GetFiles(SourcePath("Settings"), "*.cs")
                    .Select(File.ReadAllText));

            // D-041：UI 用 IsRemote 区分本地/网络数据源的校验与反馈；
            // 上传/台账类型仍不得回归。只断言接线，不断言文案字面量。
            Assert.Contains("MachineWorkbookSource.IsRemote", source);
            Assert.DoesNotContain("MachineLedgerReporter", source);
            Assert.DoesNotContain("ClientSubmissionUploader", source);
            Assert.DoesNotContain("BackgroundUploadQueue", source);
            // 新页的接线必须存在（否则按钮只是摆设）。
            Assert.Contains("ConfigShareService.Export()", source);
            Assert.Contains("ConfigShareService.Preview(", source);
            Assert.Contains("ConfigShareService.Import(", source);
            Assert.Contains("OperationLog.ReadRecent(", source);
            Assert.Contains("OperationLog.OpenFolder()", source);
            // 无效设置：UNQ_TEXT（桥架规格由命令固定传入）与 UNC_FILL_BRIDGE
            // （xframe_v2 已无 BRIDGE_FRAME_INFO 属性）都是已删除的死键，
            // U1SET 不得再引用；UNQ_TEXT_OFF 是有效键，注意精确匹配。
            Assert.DoesNotContain("ConfigKeys.UnqText,", source);
            Assert.DoesNotContain("ConfigKeys.FillBridge", source);
        }

        [Fact]
        public void MachineRefresh_RemainsExplicitAndRunsOffTheUiThread()
        {
            string source = ReadSource("SettingsWindow.xaml.cs");

            Assert.Contains("private async void RefreshMachineWorkbook", source);
            Assert.Contains("await Task.Run", source);
            Assert.Contains("button.Content = \"刷新中...\"", source);
            Assert.Contains("MachineWorkbookSource.TryGetSnapshot", source);
            // 刷新只能由按钮显式触发，不随窗口装载自动进行。
            Assert.DoesNotContain("Loaded += RefreshMachineWorkbook", source);
            Assert.DoesNotContain("Shown += RefreshMachineWorkbook", source);
            Assert.Contains("MachinePage.RefreshButton.Click", source);
        }

        // ---------- 辅助 ----------

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
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }

        /// <summary>逻辑树遍历：页面无需进入可视树即可断言结构。</summary>
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
            IEnumerator children;
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
