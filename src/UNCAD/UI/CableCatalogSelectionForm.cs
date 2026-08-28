using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>Lets the user explicitly choose a fixed-catalog cable replacement.</summary>
    public sealed class CableCatalogSelectionForm : Form
    {
        private readonly List<ListItem> _candidates;
        private readonly TextBox _search = new TextBox
        {
            Name = "CableCatalogSearch",
            Dock = DockStyle.Top
        };
        private readonly ListView _list = new ListView
        {
            Name = "CableCatalogList",
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false
        };
        private readonly Button _confirm;

        public CableCatalogSelectionForm(IEnumerable<ListItem> candidates,
            string missingModel)
        {
            _candidates = (candidates ?? Enumerable.Empty<ListItem>())
                .Where(item => item != null)
                .OrderBy(item => item.Code ?? "", StringComparer.Ordinal)
                .ToList();
            DialogLayout.Apply(this, "选择替代电缆型号 · " + Branding.Nameplate,
                new Size(820, 560), new Size(680, 460));

            var current = new Label
            {
                Text = "当前型号: " + (string.IsNullOrWhiteSpace(missingModel)
                    ? "未填写" : missingModel.Trim()),
                Dock = DockStyle.Top,
                Height = 34,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0)
            };
            var searchPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(8, 7, 8, 7)
            };
            searchPanel.Controls.Add(_search);
            _list.Columns.Add("项目编码", 90);
            _list.Columns.Add("规格", 180);
            _list.Columns.Add("名称", 180);
            _list.Columns.Add("特征", 320);

            _confirm = DialogLayout.CommandButton("使用所选型号", DialogResult.None);
            Button cancel = DialogLayout.CommandButton("保持原型号", DialogResult.Cancel);
            _confirm.Click += Confirm;
            FlowLayoutPanel commands = DialogLayout.CommandBar();
            commands.Controls.Add(cancel);
            commands.Controls.Add(_confirm);

            Controls.Add(_list);
            Controls.Add(searchPanel);
            Controls.Add(current);
            Controls.Add(commands);
            AcceptButton = _confirm;
            CancelButton = cancel;

            _search.TextChanged += (sender, args) => Populate(_search.Text);
            _list.SelectedIndexChanged += (sender, args) =>
                _confirm.Enabled = _list.SelectedItems.Count == 1;
            _list.DoubleClick += Confirm;
            Populate("");
        }

        public ListItem SelectedItem => _list.SelectedItems.Count == 1
            ? _list.SelectedItems[0].Tag as ListItem : null;

        private void Populate(string query)
        {
            string keyword = (query ?? "").Trim();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (ListItem candidate in _candidates.Where(item =>
                    Matches(item, keyword)))
                {
                    var row = new ListViewItem(candidate.Code ?? "") { Tag = candidate };
                    row.SubItems.Add(candidate.Spec ?? "");
                    row.SubItems.Add(candidate.Name ?? "");
                    row.SubItems.Add(candidate.Feature ?? "");
                    _list.Items.Add(row);
                }
                if (_list.Items.Count > 0) _list.Items[0].Selected = true;
            }
            finally { _list.EndUpdate(); }
            _confirm.Enabled = _list.SelectedItems.Count == 1;
        }

        private static bool Matches(ListItem item, string keyword)
        {
            if (keyword.Length == 0) return true;
            return Contains(item.Code, keyword) || Contains(item.Spec, keyword)
                || Contains(item.Name, keyword) || Contains(item.Feature, keyword);
        }

        private static bool Contains(string value, string keyword)
            => (value ?? "").IndexOf(keyword,
                StringComparison.OrdinalIgnoreCase) >= 0;

        private void Confirm(object sender, EventArgs e)
        {
            if (SelectedItem == null) return;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
