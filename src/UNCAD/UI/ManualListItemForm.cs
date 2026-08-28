using System;
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
    /// Adds one user-selected database item. Material identity is read-only and always comes
    /// from the fixed catalog; only the requested quantity belongs to this review session.
    /// </summary>
    public sealed class ManualListItemForm : Form
    {
        private readonly List<ListItem> _catalog;
        private readonly TextBox _search = new TextBox
        {
            Name = "CatalogSearch",
            Dock = DockStyle.Fill
        };
        private readonly ComboBox _category = new ComboBox
        {
            Name = "CatalogCategory",
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        private readonly ListView _list = new ListView
        {
            Name = "CatalogItems",
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false
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
        private readonly Button _confirm;

        public ManualListItemForm(IEnumerable<ListItem> catalog, bool replacement = false)
        {
            _catalog = (catalog ?? Enumerable.Empty<ListItem>())
                .Where(IsSelectable)
                .OrderBy(item => item.Code ?? "", StringComparer.Ordinal)
                .ToList();
            DialogLayout.Apply(this, (replacement ? "替换固定清单项目 · "
                : "从固定清单添加 · ") + Branding.Nameplate,
                new Size(980, 650), new Size(760, 520));

            var filters = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
                ColumnCount = 4,
                Padding = new Padding(8, 7, 8, 5)
            };
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            filters.Controls.Add(LabelFor("搜索:"), 0, 0);
            filters.Controls.Add(_search, 1, 0);
            filters.Controls.Add(LabelFor("类别:"), 2, 0);
            filters.Controls.Add(_category, 3, 0);

            _list.Columns.Add("类别", 90);
            _list.Columns.Add("项目编码", 90);
            _list.Columns.Add("项目名称", 180);
            _list.Columns.Add("别名", 130);
            _list.Columns.Add("别名1", 110);
            _list.Columns.Add("单位", 60);
            _list.Columns.Add("项目特征", 390);

            _category.Items.Add("全部");
            foreach (string category in _catalog.Select(CategoryOf)
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
                _category.Items.Add(category);
            _category.SelectedIndex = 0;

            var quantityBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(10, 8, 10, 6),
                WrapContents = false
            };
            quantityBar.Controls.Add(new Label
            {
                Text = "数量:",
                Width = 60,
                Height = 28,
                TextAlign = ContentAlignment.MiddleRight
            });
            quantityBar.Controls.Add(_quantity);

            quantityBar.Visible = !replacement;
            _confirm = DialogLayout.CommandButton(replacement
                ? "使用所选清单" : "添加所选清单", DialogResult.None);
            Button cancel = DialogLayout.CommandButton("取消", DialogResult.Cancel);
            _confirm.Click += Confirm;
            FlowLayoutPanel commands = DialogLayout.CommandBar();
            commands.Controls.Add(cancel);
            commands.Controls.Add(_confirm);

            Controls.Add(_list);
            Controls.Add(filters);
            Controls.Add(quantityBar);
            Controls.Add(commands);
            AcceptButton = _confirm;
            CancelButton = cancel;

            _search.TextChanged += (sender, args) => Populate();
            _category.SelectedIndexChanged += (sender, args) => Populate();
            _list.SelectedIndexChanged += (sender, args) => UpdateConfirmState();
            _list.DoubleClick += Confirm;
            Populate();
        }

        public ListItem SelectedItem => _list.SelectedItems.Count == 1
            ? _list.SelectedItems[0].Tag as ListItem : null;

        public string Quantity => TextFormatter.FormatNum((double)_quantity.Value);

        private void Populate()
        {
            string query = (_search.Text ?? "").Trim();
            string category = Convert.ToString(_category.SelectedItem) ?? "全部";
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (ListItem item in _catalog.Where(candidate =>
                    (category == "全部" || CategoryOf(candidate) == category)
                    && Matches(candidate, query)))
                {
                    var row = new ListViewItem(CategoryOf(item)) { Tag = item };
                    row.SubItems.Add(item.Code ?? "");
                    row.SubItems.Add(item.Name ?? "");
                    row.SubItems.Add(item.Alias ?? "");
                    row.SubItems.Add(item.Alias1 ?? "");
                    row.SubItems.Add(item.Unit ?? "");
                    row.SubItems.Add(item.Feature ?? "");
                    _list.Items.Add(row);
                }
                if (_list.Items.Count > 0) _list.Items[0].Selected = true;
            }
            finally { _list.EndUpdate(); }
            UpdateConfirmState();
        }

        private static bool IsSelectable(ListItem item)
            => item != null && !string.IsNullOrWhiteSpace(item.Code)
                && !string.IsNullOrWhiteSpace(item.Name);

        private static string CategoryOf(ListItem item)
            => string.IsNullOrWhiteSpace(item?.Category) ? "未分类" : item.Category.Trim();

        private static bool Matches(ListItem item, string query)
        {
            if (query.Length == 0) return true;
            return Contains(item.Category, query) || Contains(item.Code, query)
                || Contains(item.Name, query) || Contains(item.Feature, query)
                || Contains(item.Unit, query) || Contains(item.Alias, query)
                || Contains(item.Alias1, query);
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

        private void Confirm(object sender, EventArgs e)
        {
            if (SelectedItem == null) return;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
