using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.QuickLine;
using UNCAD.Core.Text;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// U1F/U1U 清单确认窗（3.0 WPF 版，D-038，P2 工作台模式）。
    /// 行为等价迁移自 WinForms FillReviewForm：
    ///  - 基础信息（身份/电缆与预留/软管与轴位）+ 清单选择两个页签；
    ///  - 设备内预留编辑即时联动 SyncCableReserveMeters 重算电缆长度；
    ///  - 未匹配条目勾选禁用且强制 false，不得进入 SelectedRows；
    ///  - 替换固定清单必须锁定原类别（Manual 行除外）；
    ///  - Manual 行与所有行一样仅“数量/长度”与“生成”可编辑；
    ///  - “恢复默认”以会话快照重建自动项。
    /// 权威校验在“确认填充”按钮里（控件级只做即时反馈）。
    /// </summary>
    public sealed partial class FillReviewWindow : FluentWindow
    {
        /// <summary>用户点了“确认填充”为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        public FillReviewData Data { get; private set; }

        private readonly FillReviewData _defaults;
        private readonly BoqCatalogIndex _catalog;
        private readonly FillPlanningOptions _planningOptions;
        private readonly bool _lockIdentity;
        private readonly ObservableCollection<FillReviewRow> _rows =
            new ObservableCollection<FillReviewRow>();
        private bool _synchronizing;

        public FillReviewWindow(FillReviewData data, List<ListItem> catalogItems = null)
            : this(data, new BoqCatalogIndex(catalogItems), FillPlanningOptions.Default)
        {
        }

        public FillReviewWindow(FillReviewData data, BoqCatalogIndex catalog,
            FillPlanningOptions planningOptions)
            : this(data, catalog, planningOptions, false)
        {
        }

        /// <summary>U1U keeps the persisted machine/device identity fixed while editing quantities.</summary>
        public FillReviewWindow(FillReviewData data, BoqCatalogIndex catalog,
            FillPlanningOptions planningOptions, bool lockIdentity)
            : this(data, catalog, planningOptions, lockIdentity, null, null)
        {
        }

        /// <summary>
        /// 3.3.0（D-054）：带 3D 路由预览与图层矫正报告的完整构造（U1U 单框链）。
        /// preview 为 null 时预览区隐藏（U1F/旧行为）；layerCorrectionText 为
        /// null 时不显示图层矫正行。
        /// </summary>
        public FillReviewWindow(FillReviewData data, BoqCatalogIndex catalog,
            FillPlanningOptions planningOptions, bool lockIdentity,
            QuickLineOrbitPreviewModel preview, string layerCorrectionText)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            _catalog = catalog ?? new BoqCatalogIndex(null);
            _planningOptions = planningOptions ?? FillPlanningOptions.Default;
            _lockIdentity = lockIdentity;
            _defaults = Data.Snapshot();

            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            Title = ProductMetadata.ProductName + " · 清单确认";
            HeaderBar.Title = Title;

            // 特性③：左侧 3D 预览（只绘制传入模型，不重建场景）。
            if (preview != null)
            {
                PreviewColumn.Width = new GridLength(300, GridUnitType.Pixel);
                PreviewHost.Visibility = Visibility.Visible;
                OrbitPreview.SetModel(preview);
            }
            // 特性①：图层矫正报告（确认窗里列出，动作在写事务里执行）。
            if (!string.IsNullOrWhiteSpace(layerCorrectionText))
            {
                LayerGovernorText.Text = layerCorrectionText;
                LayerGovernorText.Visibility = Visibility.Visible;
            }

            PanelBox.Items.Add("");
            PanelBox.Items.Add("I-Line盘");
            PanelBox.Items.Add("母线插接口");
            PanelBox.Items.Add("插座盘");

            ReviewGrid.ItemsSource = _rows;

            LoadFromData(Data);
            if (_lockIdentity)
            {
                MachineBox.IsReadOnly = true;
                CircuitBox.IsReadOnly = true;
            }
            PopulateRows(Data.Items);
            UpdateDeleteState();
            Loaded += (sender, args) => MachineBox.Focus();
        }

        // ---- 基础信息字段同步 -------------------------------------------------

        private void LoadFromData(FillReviewData data)
        {
            _synchronizing = true;
            MachineBox.Text = data.Machine.MachineId ?? "";
            RegionBox.Text = data.Machine.Region ?? "";
            CircuitBox.Text = data.Machine.CircuitName ?? "";
            PanelBox.Text = data.Machine.Next ?? "";
            FrBox.Text = data.Machine.Fr ?? "";
            SeqBox.Text = data.Machine.Seq ?? "";
            OriginalCableBox.Text = data.OriginalCableModel ?? "";
            CableBox.Text = data.BoqCableModel ?? "";
            CableMetersBox.Text = data.CableMeters ?? "";
            MeasuredCableBox.Text = data.MeasuredCableMeters ?? "";
            DeviceReserveBox.Text = data.DeviceReserveMm ?? "";
            PanelReserveBox.Text = data.PanelReserveMm ?? "";
            DeviceReserveBox.IsEnabled = data.CableReserveEditable;
            DiameterBox.Text = data.Machine.Dia ?? "";
            DownstreamBox.Text = data.Machine.DownstreamAxis ?? "";
            UpstreamBox.Text = data.Machine.UpstreamAxis ?? "";
            DetailBox.Text = data.Machine.Detail ?? "";
            _synchronizing = false;
        }

        private void OnCableMetersChanged(object sender, TextChangedEventArgs e)
        {
            if (_synchronizing) return;
            Data.SetCableMeters(CableMetersBox.Text);
            RefreshItem(Data.CableItem());
        }

        /// <summary>设备内预留联动：重算电缆总长并回写电缆长度输入与清单行。</summary>
        private void OnDeviceReserveChanged(object sender, TextChangedEventArgs e)
        {
            if (_synchronizing) return;
            Data.DeviceReserveMm = DeviceReserveBox.Text;
            Data.SyncCableReserveMeters();
            _synchronizing = true;
            CableMetersBox.Text = Data.CableMeters ?? "";
            _synchronizing = false;
            RefreshItem(Data.CableItem());
        }

        private void OnDiameterChanged(object sender, TextChangedEventArgs e)
        {
            if (_synchronizing) return;
            // 直径编辑立即重建/更新软管行；软管行可能由此创建（与旧窗体一致，
            // 新建的行不追加到表格，由确认前的规划兜底）。
            FillReviewItem flexible = Data.SetFlexibleConduitDiameter(
                DiameterBox.Text, _catalog, _planningOptions);
            if (flexible != null) RefreshItem(flexible);
        }

        // ---- 清单表格 ---------------------------------------------------------

        private void PopulateRows(IEnumerable<FillReviewItem> items)
        {
            _synchronizing = true;
            _rows.Clear();
            foreach (FillReviewItem item in items)
            {
                var row = new FillReviewRow(item);
                HookRow(row);
                ApplyCatalogState(row);
                _rows.Add(row);
            }
            _synchronizing = false;
            UpdateCount();
            UpdateCatalogWarning();
            if (Data.Items.Any(item => item.RequiresCatalogConfirmation))
                ReviewTabs.SelectedIndex = 1;
        }

        private void HookRow(FillReviewRow row)
            => row.ColumnChanged += OnRowColumnChanged;

        /// <summary>表格单元格变化（勾选/数量）：更新统计；电缆行的数量回写电缆长度。</summary>
        private void OnRowColumnChanged(FillReviewRow row, string column)
        {
            if (_synchronizing) return;
            UpdateCount();
            if (column != "Quantity"
                || row.Item.Category != TableFillCategory.Cable) return;
            _synchronizing = true;
            CableMetersBox.Text = row.Item.Quantity ?? "";
            Data.CableMeters = row.Item.Quantity ?? "";
            _synchronizing = false;
        }

        /// <summary>未匹配行：强制不勾选；替换为固定清单后恢复可勾选。</summary>
        private static void ApplyCatalogState(FillReviewRow row)
        {
            if (row.Item.RequiresCatalogConfirmation) row.Item.Included = false;
            row.Refresh();
        }

        private void RefreshItem(FillReviewItem item)
        {
            foreach (FillReviewRow row in _rows)
            {
                if (!ReferenceEquals(row.Item, item)) continue;
                row.Refresh();
                break;
            }
            UpdateCount();
            UpdateCatalogWarning();
        }

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateDeleteState();

        private void UpdateDeleteState()
        {
            bool hasItem = ReviewGrid.SelectedItem is FillReviewRow;
            ReplaceItemButton.IsEnabled = hasItem;
            RemoveItemButton.IsEnabled = hasItem;
        }

        private void OnCheckAll(object sender, RoutedEventArgs e) => SetAll(true);

        private void OnUncheckAll(object sender, RoutedEventArgs e) => SetAll(false);

        private void SetAll(bool included)
        {
            _synchronizing = true;
            try
            {
                foreach (FillReviewRow row in _rows)
                {
                    if (!row.Item.CatalogMatched) continue;
                    row.Item.Included = included;
                    row.Refresh();
                }
            }
            finally { _synchronizing = false; }
            UpdateCount();
        }

        /// <summary>恢复默认：以会话快照重建自动项与基础信息（含已确认的替换不保留）。</summary>
        private void OnRestoreDefaults(object sender, RoutedEventArgs e)
        {
            Data = _defaults.Snapshot();
            LoadFromData(Data);
            PopulateRows(Data.Items);
            UpdateDeleteState();
        }

        private void OnRemoveSelectedItem(object sender, RoutedEventArgs e)
        {
            if (!(ReviewGrid.SelectedItem is FillReviewRow row)) return;
            if (!Data.RemoveItem(row.Item)) return;
            _rows.Remove(row);
            UpdateCount();
            UpdateCatalogWarning();
            UpdateDeleteState();
        }

        /// <summary>嵌套固定清单选择窗的显示接缝：默认走 AutoCAD 宿主 API（D-038）。
        /// 测试注入替身，避免测试进程依赖 AutoCAD 程序集。</summary>
        internal Action<Window> ShowNestedPicker { get; set; }

        private void ShowPicker(Window picker)
            => (ShowNestedPicker ?? DefaultShowPicker)(picker);

        private static void DefaultShowPicker(Window picker)
            => Autodesk.AutoCAD.ApplicationServices.Application
                .ShowModalWindow(picker);

        // ---- 固定清单添加 / 替换 ----------------------------------------------

        private void OnAddCatalogItem(object sender, RoutedEventArgs e)
        {
            // 连续添加：对话框保持打开，每选中一项立即加入评审表，直到用户关闭。
            var picker = new ManualListItemWindow(_catalog.SelectableItems);
            picker.PickRequested += AddCatalogRow;
            ShowPicker(picker);
            if (!picker.Confirmed) return;
            if (picker.SelectedItem != null)
                AddCatalogRow(picker.SelectedItem, picker.Quantity);
        }

        internal void AddCatalogRow(ListItem catalogItem, string quantity)
        {
            FillReviewItem item = Data.AddCatalogItem(catalogItem, quantity);
            var row = new FillReviewRow(item);
            HookRow(row);
            ApplyCatalogState(row);
            _rows.Add(row);
            ReviewTabs.SelectedIndex = 1;
            ReviewGrid.SelectedItem = row;
            UpdateCount();
            UpdateDeleteState();
        }

        private void OnReplaceSelectedItem(object sender, RoutedEventArgs e)
        {
            if (ReviewGrid.SelectedItem is FillReviewRow row)
                OpenCatalogPicker(row.Item);
        }

        private void OnResolveFirstUnmatched(object sender, RoutedEventArgs e)
        {
            FillReviewItem item = Data.Items.FirstOrDefault(candidate =>
                candidate.RequiresCatalogConfirmation);
            if (item == null) return;
            FocusReviewItem(item);
            OpenCatalogPicker(item);
        }

        /// <summary>构造替换用固定清单选择窗（锁定原类别）；测试通过它核对类别跳转。</summary>
        internal ManualListItemWindow CreateCatalogPicker(string category)
            => new ManualListItemWindow(_catalog.SelectableItems, true, category);

        private void OpenCatalogPicker(FillReviewItem item)
        {
            string category = FillReviewData.CategoryName(item.Category);
            ManualListItemWindow picker = CreateCatalogPicker(category);
            ShowPicker(picker);
            if (!picker.Confirmed || picker.SelectedItem == null) return;
            ApplyCatalogReplacement(item, category, picker.SelectedItem);
        }

        internal void ApplyCatalogReplacement(FillReviewItem item, string category,
            ListItem selected)
        {
            string selectedCategory = (selected.Category ?? "").Trim();
            if (item.Category != TableFillCategory.Manual
                && !string.Equals(selectedCategory, category, StringComparison.Ordinal))
            {
                ShowError("只能选择“" + category + "”类别的固定清单项目。", "固定清单类别不一致");
                return;
            }
            Data.ReplaceWithCatalogItem(item, selected, _catalog);
            if (item.Category == TableFillCategory.Cable)
            {
                _synchronizing = true;
                CableBox.Text = Data.BoqCableModel;
                _synchronizing = false;
            }
            RefreshItem(item);
            UpdateDeleteState();
        }

        private void FocusReviewItem(FillReviewItem item)
        {
            ReviewTabs.SelectedIndex = 1;
            foreach (FillReviewRow row in _rows)
            {
                if (!ReferenceEquals(row.Item, item)) continue;
                ReviewGrid.SelectedItem = row;
                break;
            }
        }

        // ---- 确认（权威校验） ---------------------------------------------------

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            // 绑定是 PropertyChanged 即时回写，无悬挂编辑；直接按当前值校验。
            if (string.IsNullOrWhiteSpace(MachineBox.Text)
                || string.IsNullOrWhiteSpace(CircuitBox.Text))
            {
                ReviewTabs.SelectedIndex = 0;
                (string.IsNullOrWhiteSpace(MachineBox.Text) ? MachineBox : CircuitBox)
                    .Focus();
                ShowError("机台ID和设备/回路不能为空。", "U1F");
                return;
            }
            if (!ValidQuantity(CableMetersBox.Text))
            {
                ReviewTabs.SelectedIndex = 0;
                CableMetersBox.Focus();
                ShowError("电缆长度必须是非负数字或留空。", "U1F");
                return;
            }
            Data.DeviceReserveMm = (DeviceReserveBox.Text ?? "").Trim();
            if (Data.CableReserveEditable && Data.DeviceReserveMm.Length > 0
                && (!double.TryParse(Data.DeviceReserveMm,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double reserve)
                    || reserve < 0d || reserve > 100000d))
            {
                ReviewTabs.SelectedIndex = 0;
                DeviceReserveBox.Focus();
                DeviceReserveBox.SelectAll();
                ShowError("设备内预留必须是 0~100000 的毫米数。", "U1U");
                return;
            }
            string diameterText = (DiameterBox.Text ?? "").Trim();
            if (diameterText.Length > 0
                && !ConduitDiameter.TryNormalize(diameterText, out _))
            {
                ReviewTabs.SelectedIndex = 0;
                DiameterBox.Focus();
                DiameterBox.SelectAll();
                ShowError("软管直径格式无效，请输入数字、DN32、Φ32或32mm等格式。", "U1F");
                return;
            }
            // 未匹配行的默认未勾选仅用于阻止写出，不代表用户已决定忽略。
            // 必须明确选择固定清单身份或删除该行，避免关键材料被静默遗漏。
            FillReviewItem unmatched = Data.Items.FirstOrDefault(item =>
                item.RequiresCatalogConfirmation);
            if (unmatched != null)
            {
                FocusReviewItem(unmatched);
                string category = FillReviewData.CategoryName(unmatched.Category);
                ShowWarning("找不到对应型号的" + category + "：" + MissingSubject(unmatched)
                    + "。将自动打开“" + category + "”待选页，请从固定清单中选择对应项目。",
                    "固定清单未匹配");
                OpenCatalogPicker(unmatched);
                return;
            }

            for (int index = 0; index < Data.Items.Count; index++)
            {
                FillReviewItem item = Data.Items[index];
                if (!item.Included) continue;
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    ReviewTabs.SelectedIndex = 1;
                    if (index < _rows.Count) ReviewGrid.SelectedItem = _rows[index];
                    ShowError("清单项名称不能为空。", "U1F");
                    return;
                }
                if (ValidQuantity(item.Quantity)) continue;
                ReviewTabs.SelectedIndex = 1;
                if (index < _rows.Count) ReviewGrid.SelectedItem = _rows[index];
                ShowError("“" + item.Name + "”的数量/长度必须是非负数字或留空。", "U1F");
                return;
            }
            Data.SetCableMeters(CableMetersBox.Text);
            if (!_lockIdentity) Data.Machine.MachineId = MachineBox.Text.Trim();
            Data.Machine.Region = RegionBox.Text.Trim();
            if (!_lockIdentity) Data.Machine.CircuitName = CircuitBox.Text.Trim();
            Data.Machine.Next = (PanelBox.Text ?? "").Trim();
            Data.Machine.Fr = FrBox.Text.Trim();
            Data.Machine.Seq = SeqBox.Text.Trim();
            // Diameter edits are applied by OnDiameterChanged. Do not re-create a
            // deleted hose row here from the stale text-box value; the presence of a
            // Ruanguan block and the user's row deletion are resolved by FillFeature
            // before/after this dialog.
            Data.Machine.DownstreamAxis = DownstreamBox.Text.Trim();
            Data.Machine.UpstreamAxis = UpstreamBox.Text.Trim();
            Data.Machine.Detail = DetailBox.Text.Trim();
            HideError();
            Confirmed = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private string MissingSubject(FillReviewItem item)
        {
            if (item.Category == TableFillCategory.Cable)
            {
                string model = (Data.BoqCableModel ?? "").Trim();
                if (model.Length > 0) return model;
            }
            return string.IsNullOrWhiteSpace(item.Name)
                ? "未提供型号" : item.Name.Trim().Replace(@"\P", " / ");
        }

        // ---- 状态条 -------------------------------------------------------------

        internal void UpdateCatalogWarning()
        {
            List<string> missing = FillAnomalyDetector.DetectCatalog(Data)
                .Select(anomaly => FillReviewData.CategoryName(anomaly.Category) + "："
                    + anomaly.Subject.Replace("\\P", " / "))
                .Distinct(StringComparer.Ordinal).ToList();
            CatalogWarningBar.IsOpen = missing.Count > 0;
            ResolveMissingButton.IsEnabled = missing.Count > 0;
            if (missing.Count > 0)
                CatalogWarningBar.Message = "[BOQ-CATALOG-MISSING] 找不到对应型号："
                    + string.Join("；", missing)
                    + "。点击“选择未匹配项”会自动打开对应类别，或删除不需要的项目。";
        }

        private void UpdateCount()
        {
            int total = Data.Items.Count;
            int selected = Data.Items.Count(item => item.Included);
            CountText.Text = "将生成 " + selected + " / " + total + " 行";
            CountText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
                selected == total
                    ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush");
            CountProgress.Maximum = Math.Max(1, total);
            CountProgress.Value = Math.Min(selected, CountProgress.Maximum);
            CountProgress.SetResourceReference(ProgressBar.ForegroundProperty,
                selected == total
                    ? "SystemFillColorSuccessBrush" : "AccentFillDefaultBrush");
        }

        private void ShowError(string message, string title)
        {
            ErrorBar.Severity = InfoBarSeverity.Error;
            ErrorBar.Title = title;
            ErrorBar.Message = message;
            ErrorBar.IsOpen = true;
        }

        private void ShowWarning(string message, string title)
        {
            ErrorBar.Severity = InfoBarSeverity.Warning;
            ErrorBar.Title = title;
            ErrorBar.Message = message;
            ErrorBar.IsOpen = true;
        }

        private void HideError() => ErrorBar.IsOpen = false;

        internal static bool ValidQuantity(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) return true;
            double parsedValue;
            bool parsed = double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parsedValue)
                || double.TryParse(text, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out parsedValue);
            return parsed && parsedValue >= 0 && !double.IsNaN(parsedValue)
                && !double.IsInfinity(parsedValue);
        }
    }
}
