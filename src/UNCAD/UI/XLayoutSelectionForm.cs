using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Dwg;
using UNCAD.Core.Text;

namespace UNCAD.UI
{
    /// <summary>
    /// XLAYOUT 排版前的机台/回路选择窗。
    ///
    /// 左侧是"机台 → 回路"两级树，都带复选框；顶部筛选框支持用空格/逗号等分隔多个
    /// 关键词（AND 语义，类似 Excel 筛选，机台ID 与回路名都参与匹配）。
    /// **只有勾选且命中当前筛选的回路**才会参与排版。
    /// </summary>
    public sealed class XLayoutSelectionForm : DpiAwareForm
    {
        private readonly IReadOnlyList<XLayoutFrameItem> _items;
        private readonly TreeView _tree = new TreeView { Name = "XLayoutTree" };
        private readonly TextBox _filter = new TextBox { Name = "XLayoutFilter" };
        private readonly Label _status = new Label { Name = "XLayoutStatus" };
        private readonly Button _ok;
        private readonly HashSet<XLayoutFrameItem> _checked = new HashSet<XLayoutFrameItem>();
        private readonly HashSet<string> _expanded = new HashSet<string>(
            IdentityTextNormalizer.Comparer);
        private IReadOnlyList<string> _terms = Array.Empty<string>();
        private bool _updatingChecks;

        public XLayoutSelectionForm(IReadOnlyList<XLayoutFrameItem> items)
        {
            _items = (items ?? Array.Empty<XLayoutFrameItem>())
                .Where(item => item != null).ToList();
            foreach (XLayoutFrameItem item in _items) _checked.Add(item);

            DialogLayout.Apply(this, "XLAYOUT · 选择要排版的机台/回路",
                new Size(720, 620), new Size(560, 460));

            var header = UiTheme.Header("选择要排版的机台/回路",
                "勾选后只重排这些回路；筛选框支持空格或逗号分隔多个关键词（同时命中）");

            var filterRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0, UiTheme.SpaceS, 0, UiTheme.SpaceS)
            };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.Controls.Add(new Label
            {
                Text = "筛选",
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, 8, UiTheme.SpaceS, 0)
            }, 0, 0);
            _filter.Width = 260;
            _filter.Margin = new Padding(0, 6, UiTheme.SpaceS, 0);
            filterRow.Controls.Add(_filter, 1, 0);
            Button checkAll = SmallButton("全选");
            checkAll.Click += (sender, args) => SetVisibleChecked(true);
            Button uncheckAll = SmallButton("全不选");
            uncheckAll.Click += (sender, args) => SetVisibleChecked(false);
            Button invert = SmallButton("反选");
            invert.Click += (sender, args) => InvertVisible();
            var toggleRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0),
                WrapContents = false
            };
            toggleRow.Controls.Add(checkAll);
            toggleRow.Controls.Add(uncheckAll);
            toggleRow.Controls.Add(invert);
            filterRow.Controls.Add(toggleRow, 2, 0);
            Button expand = SmallButton("展开全部");
            expand.Click += (sender, args) => _tree.ExpandAll();
            Button collapse = SmallButton("折叠全部");
            collapse.Click += (sender, args) => _tree.CollapseAll();
            var expandRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Margin = new Padding(UiTheme.SpaceS, 4, 0, 0),
                WrapContents = false
            };
            expandRow.Controls.Add(expand);
            expandRow.Controls.Add(collapse);
            filterRow.Controls.Add(expandRow, 3, 0);

            _tree.Dock = DockStyle.Fill;
            _tree.CheckBoxes = true;
            _tree.HideSelection = false;
            _tree.ShowLines = true;
            _tree.ShowPlusMinus = true;
            _tree.ShowRootLines = true;
            _tree.ItemHeight = 24;
            _tree.AfterCheck += OnAfterCheck;
            _tree.BeforeExpand += (sender, args) =>
            {
                if (args.Node.Tag is string machineId)
                    _expanded.Add(machineId);
            };
            _tree.BeforeCollapse += (sender, args) =>
            {
                if (args.Node.Tag is string machineId)
                    _expanded.Remove(machineId);
            };

            _status.AutoSize = false;
            _status.Dock = DockStyle.Bottom;
            _status.Height = 28;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = UiTheme.TextSecondary;

            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            _ok = UiTheme.PrimaryButton("确定", DialogResult.OK);
            _ok.Click += (sender, args) => Confirm();
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Controls.Add(cancel);
            commands.Controls.Add(_ok);
            AcceptButton = cancel;
            CancelButton = cancel;

            Controls.Add(_tree);
            Controls.Add(_status);
            Controls.Add(commands);
            Controls.Add(filterRow);
            Controls.Add(header);

            _filter.TextChanged += (sender, args) => ApplyFilter(_filter.Text);
            RebuildTree();
            UpdateStatus();
        }

        /// <summary>确定后参与排版的回路（勾选且命中当前筛选）。</summary>
        public IReadOnlyList<XLayoutFrameItem> SelectedItems { get; private set; }
            = Array.Empty<XLayoutFrameItem>();

        /// <summary>当前筛选关键词（调试/测试用）。</summary>
        public IReadOnlyList<string> Terms => _terms;

        /// <summary>树控件（测试用）。</summary>
        internal TreeView Tree => _tree;

        /// <summary>可见（命中筛选）的回路数量。</summary>
        public int VisibleCircuitCount => CountVisibleItems();

        /// <summary>按文本重新筛选并重建树（保留勾选状态）。</summary>
        public void ApplyFilter(string text)
        {
            _terms = XLayoutSelectionFilter.SplitTerms(text);
            RebuildTree();
            UpdateStatus();
        }

        /// <summary>对当前可见的回路全选/全不选（测试与按钮共用）。</summary>
        public void SetVisibleChecked(bool value)
        {
            _updatingChecks = true;
            try
            {
                foreach (XLayoutFrameItem item in _items.Where(MatchesCurrentFilter))
                {
                    if (value) _checked.Add(item);
                    else _checked.Remove(item);
                }
                RebuildTree();
            }
            finally { _updatingChecks = false; }
            UpdateStatus();
        }

        /// <summary>对当前可见的回路反选。</summary>
        public void InvertVisible()
        {
            _updatingChecks = true;
            try
            {
                foreach (XLayoutFrameItem item in _items.Where(MatchesCurrentFilter))
                {
                    if (!_checked.Remove(item)) _checked.Add(item);
                }
                RebuildTree();
            }
            finally { _updatingChecks = false; }
            UpdateStatus();
        }

        /// <summary>计算"勾选且命中筛选"的回路（确定时与测试共用）。</summary>
        public IReadOnlyList<XLayoutFrameItem> ComputeSelectedItems()
            => _items.Where(item => _checked.Contains(item))
                .Where(MatchesCurrentFilter)
                .ToList();

        private bool MatchesCurrentFilter(XLayoutFrameItem item)
            => XLayoutSelectionFilter.MatchesCircuit(item.MachineId, item.DeviceName, _terms);

        private void Confirm()
        {
            IReadOnlyList<XLayoutFrameItem> selected = ComputeSelectedItems();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "至少选择一个回路才能排版。", "XLAYOUT",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            SelectedItems = selected;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void RebuildTree()
        {
            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                foreach (IGrouping<string, XLayoutFrameItem> machine in _items
                    .GroupBy(item => (item.MachineId ?? "").Trim(),
                        IdentityTextNormalizer.Comparer)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                {
                    bool machineMatches = XLayoutSelectionFilter.MatchesMachine(
                        machine.Key, _terms);
                    List<XLayoutFrameItem> circuits = _terms.Count == 0 || machineMatches
                        ? machine.ToList()
                        : machine.Where(MatchesCurrentFilter).ToList();
                    if (circuits.Count == 0) continue;

                    var machineNode = new TreeNode(
                        machine.Key + "（" + machine.Count() + " 个回路）")
                    {
                        Tag = machine.Key,
                        Checked = machine.All(item => _checked.Contains(item)),
                        ForeColor = UiTheme.TextPrimary
                    };
                    foreach (XLayoutFrameItem item in circuits)
                    {
                        var node = new TreeNode(string.IsNullOrWhiteSpace(item.DeviceName)
                            ? "（未命名回路）"
                            : item.DeviceName)
                        {
                            Tag = item,
                            Checked = _checked.Contains(item)
                        };
                        machineNode.Nodes.Add(node);
                    }
                    _tree.Nodes.Add(machineNode);
                    if (machineMatches) machineNode.Checked =
                        machine.All(item => _checked.Contains(item));
                    machineNode.Expand();
                }
            }
            finally { _tree.EndUpdate(); }
        }

        private int CountVisibleItems()
        {
            if (_terms.Count == 0) return _items.Count;
            return _items.Count(MatchesCurrentFilter);
        }

        private void UpdateStatus()
        {
            int selected = ComputeSelectedItems().Count;
            int visible = VisibleCircuitCount;
            string text = "共 " + _items.Count + " 个回路";
            if (_terms.Count > 0) text += "，筛选命中 " + visible + " 个";
            text += "；将重排 " + selected + " 个";
            if (_status.Text != text) _status.Text = text;
        }

        private void OnAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_updatingChecks) return;
            _updatingChecks = true;
            try
            {
                if (e.Node.Tag is string)
                {
                    foreach (TreeNode child in e.Node.Nodes)
                    {
                        child.Checked = e.Node.Checked;
                        if (child.Tag is XLayoutFrameItem item)
                        {
                            if (e.Node.Checked) _checked.Add(item);
                            else _checked.Remove(item);
                        }
                    }
                }
                else if (e.Node.Tag is XLayoutFrameItem item)
                {
                    if (e.Node.Checked) _checked.Add(item);
                    else _checked.Remove(item);
                    TreeNode parent = e.Node.Parent;
                    if (parent != null)
                        parent.Checked = parent.Nodes.Cast<TreeNode>()
                            .All(node => node.Checked);
                }
            }
            finally { _updatingChecks = false; }
            UpdateStatus();
        }

        private static Button SmallButton(string text)
        {
            Button button = UiTheme.Button(text);
            button.AutoSize = true;
            button.Margin = new Padding(0, 4, UiTheme.SpaceS, 0);
            return button;
        }
    }
}
