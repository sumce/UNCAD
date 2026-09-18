using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 从固定清单选择要添加/替换的数据库项目。材料身份只读，仅数量属于本次评审。
    /// 交互按用户习惯设计：分词搜索、类别筛选、最近使用置顶、双击连续添加、
    /// 表头排序、悬停显示完整特征。
    /// </summary>
    public sealed class ManualListItemForm : DpiAwareForm
    {
        private const int MaxRecentItems = 10;

        private readonly List<ListItem> _catalog;
        private readonly bool _replacement;
        private readonly TextBox _search = new TextBox
        {
            Name = "CatalogSearch",
            Dock = DockStyle.Fill
        };
        private readonly TabControl _categoryTabs = new TabControl
        {
            Name = "CatalogTabs",
            Dock = DockStyle.Fill,
            Multiline = false,
            Padding = new Point(14, 4),
            ShowToolTips = true
        };
        private readonly ListView _list = new ListView
        {
            Name = "CatalogItems",
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            ShowItemToolTips = true
        };
        private readonly NumericUpDown _quantity = new NumericUpDown
        {
            Name = "CatalogQuantity",
            Width = 160,
            Minimum = 0,
            Maximum = 1000000,
            DecimalPlaces = 3,
            Increment = 1,
            Value = 1,
            ThousandsSeparator = true
        };
        private readonly Label _resultCount = new Label
        {
            Text = "",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.TextSecondary
        };
        private readonly Button _confirm;
        private readonly Button _continueAdd;
        private readonly List<string> _recentCodes;
        private int _sortColumn = 1; // 默认按项目编码列排序
        private bool _sortAscending = true;

        /// <summary>连续添加模式：双击或“添加并继续”时触发，对话框保持打开。</summary>
        public event Action<ListItem, string> PickRequested;

        public ManualListItemForm(IEnumerable<ListItem> catalog, bool replacement = false,
            string initialCategory = null)
        {
            _replacement = replacement;
            List<ListItem> selectable = (catalog ?? Enumerable.Empty<ListItem>())
                .Where(IsSelectable).ToList();
            if (replacement && !string.IsNullOrWhiteSpace(initialCategory)
                && !string.Equals(initialCategory, "手动添加", StringComparison.Ordinal))
            {
                // 替换必须保持原清单类别，防止把桥架身份写入电缆行等跨类错误。
                selectable = selectable.Where(item => string.Equals(CategoryOf(item),
                    initialCategory.Trim(), StringComparison.Ordinal)).ToList();
            }
            _catalog = selectable.OrderBy(item => item.Code ?? "", StringComparer.Ordinal)
                .ToList();
            _recentCodes = LoadRecentCodes();

            DialogLayout.Apply(this, (replacement ? "替换固定清单项目 · "
                : "从固定清单添加 · ") + Branding.Nameplate,
                new Size(980, 660), new Size(760, 520));

            var filters = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(
                    8, 7,
                    8, 5)
            };
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filters.Controls.Add(LabelFor("搜索:"), 0, 0);
            filters.Controls.Add(_search, 1, 0);

            _list.Columns.Add("类别", UiTheme.NotAutoScaled(90));
            _list.Columns.Add("项目编码", UiTheme.NotAutoScaled(90));
            _list.Columns.Add("项目名称", UiTheme.NotAutoScaled(190));
            _list.Columns.Add("别名", UiTheme.NotAutoScaled(130));
            _list.Columns.Add("别名1", UiTheme.NotAutoScaled(110));
            _list.Columns.Add("单位", UiTheme.NotAutoScaled(60));
            _list.Columns.Add("项目特征", UiTheme.NotAutoScaled(300));
            _list.ColumnClick += OnColumnClick;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = UiTheme.Surface;
            _list.ForeColor = UiTheme.TextPrimary;

            // 类别直接做成顶部标签；标签值只来自“类”列，不根据名称或编码推断。
            AddCategoryTab("全部");
            AddCategoryTab("最近使用");
            foreach (string category in _catalog.Select(CategoryOf)
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
                AddCategoryTab(category);
            SelectCategoryTab(initialCategory);

            var quantityBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Height = 44,
                ColumnCount = 4,
                Padding = new Padding(
                    10, 6,
                    10, 4)
            };
            quantityBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            quantityBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            quantityBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            quantityBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            quantityBar.Controls.Add(new Label
            {
                Text = "数量:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill
            }, 0, 0);
            quantityBar.Controls.Add(_quantity, 1, 0);
            quantityBar.Controls.Add(new Label
            {
                Text = "双击项目 = 添加并继续；回车 = 添加后关闭",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary
            }, 2, 0);
            quantityBar.Controls.Add(_resultCount, 3, 0);
            quantityBar.Visible = !replacement;

            _confirm = UiTheme.PrimaryButton(replacement
                ? "使用所选清单" : "添加所选清单");
            _continueAdd = UiTheme.Button("添加并继续");
            _continueAdd.Visible = !replacement;
            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            _confirm.Click += (sender, args) => PickCurrent(keepOpen: false);
            _continueAdd.Click += (sender, args) => PickCurrent(keepOpen: true);
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Controls.Add(cancel);
            commands.Controls.Add(_continueAdd);
            commands.Controls.Add(_confirm);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute,
                replacement ? 0 : 44));
            content.Controls.Add(_categoryTabs, 0, 0);
            content.Controls.Add(filters, 0, 1);
            content.Controls.Add(_list, 0, 2);
            content.Controls.Add(quantityBar, 0, 3);

            Controls.Add(content);
            Controls.Add(commands);
            AcceptButton = _confirm;
            CancelButton = cancel;

            UiTheme.StyleInput(_search);
            _search.TextChanged += (sender, args) => Populate();
            _categoryTabs.SelectedIndexChanged += (sender, args) => Populate();
            _list.SelectedIndexChanged += (sender, args) => UpdateConfirmState();
            _list.DoubleClick += (sender, args) =>
                PickCurrent(keepOpen: !_replacement);
            Populate();
        }

        public ListItem SelectedItem => _list.SelectedItems.Count == 1
            ? _list.SelectedItems[0].Tag as ListItem : null;

        public string Quantity => TextFormatter.FormatNum((double)_quantity.Value);

        /// <summary>当前顶部类别标签，供未匹配项跳转和 UI 回归测试核对。</summary>
        public string SelectedCategory => Convert.ToString(_categoryTabs.SelectedTab?.Tag) ?? "全部";

        private void PickCurrent(bool keepOpen)
        {
            ListItem item = SelectedItem;
            if (item == null) return;
            RememberRecent(item);
            if (keepOpen)
            {
                PickRequested?.Invoke(item, Quantity);
                _list.Focus();
            }
            else
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void Populate()
        {
            string query = (_search.Text ?? "").Trim();
            string category = SelectedCategory;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                List<ListItem> visible = _catalog.Where(candidate =>
                    CategoryMatches(candidate, category) && Matches(candidate, query)).ToList();
                IEnumerable<ListItem> ordered = category == "最近使用"
                    ? visible.OrderBy(item => RecentRank(item.Code))
                    : visible.OrderBy(item => RecentRank(item.Code) < 0 ? 1 : 0)
                        .ThenBy(item => RecentRank(item.Code))
                        .ThenBy(item => item.Code ?? "", StringComparer.Ordinal);
                foreach (ListItem item in ordered)
                {
                    var row = new ListViewItem(CategoryOf(item)) { Tag = item };
                    row.SubItems.Add(item.Code ?? "");
                    row.SubItems.Add(item.Name ?? "");
                    row.SubItems.Add(item.Alias ?? "");
                    row.SubItems.Add(item.Alias1 ?? "");
                    row.SubItems.Add(item.Unit ?? "");
                    row.SubItems.Add(item.Feature ?? "");
                    row.ToolTipText = BuildTooltip(item);
                    _list.Items.Add(row);
                }
                if (_list.Items.Count > 0) _list.Items[0].Selected = true;
                _resultCount.Text = "共 " + _list.Items.Count + " 项";
            }
            finally { _list.EndUpdate(); }
            UpdateConfirmState();
        }

        private void AddCategoryTab(string category)
        {
            var page = new TabPage(category)
            {
                Tag = category,
                ToolTipText = category == "未分类"
                    ? "这些项目未设置类，仅供人工选择，不参与自动匹配。"
                    : category + "清单"
            };
            _categoryTabs.TabPages.Add(page);
        }

        private void SelectCategoryTab(string requestedCategory)
        {
            string requested = (requestedCategory ?? "").Trim();
            TabPage selected = _categoryTabs.TabPages.Cast<TabPage>().FirstOrDefault(page =>
                string.Equals(Convert.ToString(page.Tag), requested, StringComparison.Ordinal));
            _categoryTabs.SelectedTab = selected ?? _categoryTabs.TabPages[0];
        }

        private bool CategoryMatches(ListItem item, string category)
        {
            if (category == "全部") return true;
            if (category == "最近使用") return RecentRank(item.Code) >= 0;
            return CategoryOf(item) == category;
        }

        private int RecentRank(string code)
        {
            int index = _recentCodes.IndexOf(code ?? "");
            return index < 0 ? -1 : index;
        }

        private void RememberRecent(ListItem item)
        {
            string code = item.Code ?? "";
            _recentCodes.Remove(code);
            _recentCodes.Insert(0, code);
            if (_recentCodes.Count > MaxRecentItems)
                _recentCodes.RemoveAt(_recentCodes.Count - 1);
            try
            {
                Settings.Set(ConfigKeys.FillRecentCatalogItems,
                    string.Join(",", _recentCodes));
            }
            catch (Exception ex)
            {
                // 最近使用只是 UI 便利功能，保存失败不能阻断本次清单选择。
                Log.Warn("清单最近使用记录保存失败: " + ex.Message);
            }
        }

        private static List<string> LoadRecentCodes()
        {
            string raw = Settings.Get(ConfigKeys.FillRecentCatalogItems, "");
            return (raw ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(code => code.Trim()).Where(code => code.Length > 0)
                .Distinct(StringComparer.Ordinal).Take(MaxRecentItems).ToList();
        }

        private static string BuildTooltip(ListItem item)
        {
            string text = (item.Category ?? "") + " · " + (item.Code ?? "")
                + " " + (item.Name ?? "");
            if (!string.IsNullOrWhiteSpace(item.Alias))
                text += "\\n别名: " + item.Alias.Trim();
            if (!string.IsNullOrWhiteSpace(item.Feature))
                text += "\\n" + item.Feature.Trim();
            return text;
        }

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (_sortColumn == e.Column) _sortAscending = !_sortAscending;
            else { _sortColumn = e.Column; _sortAscending = true; }
            _list.ListViewItemSorter = new ItemComparer(e.Column, _sortAscending);
            _list.Sort();
        }

        private sealed class ItemComparer : IComparer
        {
            private readonly int _column;
            private readonly int _sign;

            public ItemComparer(int column, bool ascending)
            {
                _column = column;
                _sign = ascending ? 1 : -1;
            }

            public int Compare(object x, object y)
            {
                var a = (ListViewItem)x;
                var b = (ListViewItem)y;
                string xs = a.SubItems.Count > _column ? a.SubItems[_column].Text ?? "" : "";
                string ys = b.SubItems.Count > _column ? b.SubItems[_column].Text ?? "" : "";
                // 自然排序:数字段按数值比较,避免 "10" 排在 "2" 前面。
                return NaturalCompare(xs, ys) * _sign;
            }

            private static int NaturalCompare(string x, string y)
            {
                int ix = 0, iy = 0;
                while (ix < x.Length && iy < y.Length)
                {
                    char cx = x[ix], cy = y[iy];
                    if (char.IsDigit(cx) && char.IsDigit(cy))
                    {
                        long nx = ReadNumber(x, ref ix);
                        long ny = ReadNumber(y, ref iy);
                        if (nx != ny) return nx < ny ? -1 : 1;
                    }
                    else
                    {
                        int compared = char.ToUpperInvariant(cx)
                            .CompareTo(char.ToUpperInvariant(cy));
                        if (compared != 0) return compared;
                        ix++;
                        iy++;
                    }
                }
                return (x.Length - ix).CompareTo(y.Length - iy);
            }

            private static long ReadNumber(string text, ref int index)
            {
                long value = 0;
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    value = value * 10 + (text[index] - '0');
                    index++;
                }
                return value;
            }
        }

        private static bool IsSelectable(ListItem item)
            => item != null && !string.IsNullOrWhiteSpace(item.Code)
                && !string.IsNullOrWhiteSpace(item.Name);

        private static string CategoryOf(ListItem item)
            => string.IsNullOrWhiteSpace(item?.Category) ? "未分类" : item.Category.Trim();

        /// <summary>分词匹配：空格分隔的每个关键词都必须在任一可搜索列中出现。</summary>
        private static bool Matches(ListItem item, string query)
        {
            string[] tokens = (query ?? "").Split(
                new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return true;
            foreach (string token in tokens)
            {
                if (!Contains(item.Category, token) && !Contains(item.Code, token)
                    && !Contains(item.Name, token) && !Contains(item.Feature, token)
                    && !Contains(item.Unit, token) && !Contains(item.Alias, token)
                    && !Contains(item.Alias1, token))
                    return false;
            }
            return true;
        }

        private static bool Contains(string value, string query)
            => (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private static Label LabelFor(string text)
            => new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            };

        private void UpdateConfirmState()
            => _confirm.Enabled = SelectedItem != null;
    }
}
