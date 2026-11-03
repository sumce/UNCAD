using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using UNCAD.Core.Excel;
using UNCAD.Core.Text;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 从固定清单选择要添加/替换的数据库项目（3.0 WPF 版，D-038，P2 工作台模式）。
    /// 材料身份只读，仅数量属于本次评审。交互按用户习惯设计：分词搜索、
    /// 类别筛选、最近使用置顶、双击连续添加、表头排序、悬停显示完整特征。
    /// </summary>
    public sealed partial class ManualListItemWindow : FluentWindow
    {
        private const int MaxRecentItems = 10;

        /// <summary>连续添加模式：双击或“添加并继续”时触发，对话框保持打开。</summary>
        public event Action<ListItem, string> PickRequested;

        private readonly List<CatalogRow> _catalog;
        private readonly bool _replacement;
        private readonly List<string> _recentCodes;
        private readonly ObservableCollection<CatalogRow> _visible =
            new ObservableCollection<CatalogRow>();
        private int _sortColumn = 1; // 默认按项目编码列排序
        private bool _sortAscending = true;

        /// <summary>用户点了确认类按钮为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        public ListItem SelectedItem => CatalogGrid.SelectedItem is CatalogRow row
            ? row.Item : null;

        public string Quantity => TextFormatter.FormatNum(ClampedQuantity());

        /// <summary>当前类别胶囊，供未匹配项跳转和 UI 回归测试核对。</summary>
        public string SelectedCategory =>
            Convert.ToString(CategoryList.SelectedItem) ?? "全部";

        internal IReadOnlyList<string> CategoryNames => _categoryNames;
        private readonly List<string> _categoryNames = new List<string>();

        public ManualListItemWindow(IEnumerable<ListItem> catalog, bool replacement = false,
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
            _catalog = selectable
                .OrderBy(item => item.Code ?? "", StringComparer.Ordinal)
                .Select(item => new CatalogRow(item)).ToList();
            _recentCodes = LoadRecentCodes();

            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;

            string caption = replacement ? "替换固定清单项目" : "从固定清单添加";
            Title = caption + " · " + Branding.Nameplate;
            HeaderBar.Title = Title;
            HeaderTextBlock.Text = caption;
            ConfirmButton.Content = replacement ? "使用所选清单" : "添加所选清单";
            ContinueButton.Visibility = replacement ? Visibility.Collapsed : Visibility.Visible;
            QuantityBar.Visibility = replacement ? Visibility.Collapsed : Visibility.Visible;

            CatalogGrid.ItemsSource = _visible;

            _categoryNames.Add("全部");
            _categoryNames.Add("最近使用");
            foreach (string category in _catalog.Select(row => row.Category)
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
                _categoryNames.Add(category);
            // 先挂 ItemsSource 再选类别：选类别会立即触发一次 Populate。
            CategoryList.ItemsSource = _categoryNames;
            SelectCategoryTab(initialCategory);
            Populate();
            Loaded += (sender, args) => SearchBox.Focus();
        }

        /// <summary>当前数量输入（0~1000000，保留 3 位小数语义与旧 NumericUpDown 一致）。</summary>
        internal double ClampedQuantity()
        {
            double value = QuantityBox.Value ?? 0d;
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0d;
            return Math.Max(0d, Math.Min(1000000d, Math.Round(value, 3)));
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => Populate();

        private void OnCategoryChanged(object sender, SelectionChangedEventArgs e) => Populate();

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateConfirmState();

        private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
            => PickCurrent(keepOpen: !_replacement);

        private void OnConfirm(object sender, RoutedEventArgs e)
            => PickCurrent(keepOpen: false);

        private void OnContinueAdd(object sender, RoutedEventArgs e)
            => PickCurrent(keepOpen: true);

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        /// <summary>表头排序：同列切换升/降序，新列从升序起（自然排序，数字段按数值比较）。</summary>
        private void OnColumnHeaderSort(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true; // 取消默认字符串排序，改用与旧窗体一致的自然排序。
            string path = e.Column.SortMemberPath ?? "";
            int column = ColumnIndexOf(path);
            if (column < 0) return;
            if (_sortColumn == column) _sortAscending = !_sortAscending;
            else { _sortColumn = column; _sortAscending = true; }
            e.Column.SortDirection = _sortAscending
                ? ListSortDirection.Ascending : ListSortDirection.Descending;
            ApplySort(column, _sortAscending);
        }

        internal void ApplySort(int column, bool ascending)
        {
            List<CatalogRow> sorted = _visible.ToList();
            sorted.Sort((a, b) =>
                NaturalCompare(ColumnText(a, column), ColumnText(b, column))
                    * (ascending ? 1 : -1));
            _visible.Clear();
            foreach (CatalogRow row in sorted) _visible.Add(row);
        }

        private static int ColumnIndexOf(string path)
        {
            switch (path)
            {
                case "Category": return 0;
                case "Code": return 1;
                case "Name": return 2;
                case "Alias": return 3;
                case "Alias1": return 4;
                case "Unit": return 5;
                case "Feature": return 6;
                default: return -1;
            }
        }

        private static string ColumnText(CatalogRow row, int column)
        {
            switch (column)
            {
                case 0: return row.Category;
                case 1: return row.Code;
                case 2: return row.Name;
                case 3: return row.Alias;
                case 4: return row.Alias1;
                case 5: return row.Unit;
                default: return row.Feature;
            }
        }

        private void PickCurrent(bool keepOpen)
        {
            ListItem item = SelectedItem;
            if (item == null) return;
            RememberRecent(item);
            if (keepOpen)
            {
                PickRequested?.Invoke(item, Quantity);
                CatalogGrid.Focus();
            }
            else
            {
                Confirmed = true;
                Close();
            }
        }

        private void Populate()
        {
            string query = (SearchBox.Text ?? "").Trim();
            string category = SelectedCategory;
            List<CatalogRow> visible = _catalog.Where(candidate =>
                CategoryMatches(candidate.Item, category)
                && Matches(candidate.Item, query)).ToList();
            IEnumerable<CatalogRow> ordered = category == "最近使用"
                ? visible.OrderBy(row => RecentRank(row.Code))
                : visible.OrderBy(row => RecentRank(row.Code) < 0 ? 1 : 0)
                    .ThenBy(row => RecentRank(row.Code))
                    .ThenBy(row => row.Code ?? "", StringComparer.Ordinal);
            _visible.Clear();
            foreach (CatalogRow row in ordered) _visible.Add(row);
            if (_visible.Count > 0) CatalogGrid.SelectedIndex = 0;
            ResultCountText.Text = "共 " + _visible.Count + " 项";
            UpdateConfirmState();
        }

        private void SelectCategoryTab(string requestedCategory)
        {
            string requested = (requestedCategory ?? "").Trim();
            string selected = _categoryNames.FirstOrDefault(category =>
                string.Equals(category, requested, StringComparison.Ordinal))
                ?? _categoryNames[0];
            CategoryList.SelectedItem = selected;
        }

        internal bool CategoryMatches(ListItem item, string category)
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

        private static bool IsSelectable(ListItem item)
            => item != null && !string.IsNullOrWhiteSpace(item.Code)
                && !string.IsNullOrWhiteSpace(item.Name);

        internal static string CategoryOf(ListItem item)
            => string.IsNullOrWhiteSpace(item?.Category) ? "未分类" : item.Category.Trim();

        /// <summary>分词匹配：空格分隔的每个关键词都必须在任一可搜索列中出现。</summary>
        internal static bool Matches(ListItem item, string query)
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

        /// <summary>自然排序：数字段按数值比较，避免 "10" 排在 "2" 前面。</summary>
        internal static int NaturalCompare(string x, string y)
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

        private void UpdateConfirmState()
            => ConfirmButton.IsEnabled = SelectedItem != null;

        /// <summary>清单行显示模型：列文本 + 悬停提示。</summary>
        public sealed class CatalogRow
        {
            public CatalogRow(ListItem item)
            {
                Item = item;
                Category = CategoryOf(item);
                Code = item.Code ?? "";
                Name = item.Name ?? "";
                Alias = item.Alias ?? "";
                Alias1 = item.Alias1 ?? "";
                Unit = item.Unit ?? "";
                Feature = item.Feature ?? "";
                Tooltip = BuildTooltip(item);
            }

            public ListItem Item { get; }
            public string Category { get; }
            public string Code { get; }
            public string Name { get; }
            public string Alias { get; }
            public string Alias1 { get; }
            public string Unit { get; }
            public string Feature { get; }
            public string Tooltip { get; }

            private static string BuildTooltip(ListItem item)
            {
                string text = (item.Category ?? "") + " · " + (item.Code ?? "")
                    + " " + (item.Name ?? "");
                if (!string.IsNullOrWhiteSpace(item.Alias))
                    text += "\n别名: " + item.Alias.Trim();
                if (!string.IsNullOrWhiteSpace(item.Feature))
                    text += "\n" + item.Feature.Trim();
                return text;
            }
        }
    }
}
