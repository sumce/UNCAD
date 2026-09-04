using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Fill;

namespace UNCAD.UI
{
    /// <summary>One frame's comparison payload for FillUpdateCompareForm.</summary>
    public sealed class FillUpdateCompareItem
    {
        public string MachineId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string FrameHandle { get; set; } = "";
        public string LastUpdatedText { get; set; } = "";
        public string LastUpdatedUser { get; set; } = "";
        public bool HasHistory { get; set; }
        public int AddedCount { get; set; }
        public int RemovedCount { get; set; }
        public int QuantityChangedCount { get; set; }
        public List<FillRowDiff> RowDiffs { get; set; } = new List<FillRowDiff>();
    }

    /// <summary>
    /// U1U 更新对比窗体:展示每个待更新图框的上次更新时间/用户,以及
    /// 现有清单与本次将写入清单的逐行对比(新增/移除/改量/保留)。
    /// 多框时左侧列表选择、右侧详情,单框时直接显示详情。
    /// </summary>
    public sealed class FillUpdateCompareForm : Form
    {
        private readonly ListBox _itemList;
        private readonly Label _title;
        private readonly Label _history;
        private readonly Label _summary;
        private readonly ListView _diffs;
        private readonly Button _ok;
        private readonly List<FillUpdateCompareItem> _items;

        public FillUpdateCompareForm(List<FillUpdateCompareItem> items)
        {
            _items = items ?? new List<FillUpdateCompareItem>();
            bool multiple = _items.Count > 1;
            DialogLayout.Apply(this, "U1U · 清单更新对比",
                new Size(multiple ? 1040 : 820, 640), new Size(760, 480));

            // 左侧:图框列表(仅多框)
            var listPanel = new Panel { Dock = DockStyle.Left, Width = 260 };
            var listHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = "待更新图框(" + _items.Count + " 个)",
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextPrimary,
                Padding = new Padding(8, 6, 4, 0)
            };
            _itemList = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.FontBody,
                IntegralHeight = false
            };
            listPanel.Controls.Add(_itemList);
            listPanel.Controls.Add(listHeader);

            // 右侧:详情
            _title = new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.Accent,
                Padding = new Padding(8, 6, 4, 0)
            };
            _history = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Font = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(8, 2, 4, 0)
            };
            _summary = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                Font = UiTheme.FontCaption,
                ForeColor = UiTheme.TextPrimary,
                Padding = new Padding(8, 2, 4, 0)
            };

            _diffs = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.FixedSingle,
                MultiSelect = false,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.FontBody
            };
            // 列与清单表格一致:序号/项目名称/特征描述/单位/数量/项次编码,
            // 另加"上次数量"列做对比;状态用行颜色表达。
            _diffs.Columns.Add("序号", 44);
            _diffs.Columns.Add("项目名称", 150);
            _diffs.Columns.Add("特征描述", 260);
            _diffs.Columns.Add("单位", 44);
            _diffs.Columns.Add("上次数量", 86);
            _diffs.Columns.Add("本次数量", 86);
            _diffs.Columns.Add("项次编码", 70);

            var detail = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            detail.Controls.Add(_diffs);
            detail.Controls.Add(_summary);
            detail.Controls.Add(_history);
            detail.Controls.Add(_title);

            _ok = UiTheme.PrimaryButton("继续更新");
            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            FlowLayoutPanel btnRow = UiTheme.CommandBar();
            btnRow.Controls.Add(cancel);
            btnRow.Controls.Add(_ok);

            if (multiple) Controls.Add(listPanel);
            Controls.Add(detail);
            Controls.Add(btnRow);
            AcceptButton = _ok;
            CancelButton = cancel;

            foreach (FillUpdateCompareItem item in _items)
            {
                int changed = item.AddedCount + item.RemovedCount
                    + item.QuantityChangedCount;
                _itemList.Items.Add(item.MachineId + " · " + item.DeviceName
                    + (changed > 0 ? "  ·  " + changed + " 项变化" : ""));
            }
            if (_items.Count > 0) _itemList.SelectedIndex = 0;
            _itemList.SelectedIndexChanged += (s, e) => ShowSelected();
            Resize += (s, e) => ResizeColumns();
            ShowSelected();
        }

        private void ShowSelected()
        {
            _diffs.BeginUpdate();
            _diffs.Items.Clear();
            FillUpdateCompareItem item = Selected;
            if (item == null)
            {
                _diffs.EndUpdate();
                return;
            }
            _title.Text = item.MachineId + " · " + item.DeviceName
                + (item.FrameHandle.Length > 0 ? "  （图框 " + item.FrameHandle + "）" : "");
            _history.Text = item.HasHistory
                ? "上次更新时间: " + (string.IsNullOrWhiteSpace(item.LastUpdatedText)
                        ? "未知" : item.LastUpdatedText)
                    + "    上次更新用户: " + (string.IsNullOrWhiteSpace(item.LastUpdatedUser)
                        ? "未知" : item.LastUpdatedUser)
                : "首次更新 · 无历史记录";
            _summary.Text = item.RowDiffs.Count == 0
                ? "现有表格没有可读的清单行。"
                : "共 " + item.RowDiffs.Count + " 行:新增 " + item.AddedCount
                    + " / 移除 " + item.RemovedCount + " / 改量 "
                    + item.QuantityChangedCount + " / 保留 "
                    + (item.RowDiffs.Count - item.AddedCount - item.RemovedCount
                        - item.QuantityChangedCount);
            foreach (FillRowDiff diff in item.RowDiffs)
            {
                string ordinal = diff.Order > 0
                    ? diff.Order.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "—";
                var row = new ListViewItem(ordinal)
                {
                    UseItemStyleForSubItems = false,
                    ForeColor = RowColor(diff)
                };
                row.SubItems.Add(string.IsNullOrWhiteSpace(diff.Name) ? "—" : diff.Name);
                row.SubItems.Add(string.IsNullOrWhiteSpace(diff.Description)
                    ? "—" : diff.Description);
                row.SubItems.Add(string.IsNullOrWhiteSpace(diff.Unit) ? "—" : diff.Unit);
                row.SubItems.Add(string.IsNullOrWhiteSpace(diff.OldQuantity)
                    ? "—" : diff.OldQuantity);
                row.SubItems.Add(string.IsNullOrWhiteSpace(diff.NewQuantity)
                    ? "—" : diff.NewQuantity);
                row.SubItems.Add(string.IsNullOrWhiteSpace(diff.Code) ? "—" : diff.Code);
                _diffs.Items.Add(row);
            }
            _diffs.EndUpdate();
            ResizeColumns();
        }

        private static Color RowColor(FillRowDiff diff)
        {
            if (diff.Status == FillRowDiff.StatusAdded) return UiTheme.SuccessFg;
            if (diff.Status == FillRowDiff.StatusRemoved) return UiTheme.DangerFg;
            if (diff.Status == FillRowDiff.StatusQuantity) return UiTheme.WarningFg;
            return UiTheme.TextSecondary;
        }

        private void ResizeColumns()
        {
            int width = Math.Max(620, _diffs.ClientSize.Width - 8);
            int[] fixedWidths = { 44, 0, 0, 44, 86, 86, 70 };
            int fixedTotal = fixedWidths.Sum();
            int flexible = Math.Max(140, (width - fixedTotal) / 2);
            _diffs.Columns[0].Width = fixedWidths[0];
            _diffs.Columns[1].Width = flexible;
            _diffs.Columns[2].Width = flexible;
            for (int i = 3; i < fixedWidths.Length; i++)
                _diffs.Columns[i].Width = fixedWidths[i];
        }

        private FillUpdateCompareItem Selected
            => _itemList.SelectedIndex >= 0 && _itemList.SelectedIndex < _items.Count
                ? _items[_itemList.SelectedIndex] : null;
    }
}
