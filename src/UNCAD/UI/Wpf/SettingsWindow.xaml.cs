using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using UNCAD.Core.Text;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 统一配置中心（U1SET，3.0 WPF 版，D-038，P1 导航枢纽）。
    ///
    /// 左侧 NavigationView 一页一个导航项（v4 无 UiPage，页面是普通 UserControl，
    /// 用 PageProvider(IServiceProvider) + Navigate(Type) 注册，见 demo/UiDemo）；
    /// 读写仍走 Settings/ConfigKeys 既有机制——配置键一个不增、一个不减、范围不变；
    /// 权威校验在“保存”里做：失败时导航到出错页、定位字段并保留输入，不关窗。
    /// 页面（UserControl）只承载布局；接线、读写与校验全部集中在本窗口。
    /// </summary>
    public sealed partial class SettingsWindow : FluentWindow
    {
        /// <summary>用户点了“保存”且校验通过为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        internal NavigationView Navigation => Navi;
        internal InfoBar Validation => ValidationBar;
        internal SettingsOverviewPage OverviewPage => _overview;
        internal SettingsDrawPage DrawPage => _draw;
        internal SettingsStatisticsPage StatPage => _stat;
        internal SettingsStylePage StylePage => _style;
        internal SettingsStartupPage StartupPage => _startup;
        internal SettingsMachinePage MachinePage => _machine;
        internal SettingsBomPage BomPage => _bom;
        internal SettingsDownstreamPage DownstreamPage => _downstream;
        internal SettingsColorPage ColorPage => _color;
        internal SettingsOutputPage OutputPage => _output;
        internal SettingsLogsPage LogsPage => _logs;

        /// <summary>侧边栏顺序：概览 → 绘制 → 统计/样式/启动 → 机台数据 → 清单 → Downstream → 颜色 → 输出 → 日志。</summary>
        internal static readonly string[] PageTitles =
        {
            "概览", "绘制标注", "统计汇总", "文字样式", "界面与启动",
            "机台数据", "清单与表格", "Downstream 自动添加", "颜色",
            "输出与分享", "日志与记录"
        };

        private readonly SettingsOverviewPage _overview = new SettingsOverviewPage();
        private readonly SettingsDrawPage _draw = new SettingsDrawPage();
        private readonly SettingsStatisticsPage _stat = new SettingsStatisticsPage();
        private readonly SettingsStylePage _style = new SettingsStylePage();
        private readonly SettingsStartupPage _startup = new SettingsStartupPage();
        private readonly SettingsMachinePage _machine = new SettingsMachinePage();
        private readonly SettingsBomPage _bom = new SettingsBomPage();
        private readonly SettingsDownstreamPage _downstream = new SettingsDownstreamPage();
        private readonly SettingsColorPage _color = new SettingsColorPage();
        private readonly SettingsOutputPage _output = new SettingsOutputPage();
        private readonly SettingsLogsPage _logs = new SettingsLogsPage();

        private readonly List<NavigationViewItem> _menuItems;
        private readonly List<Type> _pageTypes;
        /// <summary>11 个页面实例。直接作为 Navi.ContentOverlay 注入（不走 Frame 导航）：
        /// WPF-UI NavigationView 的 Frame 不继承窗口合并的资源字典，走导航时页内
        /// ToggleSwitch/NumberBox 等拿不到控件模板，会退化成兜底样式（D-046 复盘）。</summary>
        private readonly object[] _pages;
        private readonly int _initialPage;
        private int _currentPage = -1;
        private bool _machineRefreshInProgress;

        public SettingsWindow() : this(0) { }

        public SettingsWindow(int pageIndex)
        {
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            Title = ProductMetadata.ProductName + " · 配置中心";

            _menuItems = new List<NavigationViewItem>
            {
                NavOverview, NavDraw, NavStat, NavStyle, NavStartup, NavMachine,
                NavBom, NavDownstream, NavColor, NavOutput, NavLogs
            };
            _pageTypes = new List<Type>
            {
                typeof(SettingsOverviewPage), typeof(SettingsDrawPage),
                typeof(SettingsStatisticsPage), typeof(SettingsStylePage),
                typeof(SettingsStartupPage), typeof(SettingsMachinePage),
                typeof(SettingsBomPage), typeof(SettingsDownstreamPage),
                typeof(SettingsColorPage), typeof(SettingsOutputPage),
                typeof(SettingsLogsPage)
            };
            _pages = new object[]
            {
                _overview, _draw, _stat, _style, _startup, _machine,
                _bom, _downstream, _color, _output, _logs
            };
            _initialPage = Math.Max(0, Math.Min(pageIndex, _pageTypes.Count - 1));

            WireActions();
            OutputPage.DirectoryHintText.Text = "默认：<根目录>\\<机台ID>\\（BOQ / DWG / PDF 都在各自的机台文件夹里）；根目录留空使用 "
                + DefaultOutputRoot();
            LoadValues();
            // NavigationView 的内容区在模板应用后才存在，首次导航必须等 Loaded。
            Loaded += (sender, args) => ShowPage(_initialPage);
        }

        /// <summary>当前选中导航项的下标（与 PageTitles 对应；未选中为 -1）。</summary>
        internal int SelectedPageIndex
        {
            get
            {
                for (int index = 0; index < _menuItems.Count; index++)
                    if (ReferenceEquals(Navi.SelectedItem, _menuItems[index])) return index;
                return _currentPage;
            }
        }

        /// <summary>切换到指定导航页（越界钳回范围内）；测试与校验定位共用。
        /// 页面实例直接挂 Navi.ContentOverlay（不走 Frame 导航，见 _pages 注释）；
        /// 选中态由控件在用户点击时自同步，程序化切换由 SelectedPageIndex 的
        /// _currentPage 兜底。</summary>
        internal void ShowPage(int index)
        {
            if (!IsLoaded) return;
            int clamped = Math.Max(0, Math.Min(index, _pages.Length - 1));
            _currentPage = clamped;
            Navi.ContentOverlay = _pages[clamped];
        }

        private void OnNavItemInvoked(NavigationView sender, RoutedEventArgs args)
        {
            // v4 的 ItemInvoked 只给 RoutedEventArgs：被点项从 OriginalSource 取。
            if (args.OriginalSource is NavigationViewItem item)
            {
                int index = _menuItems.IndexOf(item);
                if (index >= 0) ShowPage(index);
            }
        }

        private void WireActions()
        {
            OverviewPage.GoMachineButton.Click += (sender, args) => ShowPage(5);
            OverviewPage.GoOutputButton.Click += (sender, args) => ShowPage(9);
            MachinePage.BrowseExcelButton.Click += OnBrowseExcel;
            MachinePage.RefreshButton.Click += (sender, args) => RefreshMachineWorkbook();
            MachinePage.FillExcelPath.TextChanged +=
                (sender, args) =>
                {
                    UpdateMachineSnapshotStatus();
                    RefreshOverviewStatus();
                };
            OutputPage.BrowseButton.Click += OnBrowseOutput;
            OutputPage.OpenButton.Click += (sender, args) => OpenFolder(CurrentOutputRoot());
            OutputPage.CopyShareButton.Click += (sender, args) => CopyConfigToClipboard();
            OutputPage.ImportShareButton.Click += (sender, args) => ImportConfigFromClipboard();
            LogsPage.OpenLogsButton.Click += (sender, args) => OpenFolder(LogDirectory());
            LogsPage.OpenHistoryButton.Click += (sender, args) => OperationLog.OpenFolder();
            LogsPage.RefreshButton.Click += (sender, args) => RefreshOperationLog();
        }

        // ---------- 保存（权威校验在“保存”按钮里做） ----------

        private void OnSave(object sender, RoutedEventArgs e) => TryConfirm();

        /// <summary>校验 → 全部通过才置 Confirmed 并关窗；失败保留输入并定位字段。</summary>
        internal void TryConfirm()
        {
            if (_machineRefreshInProgress) return;
            if (!ConduitDiameter.TryNormalize(DrawPage.ConduitDiameter.Text, out string diameter))
            {
                ShowValidationError(1, "默认管径无效，请输入1到1000毫米之间的数值。");
                return;
            }
            ListItem conduitItem = ListItemReader.EmbeddedCatalogIndex
                .FindRigidConduit(diameter);
            if (conduitItem == null
                || BoqFeatureName.Extract(conduitItem.Feature).Length == 0)
            {
                ShowValidationError(1, "固定清单中没有该线管型号。可选型号为 20、25、38、51 mm。");
                return;
            }
            DrawPage.ConduitDiameter.Text = diameter;
            // 用户只需要提供机台/设备表；固定清单随插件内嵌，不再有清单路径校验。
            string machinePath = MachinePage.FillExcelPath.Text.Trim();
            // D-041：本地/UNC 路径必须是现有文件（或保留过 SQLite 快照的同一路径）；
            // HTTP/HTTPS 网络地址在“刷新”时才下载，这里不要求文件已存在。
            if (machinePath.Length > 0
                && !MachineWorkbookSource.IsRemote(machinePath)
                && !File.Exists(machinePath)
                && !MachineWorkbookSource.HasSnapshot(machinePath))
            {
                ShowValidationError(5, "机台数据 Excel 不存在。");
                return;
            }
            // Save() 由调用方（SettingsFeature）在 Confirmed 后执行；这里只刷新概览状态。
            RefreshOverviewStatus();
            ValidationBar.IsOpen = false;
            Confirmed = true;
            Close();
        }

        /// <summary>校验失败：导航到出错页、聚焦字段并保留输入，窗口保持打开。</summary>
        private void ShowValidationError(int pageIndex, string message)
        {
            ShowPage(pageIndex);
            Control field = pageIndex == 1 ? (Control)DrawPage.ConduitDiameter
                : MachinePage.FillExcelPath;
            field.Focus();
            SelectAllText(field);
            ValidationBar.Message = message;
            ValidationBar.Severity = InfoBarSeverity.Warning;
            ValidationBar.IsOpen = true;
        }

        private static void SelectAllText(Control field)
        {
            if (field is System.Windows.Controls.TextBox textBox)
            {
                textBox.SelectAll();
                return;
            }
            // 可编辑 ComboBox：选中文本要打到内部 TextBox 上。
            if (field is ComboBox comboBox)
            {
                var editor = FindChild<System.Windows.Controls.TextBox>(comboBox);
                editor?.SelectAll();
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        // ---------- 读取当前值 ----------

        private void LoadValues()
        {
            DrawPage.UnlText.Text = Settings.Get(ConfigKeys.UnlText, "2000mm");
            SetNumber(DrawPage.UnlHeight, Settings.GetDouble(ConfigKeys.UnlHeight, 180.0));
            DrawPage.UnlPos.SelectedValue =
                UnlPosValue(Settings.Get(ConfigKeys.UnlPos, "1"));
            SetNumber(DrawPage.UnlOffset, Settings.GetDouble(ConfigKeys.UnlOffset, 0.0));

            SetNumber(DrawPage.UnqHeight, Settings.GetDouble(ConfigKeys.UnqHeight, 180.0));
            SetNumber(DrawPage.UnqLineOff, Settings.GetDouble(ConfigKeys.UnqLineOff, 15.0));
            SetNumber(DrawPage.UnqTextOff, Settings.GetDouble(ConfigKeys.UnqTextOff, 0.0));
            DrawPage.UnqSide.SelectedValue =
                Settings.Get(ConfigKeys.UnqSide, "1") == "0" ? "0" : "1";

            DrawPage.ConduitDiameter.Text = ConduitDiameter.NormalizeOrDefault(
                Settings.Get(ConfigKeys.ConduitDiameter, "20"), "20");

            SetNumber(DrawPage.UnrDiameter, Settings.GetDouble(ConfigKeys.UnrDiameter, 300.0));

            SetNumber(StatPage.UnaddHeight, Settings.GetDouble(ConfigKeys.UnaddHeight, 180.0));
            SetNumber(StatPage.UnaddMmPerGrid,
                Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0));
            StatPage.StatTextEnabled.IsChecked =
                Settings.GetBool(ConfigKeys.UnaddTextEnabled, true);
            StatPage.StatMTextEnabled.IsChecked = Settings.GetBool(
                ConfigKeys.UnaddMTextEnabled, StatisticsSettings.DefaultIncludeMText);
            StatPage.StatDimensionEnabled.IsChecked = Settings.GetBool(
                ConfigKeys.UnaddDimensionEnabled, StatisticsSettings.DefaultIncludeDimension);
            StatPage.StatCableEnabled.IsChecked =
                Settings.GetBool(ConfigKeys.UnaddCableEnabled, true);
            StatPage.StatBridgeEnabled.IsChecked =
                Settings.GetBool(ConfigKeys.UnaddBridgeEnabled, true);
            StatPage.StatConduitEnabled.IsChecked =
                Settings.GetBool(ConfigKeys.UnaddConduitEnabled, true);
            StartupPage.SplashEnabled.IsChecked =
                Settings.GetBool(ConfigKeys.StartupSplashEnabled, true);

            // 文字样式
            StylePage.StyleName.Text = Settings.Get(ConfigKeys.StyleName, "UNC-标注");
            StylePage.StyleFont.Text = Settings.Get(ConfigKeys.StyleFont, "msyh.ttf");
            StylePage.StyleBigFont.Text = Settings.Get(ConfigKeys.StyleBigFont, "");
            SetNumber(StylePage.StyleWidth, Settings.GetDouble(ConfigKeys.StyleWidth, 0.8));

            // Excel 填充
            MachinePage.FillExcelPath.Text = Settings.Get(ConfigKeys.FillExcelPath, "");
            SetNumber(BomPage.FillTableRow, Settings.GetDouble(ConfigKeys.FillTableRow, 1.0));
            SetNumber(BomPage.FillClearRows, Settings.GetDouble(ConfigKeys.FillClearRows, 11.0));
            SetNumber(BomPage.FillTextHeight, Settings.GetDouble(ConfigKeys.FillTextHeight,
                TableFillFormatter.DefaultTextHeight));
            SetNumber(BomPage.FillFlexibleMeters, Settings.GetDouble(
                ConfigKeys.FillFlexibleConduitMeters,
                FillPlanningOptions.DefaultFlexibleConduitMeters));
            SetNumber(BomPage.FillCableOwnerReserve, Settings.GetDouble(
                ConfigKeys.FillCableOwnerReserve,
                FillRuntimeOptions.DefaultCableOwnerReserveMillimeters));
            SetNumber(DownstreamPage.FillDownstreamAmps, Settings.GetDouble(
                ConfigKeys.FillDownstreamSocketAmps,
                FillRuntimeOptions.DefaultDownstreamSocketAmps));
            SetNumber(DownstreamPage.FillDownstreamPanelAmps, Settings.GetDouble(
                ConfigKeys.FillDownstreamPanelAmps,
                FillRuntimeOptions.DefaultDownstreamPanelAmps));
            DownstreamPage.DownstreamSocketEnabled.IsChecked = Settings.GetBool(
                ConfigKeys.FillDownstreamSocketEnabled,
                FillRuntimeOptions.DefaultDownstreamEnabled);
            DownstreamPage.DownstreamPanelEnabled.IsChecked = Settings.GetBool(
                ConfigKeys.FillDownstreamPanelEnabled,
                FillRuntimeOptions.DefaultDownstreamEnabled);
            OutputPage.SubmitFolder.Text = Settings.Get(ConfigKeys.SubmitFolder, "");
            SelectColor(ColorPage.DeviceColorBox, FillColorSettings.DeviceColorIndex());
            SelectColor(ColorPage.UpstreamColorBox, FillColorSettings.UpstreamColorIndex());
            FillAutoFillOptions autoFill = FillAutoFillOptions.Default;
            BomPage.AutofillCable.IsChecked =
                Settings.GetBool(ConfigKeys.FillAutofillCable, autoFill.Cable);
            BomPage.AutofillBreaker.IsChecked =
                Settings.GetBool(ConfigKeys.FillAutofillBreaker, autoFill.Breaker);
            BomPage.AutofillPanel.IsChecked =
                Settings.GetBool(ConfigKeys.FillAutofillPanel, autoFill.Panel);
            BomPage.AutofillFlexibleConduit.IsChecked = Settings.GetBool(
                ConfigKeys.FillAutofillFlexibleConduit, autoFill.FlexibleConduit);
            BomPage.AutofillRigidConduit.IsChecked = Settings.GetBool(
                ConfigKeys.FillAutofillRigidConduit, autoFill.RigidConduit);
            BomPage.AutofillBridge.IsChecked =
                Settings.GetBool(ConfigKeys.FillAutofillBridge, autoFill.Bridge);
            BomPage.AutofillOutletPanel.IsChecked = Settings.GetBool(
                ConfigKeys.FillAutofillOutletPanel, autoFill.OutletPanel);
            BomPage.AutofillBusPlugBox.IsChecked = Settings.GetBool(
                ConfigKeys.FillAutofillBusPlugBox, autoFill.BusPlugBox);
            // 电缆盘内/箱内预留与整机回路校验（2.10.0）
            SetNumber(BomPage.CableReserveILine, Settings.GetDouble(
                ConfigKeys.CableReserveILine,
                CableReserveSettings.DefaultILinePanelMillimeters));
            SetNumber(BomPage.CableReserveSocket, Settings.GetDouble(
                ConfigKeys.CableReserveSocket,
                CableReserveSettings.DefaultSocketPanelMillimeters));
            SetNumber(BomPage.CableReserveBusbar, Settings.GetDouble(
                ConfigKeys.CableReserveBusbar,
                CableReserveSettings.DefaultBusbarConnectorMillimeters));
            BomPage.RequireAllCircuits.IsChecked =
                Settings.GetBool(ConfigKeys.FillRequireAllCircuits, true);
            BomPage.DeviceReserveAutoDetect.IsChecked =
                Settings.GetBool(ConfigKeys.FillDeviceReserveAutoDetect, true);
            // 日志与操作记录（2.10.0）
            SelectLogLevel(Settings.Get(ConfigKeys.LogLevel, FileLogger.DefaultLevel));
            SetNumber(LogsPage.LogRetentionDays, Settings.GetDouble(
                ConfigKeys.LogRetentionDays, FileLogger.DefaultRetentionDays));
            SetNumber(LogsPage.HistoryRetentionMonths, Settings.GetDouble(
                ConfigKeys.HistoryRetentionMonths, OperationLog.DefaultRetentionMonths));
            RefreshOperationLog();
            RefreshOverviewStatus();
        }

        /// <summary>概览就绪仪表盘：机台数据源快照状态与输出目录状态（2026-11-03）。
        /// 数据或目录变化后由 LoadValues 重新计算，改动在页面上即时可见。</summary>
        private void RefreshOverviewStatus()
        {
            try
            {
                string source = MachinePage.FillExcelPath.Text.Trim();
                if (source.Length == 0)
                {
                    OverviewPage.DataSourceText.Text = "尚未配置";
                    OverviewPage.DataSourceDetailText.Text = "选机台 Excel 或网络地址后刷新";
                }
                else if (MachineWorkbookSource.TryGetSnapshot(source,
                    out MachineWorkbookSnapshotInfo snapshot))
                {
                    OverviewPage.DataSourceText.Text = "已就绪 · " + snapshot.RowCount + " 回路";
                    OverviewPage.DataSourceDetailText.Text = RefreshedTime(snapshot.RefreshedUtc)
                        + " · " + SourceLabel(source);
                }
                else
                {
                    OverviewPage.DataSourceText.Text = "未刷新";
                    OverviewPage.DataSourceDetailText.Text = SourceLabel(source)
                        + " · 到机台数据页点“刷新”";
                }

                string outputRoot = CurrentOutputRoot();
                OverviewPage.OutputText.Text = string.IsNullOrWhiteSpace(outputRoot)
                    ? "默认目录" : outputRoot;
                OverviewPage.OutputDetailText.Text = Directory.Exists(outputRoot)
                    ? "目录可用 · BOQ/DWG/PDF 按机台分夹"
                    : "目录将在首次输出时创建";
            }
            catch (System.Exception ex)
            {
                Log.Warn("概览状态刷新失败: " + ex.Message);
            }
        }

        private static string SourceLabel(string source)
            => MachineWorkbookSource.IsRemote(source)
                ? "网络地址" : "本地/共享文件";

        private static string RefreshedTime(string refreshedUtc)
        {
            if (DateTime.TryParse(refreshedUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTime utc))
                return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture);
            return refreshedUtc;
        }

        /// <summary>UNL_POS：0=居中 / 1=靠边下 / 2=靠边上；其他值回退默认 1。</summary>
        private static string UnlPosValue(string value)
            => value == "0" || value == "2" ? value : "1";

        private void SelectLogLevel(string value)
        {
            string wanted = (value ?? "").Trim();
            foreach (object item in LogsPage.LogLevel.Items)
            {
                if (!(item is ComboBoxItem entry)
                    || !string.Equals(Convert.ToString(entry.Tag), wanted,
                        StringComparison.OrdinalIgnoreCase)) continue;
                LogsPage.LogLevel.SelectedItem = entry;
                return;
            }
            foreach (object item in LogsPage.LogLevel.Items)
            {
                if (!(item is ComboBoxItem fallback)
                    || Convert.ToString(fallback.Tag) != "INFO") continue;
                LogsPage.LogLevel.SelectedItem = fallback;
                return;
            }
            LogsPage.LogLevel.SelectedIndex = 0;
        }

        // ---------- 保存（含数据校验后的写回） ----------

        public void Save()
        {
            // 线段
            Settings.Set(ConfigKeys.UnlText, DrawPage.UnlText.Text.Trim());
            Settings.Set(ConfigKeys.UnlHeight, NumberText(DrawPage.UnlHeight));
            Settings.Set(ConfigKeys.UnlPos,
                Convert.ToString(DrawPage.UnlPos.SelectedValue ?? "1"));
            Settings.Set(ConfigKeys.UnlOffset, NumberText(DrawPage.UnlOffset));

            // 桥架（偏移允许 0=自动/贴线）；规格由 U1Q1/U1Q2/U1Q4 固定传入，不写配置。
            Settings.Set(ConfigKeys.UnqHeight, NumberText(DrawPage.UnqHeight));
            Settings.Set(ConfigKeys.UnqLineOff, NumberText(DrawPage.UnqLineOff));
            Settings.Set(ConfigKeys.UnqTextOff, NumberText(DrawPage.UnqTextOff));
            Settings.Set(ConfigKeys.UnqSide,
                Convert.ToString(DrawPage.UnqSide.SelectedValue ?? "1") == "0" ? "0" : "1");

            // 线管只保存管径；标注几何参数与桥架共用。
            Settings.Set(ConfigKeys.ConduitDiameter,
                ConduitDiameter.NormalizeOrDefault(DrawPage.ConduitDiameter.Text, "20"));

            // 拱桥
            Settings.Set(ConfigKeys.UnrDiameter, NumberText(DrawPage.UnrDiameter));

            // 统计
            Settings.Set(ConfigKeys.UnaddHeight, NumberText(StatPage.UnaddHeight));
            Settings.Set(ConfigKeys.UnaddMmPerGrid, NumberText(StatPage.UnaddMmPerGrid));
            Settings.SetBool(ConfigKeys.UnaddTextEnabled, IsOn(StatPage.StatTextEnabled));
            Settings.SetBool(ConfigKeys.UnaddMTextEnabled, IsOn(StatPage.StatMTextEnabled));
            Settings.SetBool(ConfigKeys.UnaddDimensionEnabled,
                IsOn(StatPage.StatDimensionEnabled));
            Settings.SetBool(ConfigKeys.UnaddCableEnabled, IsOn(StatPage.StatCableEnabled));
            Settings.SetBool(ConfigKeys.UnaddBridgeEnabled, IsOn(StatPage.StatBridgeEnabled));
            Settings.SetBool(ConfigKeys.UnaddConduitEnabled, IsOn(StatPage.StatConduitEnabled));
            Settings.SetBool(ConfigKeys.StartupSplashEnabled, IsOn(StartupPage.SplashEnabled));

            // 文字样式
            Settings.Set(ConfigKeys.StyleName, StylePage.StyleName.Text.Trim());
            Settings.Set(ConfigKeys.StyleFont, StylePage.StyleFont.Text.Trim());
            Settings.Set(ConfigKeys.StyleBigFont, StylePage.StyleBigFont.Text.Trim());
            Settings.Set(ConfigKeys.StyleWidth, NumberText(StylePage.StyleWidth));

            // Excel 填充
            Settings.Set(ConfigKeys.FillExcelPath, MachinePage.FillExcelPath.Text.Trim());
            Settings.Set(ConfigKeys.FillTableRow, IntegerText(BomPage.FillTableRow));
            Settings.Set(ConfigKeys.FillClearRows, IntegerText(BomPage.FillClearRows));
            Settings.Set(ConfigKeys.FillTextHeight, NumberText(BomPage.FillTextHeight));
            Settings.Set(ConfigKeys.FillFlexibleConduitMeters,
                NumberText(BomPage.FillFlexibleMeters));
            Settings.Set(ConfigKeys.FillCableOwnerReserve,
                NumberText(BomPage.FillCableOwnerReserve));
            Settings.Set(ConfigKeys.FillDownstreamSocketAmps,
                IntegerText(DownstreamPage.FillDownstreamAmps));
            Settings.Set(ConfigKeys.FillDownstreamPanelAmps,
                IntegerText(DownstreamPage.FillDownstreamPanelAmps));
            Settings.SetBool(ConfigKeys.FillDownstreamSocketEnabled,
                IsOn(DownstreamPage.DownstreamSocketEnabled));
            Settings.SetBool(ConfigKeys.FillDownstreamPanelEnabled,
                IsOn(DownstreamPage.DownstreamPanelEnabled));
            Settings.Set(ConfigKeys.SubmitFolder, OutputPage.SubmitFolder.Text.Trim());
            Settings.Set(ConfigKeys.FillDeviceColorIndex,
                SelectedColorIndex(ColorPage.DeviceColorBox,
                    FillColorSettings.DefaultDeviceColorIndex)
                .ToString(CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.FillUpstreamColorIndex,
                SelectedColorIndex(ColorPage.UpstreamColorBox,
                    FillColorSettings.DefaultUpstreamColorIndex)
                .ToString(CultureInfo.InvariantCulture));
            Settings.SetBool(ConfigKeys.FillAutofillCable, IsOn(BomPage.AutofillCable));
            Settings.SetBool(ConfigKeys.FillAutofillBreaker, IsOn(BomPage.AutofillBreaker));
            Settings.SetBool(ConfigKeys.FillAutofillPanel, IsOn(BomPage.AutofillPanel));
            Settings.SetBool(ConfigKeys.FillAutofillFlexibleConduit,
                IsOn(BomPage.AutofillFlexibleConduit));
            Settings.SetBool(ConfigKeys.FillAutofillRigidConduit,
                IsOn(BomPage.AutofillRigidConduit));
            Settings.SetBool(ConfigKeys.FillAutofillBridge, IsOn(BomPage.AutofillBridge));
            Settings.SetBool(ConfigKeys.FillAutofillOutletPanel,
                IsOn(BomPage.AutofillOutletPanel));
            Settings.SetBool(ConfigKeys.FillAutofillBusPlugBox,
                IsOn(BomPage.AutofillBusPlugBox));

            // 电缆盘内/箱内预留与整机回路校验
            Settings.Set(ConfigKeys.CableReserveILine, NumberText(BomPage.CableReserveILine));
            Settings.Set(ConfigKeys.CableReserveSocket,
                NumberText(BomPage.CableReserveSocket));
            Settings.Set(ConfigKeys.CableReserveBusbar,
                NumberText(BomPage.CableReserveBusbar));
            Settings.SetBool(ConfigKeys.FillRequireAllCircuits,
                IsOn(BomPage.RequireAllCircuits));
            Settings.SetBool(ConfigKeys.FillDeviceReserveAutoDetect,
                IsOn(BomPage.DeviceReserveAutoDetect));

            // 输出结构与日志/操作记录
            Settings.Set(ConfigKeys.LogLevel, SelectedLogLevel());
            Settings.Set(ConfigKeys.LogRetentionDays,
                IntegerText(LogsPage.LogRetentionDays));
            Settings.Set(ConfigKeys.HistoryRetentionMonths,
                IntegerText(LogsPage.HistoryRetentionMonths));
        }

        private string SelectedLogLevel()
        {
            string value = Convert.ToString(
                (LogsPage.LogLevel.SelectedItem as ComboBoxItem)?.Tag) ?? "";
            return string.Equals(value, "WARN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "ERROR", StringComparison.OrdinalIgnoreCase)
                ? value.ToUpperInvariant() : "INFO";
        }

        private static void SelectColor(ComboBox box, short colorIndex)
        {
            foreach (CadColorChoice choice in box.Items.Cast<CadColorChoice>()
                .Where(choice => choice.Index == colorIndex))
            {
                box.SelectedItem = choice;
                return;
            }
            box.SelectedIndex = 0;
        }

        private static short SelectedColorIndex(ComboBox box, short fallback)
            => (box.SelectedItem as CadColorChoice)?.Index ?? fallback;

        private static bool IsOn(ToggleSwitch toggle) => toggle.IsChecked == true;

        private static double Number(NumberBox box)
        {
            double value = box.Value ?? box.Minimum;
            return Math.Min(box.Maximum, Math.Max(box.Minimum, value));
        }

        private static string NumberText(NumberBox box)
            => Number(box).ToString("0.##", CultureInfo.InvariantCulture);

        private static string IntegerText(NumberBox box)
            => Convert.ToInt32(Number(box)).ToString(CultureInfo.InvariantCulture);

        private static void SetNumber(NumberBox box, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = box.Minimum;
            box.Value = Math.Min(box.Maximum, Math.Max(box.Minimum, value));
        }

        // ---------- 机台数据：浏览 / 显式刷新 / 快照状态 ----------

        private void OnBrowseExcel(object sender, RoutedEventArgs e)
        {
            string current = MachinePage.FillExcelPath.Text.Trim();
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                Title = "选择 Excel 数据源",
                CheckFileExists = true,
                FileName = File.Exists(current) ? current : ""
            };
            if (dialog.ShowDialog(this) == true)
                MachinePage.FillExcelPath.Text = dialog.FileName;
        }

        private async void RefreshMachineWorkbook()
        {
            string source = MachinePage.FillExcelPath.Text.Trim();
            if (source.Length == 0)
            {
                ShowMachineInfo(InfoBarSeverity.Informational, "请先选择或输入机台数据源。");
                return;
            }
            bool remote = MachineWorkbookSource.IsRemote(source);
            if (!remote && !File.Exists(source))
            {
                ShowMachineInfo(InfoBarSeverity.Warning, "机台数据文件不存在：" + source);
                return;
            }
            // Refresh is an explicit user action, so remember the source before
            // the asynchronous import even if the settings window is later closed.
            Settings.Set(ConfigKeys.FillExcelPath, source);
            // 每次点击都写日志：现场反馈“点了没反应”时先看这里有没有开始/结束记录。
            Log.Info("U1SET 机台数据刷新开始（" + (remote ? "网络下载" : "本地文件") + "）: "
                + source);
            _machineRefreshInProgress = true;
            var button = MachinePage.RefreshButton;
            button.IsEnabled = false;
            string oldText = Convert.ToString(button.Content);
            button.Content = "刷新中...";
            try
            {
                // D-041：网络地址只在这一步下载（GET、不上传）；下载或解析失败时
                // 保留上一次成功快照，不阻塞用户继续使用。
                MachineWorkbookSourceResult result = null;
                await Task.Run(() => { result = MachineWorkbookSource.Refresh(source); });
                string message = result.UsedCachedFallback
                    ? "网络下载失败，继续使用上次 SQLite 快照。"
                    : remote ? "网络机台 Excel 已下载并写入 SQLite 快照。"
                        : "机台 Excel 已解析并写入 SQLite 快照。";
                if (!result.UsedCachedFallback && result.Snapshot != null)
                    message += " 有效回路: " + result.Snapshot.RowCount + " 行。";
                if (!string.IsNullOrWhiteSpace(result.Warning))
                    message += " " + result.Warning;
                Log.Info("U1SET 机台数据刷新完成: " + message);
                if (!IsLoaded) return;
                UpdateMachineSnapshotStatus();
                ShowMachineInfo(
                    result.UsedCachedFallback || !string.IsNullOrWhiteSpace(result.Warning)
                        ? InfoBarSeverity.Warning : InfoBarSeverity.Success, message);
            }
            catch (Exception ex)
            {
                Log.Error("U1SET refresh machine workbook failed", ex);
                if (!IsLoaded) return;
                ShowMachineInfo(InfoBarSeverity.Warning, "刷新失败：" + ex.Message);
            }
            finally
            {
                if (IsLoaded)
                {
                    button.Content = oldText;
                    button.IsEnabled = true;
                }
                _machineRefreshInProgress = false;
            }
        }

        private void ShowMachineInfo(InfoBarSeverity severity, string message)
        {
            MachinePage.MachineInfoBar.Severity = severity;
            MachinePage.MachineInfoBar.Message = message ?? "";
            MachinePage.MachineInfoBar.IsOpen = true;
        }

        private void UpdateMachineSnapshotStatus()
        {
            string source = MachinePage.FillExcelPath.Text.Trim();
            if (source.Length == 0)
            {
                MachinePage.SourceTypeText.Text = "未选择数据源";
                MachinePage.MachineSnapshotStatus.Text = "未选择数据源";
                return;
            }
            bool remote = MachineWorkbookSource.IsRemote(source);
            MachinePage.SourceTypeText.Text = remote
                ? "网络地址 · 点“刷新”或执行 U1DATA 时下载（只下载，不上传）"
                : "本地/共享文件夹文件";
            if (!MachineWorkbookSource.TryGetSnapshot(source,
                out MachineWorkbookSnapshotInfo snapshot))
            {
                MachinePage.MachineSnapshotStatus.Text = "尚未刷新"
                    + (remote ? "（点“刷新”下载数据）" : "");
                return;
            }
            string refreshed = snapshot.RefreshedUtc;
            if (DateTime.TryParse(snapshot.RefreshedUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime utc))
                refreshed = utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture);
            MachinePage.MachineSnapshotStatus.Text = "已刷新 · " + snapshot.RowCount
                + " 个有效回路 · " + refreshed;
        }

        // ---------- 输出目录 ----------

        /// <summary>默认输出根目录文案（Core 与 Features 使用同一定义）。</summary>
        private static string DefaultOutputRoot()
        {
            try { return OutputLayout.DefaultRootDirectory(); }
            catch (Exception) { return "我的文档\\UNCAD输出"; }
        }

        private void OnBrowseOutput(object sender, RoutedEventArgs e)
        {
            string current = OutputPage.SubmitFolder.Text.Trim();
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择输出根目录",
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(current) ? current : ""
            })
            {
                if (dialog.ShowDialog(new WpfWindowHandle(this))
                    == System.Windows.Forms.DialogResult.OK)
                    OutputPage.SubmitFolder.Text = dialog.SelectedPath;
            }
        }

        private string CurrentOutputRoot()
        {
            string configured = OutputPage.SubmitFolder.Text.Trim();
            if (configured.Length > 0) return configured;
            try { return OutputLayout.DefaultRootDirectory(); }
            catch (Exception) { return ""; }
        }

        private void OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warn("打开目录失败: " + ex.Message);
                ShowShareStatus(InfoBarSeverity.Warning, "无法打开目录：" + ex.Message);
            }
        }

        // ---------- 配置分享 ----------

        private void ShowShareStatus(InfoBarSeverity severity, string message)
        {
            OutputPage.ShareStatus.Severity = severity;
            OutputPage.ShareStatus.Message = message ?? "";
            OutputPage.ShareStatus.IsOpen = !string.IsNullOrWhiteSpace(message);
        }

        /// <summary>导出本机配置为分享码并复制到剪贴板。</summary>
        private void CopyConfigToClipboard()
        {
            try
            {
                string code = ConfigShareService.Export();
                if (string.IsNullOrWhiteSpace(code))
                {
                    ShowShareStatus(InfoBarSeverity.Warning, "本机没有可导出的配置项。");
                    return;
                }
                Clipboard.SetText(code);
                ShowShareStatus(InfoBarSeverity.Success, "已复制配置分享码（" + code.Length
                    + " 字符）。在另一台电脑的 U1SET 中粘贴即可导入。");
            }
            catch (Exception ex)
            {
                Log.Error("导出配置分享码失败", ex);
                ShowShareStatus(InfoBarSeverity.Error, "导出失败：" + ex.Message);
            }
        }

        /// <summary>从剪贴板读取分享码 → 预览变更 → 用户确认后原子导入。</summary>
        private void ImportConfigFromClipboard()
        {
            string code;
            try
            {
                code = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            }
            catch (Exception ex)
            {
                Log.Warn("读取剪贴板失败: " + ex.Message);
                ShowShareStatus(InfoBarSeverity.Warning, "无法读取剪贴板：" + ex.Message);
                return;
            }
            if (code.Length == 0)
            {
                ShowShareStatus(InfoBarSeverity.Warning,
                    "剪贴板里没有配置分享码。请先在源电脑导出并复制。");
                return;
            }
            ConfigSharePreview preview;
            try
            {
                preview = ConfigShareService.Preview(code);
            }
            catch (Exception ex)
            {
                Log.Warn("配置分享码解析失败: " + ex.Message);
                ShowShareStatus(InfoBarSeverity.Error, "分享码无效：" + ex.Message);
                return;
            }
            var dialog = new ConfigSharePreviewWindow(preview) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            try
            {
                ConfigShareImportResult result = ConfigShareService.Import(code);
                ShowShareStatus(InfoBarSeverity.Success, "已导入 " + result.AppliedCount
                    + " 项配置"
                    + (result.SkippedCount > 0
                        ? "，跳过 " + result.SkippedCount + " 项（未知或禁止分享的键）。"
                        : "。"));
                // 导入已经写入注册表；重新载入界面值，避免用户随后按“保存”用旧值覆盖。
                LoadValues();
            }
            catch (Exception ex)
            {
                Log.Error("导入配置分享码失败", ex);
                ShowShareStatus(InfoBarSeverity.Error, "导入失败：" + ex.Message);
            }
        }

        // ---------- 日志与操作记录 ----------

        private static string LogDirectory()
        {
            string directory = FileLogger.Instance.LogDirectory;
            return string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData),
                    "UNCAD", "logs")
                : directory;
        }

        /// <summary>读取最近操作记录（本地 JSONL，最多 200 条）。</summary>
        private void RefreshOperationLog()
        {
            try
            {
                IReadOnlyList<OperationLogEntry> entries = OperationLog.ReadRecent(200);
                var rows = new List<OperationLogRow>(entries.Count);
                foreach (OperationLogEntry entry in entries)
                {
                    rows.Add(new OperationLogRow(
                        entry.Timestamp.LocalDateTime.ToString(
                            "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        entry.Command, entry.Result, entry.Summary,
                        entry.DurationMs.ToString(CultureInfo.InvariantCulture)));
                }
                LogsPage.OperationLogGrid.ItemsSource = rows;
                bool hasEntries = rows.Count > 0;
                LogsPage.OperationLogGrid.Visibility = hasEntries
                    ? Visibility.Visible : Visibility.Collapsed;
                LogsPage.OperationEmpty.Visibility = hasEntries
                    ? Visibility.Collapsed : Visibility.Visible;
                LogsPage.OperationStatus.Text = hasEntries
                    ? "最近 " + rows.Count + " 条 · 目录 " + OperationLog.Directory
                    : "暂无操作记录。";
            }
            catch (Exception ex)
            {
                Log.Warn("读取操作记录失败: " + ex.Message);
                LogsPage.OperationStatus.Text = "读取操作记录失败：" + ex.Message;
            }
        }

        // ---------- 辅助 ----------

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match) return match;
                T deeper = FindChild<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        /// <summary>把 WPF 窗口句柄暴露给 WinForms FolderBrowserDialog 做属主。</summary>
        private sealed class WpfWindowHandle : System.Windows.Forms.IWin32Window
        {
            private readonly IntPtr _handle;

            public WpfWindowHandle(Window window)
            {
                _handle = new WindowInteropHelper(window).Handle;
            }

            public IntPtr Handle => _handle;
        }
    }

    /// <summary>操作记录表格行（只读字符串，格式化在装载时完成）。</summary>
    internal sealed class OperationLogRow
    {
        public OperationLogRow(string time, string command, string result,
            string summary, string duration)
        {
            Time = time;
            Command = command;
            Result = result;
            Summary = summary;
            Duration = duration;
        }

        public string Time { get; }
        public string Command { get; }
        public string Result { get; }
        public string Summary { get; }
        public string Duration { get; }
    }
}
