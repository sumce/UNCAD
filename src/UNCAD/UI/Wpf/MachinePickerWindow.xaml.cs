using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using UNCAD.Core.Excel;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// U1F 机台选择窗（3.0 WPF 版，D-038，P2 工作台模式）。
    ///   ① 机台ID 输入（前缀优先的相似建议，最多 8 个，回车/点击选择）
    ///   ② 选中机台后：顶部显示机台摘要（区域/机台ID/回路数），中间列表显示
    ///      全部回路（设备名称）及 盘柜类型/配电详情
    ///   ③ 底部实时预览：本次将写入表格与块属性的内容（单击回路即更新）
    /// </summary>
    public sealed partial class MachinePickerWindow : FluentWindow
    {
        /// <summary>用户点了“确定”为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        public MachineRow Selected => CircuitGrid.SelectedItem is CircuitRow row
            ? row.Row : null;

        private readonly List<string> _machineIds;
        private readonly Func<string, List<MachineRow>> _lookup;
        private readonly Func<MachineRow, string> _previewBuilder;
        private readonly ObservableCollection<CircuitRow> _circuits =
            new ObservableCollection<CircuitRow>();
        /// <summary>回路列表的筛选视图（D-046）：机台回路多时按关键词快速定位。</summary>
        private ICollectionView _circuitView;

        public MachinePickerWindow(IList<string> machineIds,
            Func<string, List<MachineRow>> lookup,
            Func<MachineRow, string> previewBuilder = null)
        {
            _machineIds = new List<string>(machineIds ?? new List<string>());
            _lookup = lookup ?? (id => new List<MachineRow>());
            _previewBuilder = previewBuilder;

            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            Title = "U1F · " + Branding.Nameplate;
            HeaderBar.Title = Title;

            CircuitGrid.ItemsSource = _circuits;
            _circuitView = CollectionViewSource.GetDefaultView(_circuits);
            // 浮层被点外面关闭时同步隐藏标记，避免下次更新建议时状态错乱。
            SuggestPopup.Closed += (sender, args) =>
            {
                if (MachineSuggest.Visibility == Visibility.Visible)
                    MachineSuggest.Visibility = Visibility.Collapsed;
            };
            ConfirmButton.IsEnabled = false;
            UpdatePreview();
            Loaded += (sender, args) => MachineInput.Focus();
        }

        private void OnMachineInputTextChanged(object sender, TextChangedEventArgs e)
            => UpdateSuggestions();

        private void OnMachineInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (MachineSuggest.Visibility == Visibility.Visible
                    && MachineSuggest.Items.Count > 0)
                    MachineSuggest.SelectedIndex = 0;
                CommitMachine();
            }
            else if (e.Key == Key.Down
                && MachineSuggest.Visibility == Visibility.Visible
                && MachineSuggest.Items.Count > 0)
            {
                MachineSuggest.SelectedIndex = 0;
                MachineSuggest.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                MachineInput.Clear();
                e.Handled = true;
            }
        }

        private void OnSuggestKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitMachine();
            }
            else if (e.Key == Key.Escape)
            {
                HideSuggestions();
                MachineInput.Focus();
                e.Handled = true;
            }
        }

        private void OnSuggestMouseClick(object sender, MouseButtonEventArgs e)
            => CommitMachine();

        /// <summary>输入变化 → 相似机台建议（前缀优先，含包含匹配，最多 8 个）。</summary>
        private void UpdateSuggestions()
        {
            string kw = (MachineInput.Text ?? "").Trim();
            if (kw.Length == 0)
            {
                HideSuggestions();
                return;
            }
            List<string> list = _machineIds
                .Where(id => id != null
                    && id.StartsWith(kw, StringComparison.OrdinalIgnoreCase))
                .Concat(_machineIds
                    .Where(id => id != null
                        && id.IndexOf(kw, StringComparison.OrdinalIgnoreCase) > 0
                        && !id.StartsWith(kw, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
            MachineSuggest.Items.Clear();
            foreach (string id in list) MachineSuggest.Items.Add(id);
            if (list.Any(id => string.Equals(id, kw, StringComparison.OrdinalIgnoreCase)))
            {
                CommitMachine();
                return;
            }
            MachineSuggest.Visibility = list.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
            SuggestPopup.IsOpen = MachineSuggest.Visibility == Visibility.Visible;
        }

        private void HideSuggestions()
        {
            MachineSuggest.Visibility = Visibility.Collapsed;
            SuggestPopup.IsOpen = false;
            MachineSuggest.Items.Clear();
        }

        /// <summary>选定机台后按需查询该机台的 SQLite 快照回路。</summary>
        internal void CommitMachine()
        {
            string mid = (MachineSuggest.SelectedItem as string)
                ?? (MachineInput.Text ?? "").Trim();
            MachineInput.Text = mid;
            HideSuggestions();

            _circuits.Clear();
            UpdatePreview();
            ConfirmButton.IsEnabled = false;
            UpdateCircuitCount();

            if (mid.Length == 0)
            {
                SetSummary("请输入机台 ID", false);
                return;
            }

            List<MachineRow> rows = _lookup(mid) ?? new List<MachineRow>();
            bool exact = rows.Count > 0
                && rows[0].MachineId.Equals(mid, StringComparison.OrdinalIgnoreCase);
            if (!exact)
            {
                SetSummary("未找到机台 " + mid + "，请选择上方相似机台", true);
                return;
            }

            // D-057：同机台同名回路（上游编号不同）在「设备 / 回路」上带上游后缀——
            // 两条同名回路在列表里不再长得一模一样，选错回路的概率降到最低。
            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (MachineRow row in rows)
            {
                string countKey = (row.CircuitName ?? "").Trim();
                nameCounts.TryGetValue(countKey, out int count);
                nameCounts[countKey] = count + 1;
            }
            bool anyDuplicated = nameCounts.Values.Any(count => count > 1);
            foreach (MachineRow row in rows)
            {
                string displayName = string.IsNullOrWhiteSpace(row.CircuitName)
                    ? row.MachineId : row.CircuitName;
                if (anyDuplicated && nameCounts.TryGetValue((row.CircuitName ?? "").Trim(),
                        out int sameName) && sameName > 1)
                    displayName += string.IsNullOrWhiteSpace(row.Fr)
                        ? "（上游未记录）" : "（上游 " + row.Fr.Trim() + "）";
                _circuits.Add(new CircuitRow(row, displayName));
            }
            SetSummary(rows[0].Region + " ｜ 机台 " + rows[0].MachineId
                + " ｜ 共 " + rows.Count + " 个回路"
                + (anyDuplicated ? "（含同名回路，按上游编号区分）" : ""), false);
            ApplyCircuitFilter();
            if (CircuitGrid.SelectedItem == null && _circuits.Count > 0)
                CircuitGrid.SelectedIndex = 0;
        }

        private void SetSummary(string text, bool danger)
        {
            MachineSummary.Text = text;
            MachineSummary.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
                danger ? "SystemFillColorCriticalBrush"
                    : "AccentTextFillColorPrimaryBrush");
        }

        /// <summary>筛选框变化 → 过滤回路列表并刷新计数。</summary>
        private void OnCircuitFilterTextChanged(object sender, TextChangedEventArgs e)
            => ApplyCircuitFilter();

        /// <summary>按设备/回路名、盘柜类型、配电详情匹配关键词（任一命中即可）。</summary>
        private void ApplyCircuitFilter()
        {
            if (_circuitView == null) return;
            string keyword = (CircuitFilterBox.Text ?? "").Trim();
            _circuitView.Filter = keyword.Length == 0
                ? null
                : (Predicate<object>)(item => MatchesCircuit((CircuitRow)item, keyword));
            UpdateCircuitCount();
        }

        private static bool MatchesCircuit(CircuitRow row, string keyword)
        {
            return Contains(row.DisplayName, keyword)
                || Contains(row.UpstreamId, keyword)
                || Contains(row.FacilitySwitch, keyword)
                || Contains(row.PanelType, keyword)
                || Contains(row.Detail, keyword);
        }

        private static bool Contains(string text, string keyword)
            => (text ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>计数与筛选联动：全部时只显示总数，筛选时显示“显示 X / N”。</summary>
        private void UpdateCircuitCount()
        {
            int total = _circuits.Count;
            if (total == 0)
            {
                CircuitCountText.Text = "";
                return;
            }
            int shown = 0;
            if (_circuitView != null)
            {
                foreach (object _ in _circuitView) shown++;
            }
            CircuitCountText.Text = shown == total
                ? "共 " + total + " 个回路"
                : "显示 " + shown + " / " + total + " 个回路";
        }

        /// <summary>底部预览：显示本次将写入的内容。</summary>
        private void UpdatePreview()
        {
            MachineRow row = Selected;
            ConfirmButton.IsEnabled = row != null;
            if (row == null)
            {
                CircuitDetailBox.Text = "";
                PreviewBox.Text = "选择机台与回路后，此处预览将写入的内容。";
                return;
            }
            CircuitDetailBox.Text = BuildCircuitDetail(row);
            // 3.3.3.2（审计 #5）：预览构建链会主动抛——设备块状态冲突
            // InvalidOperationException（D-030 口径）/软管长度非法
            // InvalidDataException，主流程里同因是优雅中止；这里是 WPF 事件
            // 处理器，裸奔会变成未处理异常。同 3.3.0.1 路由预览的降级口径：
            // 预览是展示件，包住降级成文案，绝不炸选择窗。
            if (_previewBuilder != null)
            {
                try
                {
                    PreviewBox.Text = _previewBuilder(row);
                }
                catch (Exception ex)
                {
                    Log.Warn("[U1F] 机台选择窗预览构建失败（不影响选择）：" + ex.Message);
                    PreviewBox.Text = "预览生成失败：" + ex.Message;
                }
            }
            else
            {
                PreviewBox.Text = row.ToString();
            }
        }

        private void OnCircuitSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePreview();
            WarningBar.IsOpen = false;
        }

        private void OnCircuitDoubleClick(object sender, MouseButtonEventArgs e)
            => Confirm();

        private void OnConfirm(object sender, RoutedEventArgs e) => Confirm();

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void Confirm()
        {
            if (Selected == null)
            {
                WarningBar.Message = "请先在列表中选择一个设备/回路。";
                WarningBar.IsOpen = true;
                return;
            }
            Confirmed = true;
            Close();
        }

        private static string DisplayPanelType(string value)
        {
            string type = (value ?? "").Trim();
            return type.Length == 0 || type == "-" ? "未指定" : type;
        }

        private static string BuildCircuitDetail(MachineRow row)
        {
            return "盘柜类型: " + DisplayPanelType(row.Next)
                + "    区域: " + (row.Region ?? "")
                + "    项目序号: " + (row.Seq ?? "") + Environment.NewLine
                + "FR: " + (row.Fr ?? "") + Environment.NewLine
                + "配电详情: " + (row.Detail ?? "") + Environment.NewLine
                + "下游轴位: " + (row.DownstreamAxis ?? "")
                + "    上游轴位: " + (row.UpstreamAxis ?? "")
                + "    电缆: " + (row.Cable ?? "")
                + "    软管: " + (string.IsNullOrWhiteSpace(row.Dia) ? "" : "Φ" + row.Dia);
        }

        /// <summary>回路列表行模型。上游编号/厂务开关列（D-057）：同机台同名回路靠
        /// 上游编号区分，选的时候不用点中行再去预览里看。</summary>
        public sealed class CircuitRow
        {
            public CircuitRow(MachineRow row, string displayName)
            {
                Row = row;
                DisplayName = displayName;
                UpstreamId = (row.Fr ?? "").Trim();
                FacilitySwitch = (row.FacilitySwitch ?? "").Trim();
                PanelType = DisplayPanelType(row.Next);
                Detail = row.Detail ?? "";
            }

            public MachineRow Row { get; }
            public string DisplayName { get; }
            public string UpstreamId { get; }
            public string FacilitySwitch { get; }
            public string PanelType { get; }
            public string Detail { get; }
        }
    }
}
