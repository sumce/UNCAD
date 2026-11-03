using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using UNCAD.Core.Dwg;
using UNCAD.Core.Text;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>选择树的节点：机台（有子）或回路（叶子），占位节点不可勾选。</summary>
    public sealed class XLayoutSelectionNode : INotifyPropertyChanged
    {
        private string _header = "";
        private bool _isChecked;
        private bool _isExpanded;

        internal XLayoutSelectionNode(string header, string machineId, string deviceName,
            bool isPlaceholder)
        {
            Header = header;
            MachineId = machineId ?? "";
            DeviceName = deviceName ?? "";
            IsPlaceholder = isPlaceholder;
            Children = new ObservableCollection<XLayoutSelectionNode>();
        }

        public string Header
        {
            get { return _header; }
            set
            {
                if (_header == value) return;
                _header = value;
                OnPropertyChanged();
            }
        }

        public string MachineId { get; }
        public string DeviceName { get; }

        /// <summary>占位节点（如"没有匹配"提示）不承载回路、不可勾选。</summary>
        public bool IsPlaceholder { get; }

        public bool ShowCheckBox => !IsPlaceholder;

        public ObservableCollection<XLayoutSelectionNode> Children { get; }

        public bool IsChecked
        {
            get { return _isChecked; }
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnPropertyChanged();
                CheckChanged?.Invoke(this);
            }
        }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
                ExpandedChanged?.Invoke(this, value);
            }
        }

        internal event Action<XLayoutSelectionNode> CheckChanged;
        internal event Action<XLayoutSelectionNode, bool> ExpandedChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// XLAYOUT 排版前的机台/回路选择窗（3.0 WPF 版，D-038，P2 工作台模式）。
    /// 左侧是"机台 → 回路"两级树，都带复选框；顶部筛选框支持空格/逗号分隔
    /// 多个关键词（任一命中即可，机台ID 与回路名都参与匹配）；
    /// 排列方案下拉选择本次排版的行序策略（D-045），选择会记住下次默认。
    /// **只有勾选且命中当前筛选的回路**才会参与排版。
    /// </summary>
    public sealed partial class XLayoutSelectionWindow : FluentWindow
    {
        /// <summary>用户点了"确定"为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        /// <summary>确定后参与排版的回路（勾选且命中当前筛选）。</summary>
        public IReadOnlyList<XLayoutFrameItem> SelectedItems { get; private set; }
            = Array.Empty<XLayoutFrameItem>();

        /// <summary>用户选择的排列方案（D-045）；未动下拉时 = 构造传入的初始值。</summary>
        public Core.Dwg.XLayoutArrangeMode ArrangeMode => SelectedArrangeMode();

        /// <summary>当前筛选关键词（调试/测试用）。</summary>
        public IReadOnlyList<string> Terms => _terms;

        /// <summary>可见（命中筛选）的回路数量。</summary>
        public int VisibleCircuitCount => CountVisibleItems();

        /// <summary>树控件（测试用）。</summary>
        internal TreeView Tree => SelectionTree;

        /// <summary>顶层（机台/占位）节点（测试用）。</summary>
        internal ObservableCollection<XLayoutSelectionNode> Roots => _roots;

        private readonly List<XLayoutFrameItem> _items;
        private readonly HashSet<XLayoutFrameItem> _checked
            = new HashSet<XLayoutFrameItem>();
        private readonly ObservableCollection<XLayoutSelectionNode> _roots
            = new ObservableCollection<XLayoutSelectionNode>();
        private readonly DispatcherTimer _filterTimer;
        private IReadOnlyList<string> _terms = Array.Empty<string>();
        private bool _suppressCascade;

        public XLayoutSelectionWindow(IReadOnlyList<XLayoutFrameItem> items,
            Core.Dwg.XLayoutArrangeMode arrangeMode
                = Core.Dwg.XLayoutArrangeMode.MachineReadiness)
        {
            _items = (items ?? Array.Empty<XLayoutFrameItem>())
                .Where(item => item != null).ToList();
            foreach (XLayoutFrameItem item in _items) _checked.Add(item);
            // 1276 个回路时每敲一个字都重建树会明显卡顿：输入停顿 250ms 再筛选。
            _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _filterTimer.Tick += (sender, args) =>
            {
                _filterTimer.Stop();
                ApplyFilter(FilterBox.Text);
            };
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            ArrangeModeBox.SelectedIndex = arrangeMode
                == Core.Dwg.XLayoutArrangeMode.MachineIdOrder ? 1
                : arrangeMode == Core.Dwg.XLayoutArrangeMode.Upstream ? 2 : 0;
            RebuildTree("");
            UpdateSelectionCount();
            Loaded += (sender, args) => FilterBox.Focus();
        }

        /// <summary>
        /// 勾选且命中当前筛选的回路（树的权威输出：XLAYOUT 只重排这些）。
        /// </summary>
        public IReadOnlyList<XLayoutFrameItem> ComputeSelectedItems()
        {
            return _items.Where(item => _checked.Contains(item)
                && XLayoutSelectionFilter.MatchesCircuit(item.MachineId,
                    item.DeviceName, _terms)).ToList();
        }

        /// <summary>把筛选框文本直接应用到树（跳过 250ms 防抖；测试与回车用）。</summary>
        internal void ApplyFilter(string text)
        {
            _terms = XLayoutSelectionFilter.SplitTerms(text);
            RebuildTree(text);
            UpdateSelectionCount();
        }

        /// <summary>只把当前可见（命中筛选）的回路设为勾选/取消；隐藏项不受影响。</summary>
        internal void SetVisibleChecked(bool value)
        {
            _suppressCascade = true;
            try
            {
                foreach (XLayoutSelectionNode machine in _roots)
                {
                    if (machine.IsPlaceholder) continue;
                    foreach (XLayoutSelectionNode circuit in machine.Children)
                    {
                        if (!IsCircuitVisible(circuit)) continue;
                        circuit.IsChecked = value;
                        if (value) _checked.Add(FindItem(circuit));
                        else _checked.Remove(FindItem(circuit));
                    }
                    SyncMachineCheckState(machine);
                }
            }
            finally { _suppressCascade = false; }
            UpdateSelectionCount();
        }

        // ---------- 树构建 ----------

        private void RebuildTree(string filterText)
        {
            foreach (XLayoutSelectionNode node in _roots)
                Unwire(node);
            _roots.Clear();
            IReadOnlyList<string> terms = XLayoutSelectionFilter.SplitTerms(filterText);
            _terms = terms;

            var machineGroups = _items
                .GroupBy(item => (item.MachineId ?? "").Trim(),
                    IdentityTextNormalizer.Comparer)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (IGrouping<string, XLayoutFrameItem> machine in machineGroups)
            {
                bool machineMatches = XLayoutSelectionFilter.MatchesMachine(
                    machine.Key, terms);
                // 机台 ID 命中时显示它的全部回路；否则只显示命中的回路。
                List<XLayoutFrameItem> shown = machineMatches
                    ? machine.ToList()
                    : machine.Where(item => XLayoutSelectionFilter.MatchesCircuit(
                        item.MachineId, item.DeviceName, terms)).ToList();
                if (shown.Count == 0) continue;

                var machineNode = new XLayoutSelectionNode(
                    machine.Key + "（" + shown.Count + " 个回路）",
                    machine.Key, "", false);
                foreach (XLayoutFrameItem item in shown)
                {
                    var circuitNode = new XLayoutSelectionNode(
                        (item.DeviceName ?? "").Length == 0
                            ? "(未识别回路)" : item.DeviceName,
                        item.MachineId, item.DeviceName, false)
                    {
                        IsChecked = _checked.Contains(item),
                        IsExpanded = true
                    };
                    circuitNode.CheckChanged += OnNodeCheckChanged;
                    machineNode.Children.Add(circuitNode);
                }
                machineNode.IsChecked = machineNode.Children.All(
                    child => child.IsChecked);
                machineNode.CheckChanged += OnNodeCheckChanged;
                machineNode.IsExpanded = true;
                _roots.Add(machineNode);
            }

            if (_roots.Count == 0)
            {
                // 空结果给一个提示节点（不可勾选），避免用户以为窗口坏了。
                _roots.Add(new XLayoutSelectionNode(
                    "没有匹配的机台/回路（调整筛选关键词）", "", "", true));
            }
            SelectionTree.ItemsSource = _roots;
        }

        private void Unwire(XLayoutSelectionNode node)
        {
            node.CheckChanged -= OnNodeCheckChanged;
            foreach (XLayoutSelectionNode child in node.Children) Unwire(child);
        }

        // ---------- 勾选级联 ----------

        private void OnNodeCheckChanged(XLayoutSelectionNode node)
        {
            if (_suppressCascade || node.IsPlaceholder) return;
            if (node.Children.Count > 0)
            {
                // 机台节点：勾选状态向子回路级联。级联期间抑制子节点事件、
                // 由本分支直接维护勾选集合——否则第一个子节点的回同步会把
                // 机台翻回去，打断剩余子节点的级联（重入 bug）。
                _suppressCascade = true;
                try
                {
                    foreach (XLayoutSelectionNode child in node.Children)
                    {
                        child.IsChecked = node.IsChecked;
                        XLayoutFrameItem item = FindItem(child);
                        if (node.IsChecked) _checked.Add(item);
                        else _checked.Remove(item);
                    }
                }
                finally { _suppressCascade = false; }
            }
            else
            {
                // 回路节点：同步勾选集合并向上推导机台状态。
                XLayoutSelectionNode machine = FindParentNode(node);
                XLayoutFrameItem item = FindItem(node);
                if (node.IsChecked) _checked.Add(item);
                else _checked.Remove(item);
                if (machine != null) SyncMachineCheckState(machine);
            }
            UpdateSelectionCount();
        }

        private void SyncMachineCheckState(XLayoutSelectionNode machine)
        {
            bool all = machine.Children.All(child => child.IsChecked);
            if (machine.IsChecked != all) machine.IsChecked = all;
        }

        private XLayoutSelectionNode FindParentNode(XLayoutSelectionNode circuit)
        {
            return _roots.FirstOrDefault(machine => machine.Children.Contains(circuit));
        }

        private XLayoutFrameItem FindItem(XLayoutSelectionNode circuit)
        {
            return _items.First(item =>
                string.Equals((item.MachineId ?? "").Trim(),
                    (circuit.MachineId ?? "").Trim(),
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals((item.DeviceName ?? "").Trim(),
                    (circuit.DeviceName ?? "").Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        // ---------- 可见性 ----------

        private bool IsCircuitVisible(XLayoutSelectionNode circuit)
        {
            return _roots.Any(machine => machine.Children.Contains(circuit));
        }

        private int CountVisibleItems()
        {
            return _roots.Sum(machine => machine.Children.Count);
        }

        // ---------- 排列方案（D-045） ----------

        private Core.Dwg.XLayoutArrangeMode SelectedArrangeMode()
        {
            switch (ArrangeModeBox.SelectedIndex)
            {
                case 1: return Core.Dwg.XLayoutArrangeMode.MachineIdOrder;
                case 2: return Core.Dwg.XLayoutArrangeMode.Upstream;
                default: return Core.Dwg.XLayoutArrangeMode.MachineReadiness;
            }
        }

        private void OnArrangeModeChanged(object sender, SelectionChangedEventArgs e)
        {
            // 选择即生效：确认时窗口的 ArrangeMode 属性读取当前下拉值。
        }

        // ---------- 按钮 ----------

        private void OnCheckAll(object sender, System.Windows.RoutedEventArgs e)
            => SetVisibleChecked(true);

        private void OnUncheckAll(object sender, System.Windows.RoutedEventArgs e)
            => SetVisibleChecked(false);

        private void OnInvert(object sender, System.Windows.RoutedEventArgs e)
        {
            _suppressCascade = true;
            try
            {
                foreach (XLayoutSelectionNode machine in _roots)
                {
                    if (machine.IsPlaceholder) continue;
                    foreach (XLayoutSelectionNode circuit in machine.Children)
                    {
                        if (!IsCircuitVisible(circuit)) continue;
                        circuit.IsChecked = !circuit.IsChecked;
                        if (circuit.IsChecked) _checked.Add(FindItem(circuit));
                        else _checked.Remove(FindItem(circuit));
                    }
                    SyncMachineCheckState(machine);
                }
            }
            finally { _suppressCascade = false; }
            UpdateSelectionCount();
        }

        private void OnExpandAll(object sender, System.Windows.RoutedEventArgs e)
            => SetAllExpanded(true);

        private void OnCollapseAll(object sender, System.Windows.RoutedEventArgs e)
            => SetAllExpanded(false);

        private void SetAllExpanded(bool expanded)
        {
            foreach (XLayoutSelectionNode machine in _roots)
                machine.IsExpanded = expanded;
        }

        private void OnConfirm(object sender, System.Windows.RoutedEventArgs e)
        {
            // 权威校验：空选择绝不带着 Confirmed 关窗。
            IReadOnlyList<XLayoutFrameItem> selected = ComputeSelectedItems();
            if (selected.Count == 0)
            {
                WarningBar.Message = "至少选择一个回路再开始排版。";
                WarningBar.IsOpen = true;
                return;
            }
            Confirmed = true;
            SelectedItems = selected;
            Close();
        }

        private void OnCancel(object sender, System.Windows.RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        // ---------- 筛选 ----------

        private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        }

        private void OnFilterKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _filterTimer.Stop();
                ApplyFilter(FilterBox.Text);
                e.Handled = true;
            }
        }

        private void UpdateSelectionCount()
        {
            int selected = ComputeSelectedItems().Count;
            SelectionCountText.Text = "已选 " + selected + " / "
                + _items.Count + " 个回路";
        }
    }
}

