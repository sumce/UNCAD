using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
        /// <summary>左侧:上次清单(表格现有行,移除行标红)。</summary>
        public List<FillRowDiff> PreviousRows { get; set; } = new List<FillRowDiff>();
        /// <summary>右侧:本次将写入的清单(新增/改量着色)。</summary>
        public List<FillRowDiff> PlannedRows { get; set; } = new List<FillRowDiff>();
    }

    /// <summary>
    /// U1U 更新对比窗体:左右两个清单并排——左边是上次清单(表格现有
    /// 内容),右边是本次将写入的清单;列与清单表格一致(序号/项目名称/
    /// 单位/数量/项次编码),变化行着色。仅展示有上次更新
    /// 信息的图框;多框时左侧列表选择。
    /// </summary>
    public sealed class FillUpdateCompareForm : Form
    {
        private readonly ListBox _itemList;
        private readonly Label _title;
        private readonly Label _history;
        private readonly Label _summary;
        private readonly ListView _previousList;
        private readonly ListView _plannedList;
        private readonly Button _ok;
        private readonly List<FillUpdateCompareItem> _items;

        public FillUpdateCompareForm(List<FillUpdateCompareItem> items)
        {
            _items = (items ?? new List<FillUpdateCompareItem>())
                .Where(item => item.HasHistory).ToList();
            bool multiple = _items.Count > 1;
            DialogLayout.Apply(this, "U1U · 清单更新对比",
                new Size(multiple ? 1180 : 920, 640), new Size(860, 480));

            // 左侧:图框列表(仅多框)
            var listPanel = new Panel { Dock = DockStyle.Left, Width = 240 };
            var listHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = "有更新记录的图框(" + _items.Count + " 个)",
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

            // 右侧:标题 + 更新信息 + 两个并排清单
            _title = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.Accent,
                Padding = new Padding(8, 4, 4, 0)
            };
            _history = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Font = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(8, 0, 4, 0)
            };
            _summary = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Font = UiTheme.FontCaption,
                ForeColor = UiTheme.TextPrimary,
                Padding = new Padding(8, 0, 4, 0)
            };

            _previousList = CreateBomList();
            _plannedList = CreateBomList();

            var previousPane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 2, 2, 0) };
            previousPane.Controls.Add(_previousList);
            previousPane.Controls.Add(PaneHeader("上次清单(表格现有内容)"));
            var plannedPane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 2, 0, 0) };
            plannedPane.Controls.Add(_plannedList);
            plannedPane.Controls.Add(PaneHeader("本次清单(将写入)"));

            var sides = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            sides.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            sides.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            sides.Controls.Add(previousPane, 0, 0);
            sides.Controls.Add(plannedPane, 1, 0);

            var detail = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            detail.Controls.Add(sides);
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

        private static Label PaneHeader(string text)
        {
            return new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = text,
                Font = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(2, 4, 2, 2)
            };
        }

        private ListView CreateBomList()
        {
            // 列与清单表格一致:序号/项目名称/特征描述/单位/数量/项次编码。
            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.FixedSingle,
                MultiSelect = false,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.FontCaption
            };
            list.Columns.Add("序号", 44);
            list.Columns.Add("项目名称", 170);
            list.Columns.Add("单位", 44);
            list.Columns.Add("数量", 76);
            list.Columns.Add("项次编码", 70);
            return list;
        }

        private void ShowSelected()
        {
            _previousList.BeginUpdate();
            _previousList.Items.Clear();
            _plannedList.BeginUpdate();
            _plannedList.Items.Clear();
            FillUpdateCompareItem item = Selected;
            if (item == null)
            {
                _previousList.EndUpdate();
                _plannedList.EndUpdate();
                return;
            }
            _title.Text = item.MachineId + " · " + item.DeviceName
                + (item.FrameHandle.Length > 0 ? "  （图框 " + item.FrameHandle + "）" : "");
            _history.Text = "上次更新时间: "
                + (string.IsNullOrWhiteSpace(item.LastUpdatedText) ? "未知" : item.LastUpdatedText)
                + "    上次更新用户: "
                + (string.IsNullOrWhiteSpace(item.LastUpdatedUser) ? "未知" : item.LastUpdatedUser);
            _summary.ForeColor = item.AddedCount + item.RemovedCount
                + item.QuantityChangedCount == 0
                ? UiTheme.SuccessFg : UiTheme.TextPrimary;
            _summary.Text = item.AddedCount + item.RemovedCount
                + item.QuantityChangedCount == 0
                ? "✓ 本次更新与上次完全一致，清单无变化"
                : "新增 " + item.AddedCount + " / 移除 " + item.RemovedCount
                    + " / 改量 " + item.QuantityChangedCount;

            foreach (FillRowDiff row in item.PreviousRows)
                _previousList.Items.Add(BomRow(row, row.OldQuantity,
                    row.Status == FillRowDiff.StatusRemoved
                        ? UiTheme.DangerFg : UiTheme.TextPrimary));
            _previousList.EndUpdate();

            foreach (FillRowDiff row in item.PlannedRows)
                _plannedList.Items.Add(BomRow(row, row.NewQuantity,
                    row.Status == FillRowDiff.StatusAdded ? UiTheme.SuccessFg
                        : row.Status == FillRowDiff.StatusQuantity
                            ? UiTheme.WarningFg : UiTheme.TextPrimary));
            _plannedList.EndUpdate();
            ResizeColumns();
        }

        private static ListViewItem BomRow(FillRowDiff row, string quantity, Color color)
        {
            string ordinal = row.Order > 0
                ? row.Order.ToString(CultureInfo.InvariantCulture) : "—";
            var item = new ListViewItem(ordinal)
            {
                UseItemStyleForSubItems = false,
                ForeColor = color
            };
            item.SubItems.Add(string.IsNullOrWhiteSpace(row.Name) ? "—" : row.Name);
            item.SubItems.Add(string.IsNullOrWhiteSpace(row.Unit) ? "—" : row.Unit);
            item.SubItems.Add(string.IsNullOrWhiteSpace(quantity) ? "—" : quantity);
            item.SubItems.Add(string.IsNullOrWhiteSpace(row.Code) ? "—" : row.Code);
            return item;
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
            foreach (ListView list in new[] { _previousList, _plannedList })
            {
                int width = Math.Max(360, list.ClientSize.Width - 8);
                int[] fixedWidths = { 44, 0, 44, 76, 70 };
                int fixedTotal = fixedWidths.Sum();
                int nameWidth = Math.Max(120, width - fixedTotal);
                list.Columns[0].Width = fixedWidths[0];
                list.Columns[1].Width = nameWidth;
                for (int i = 2; i < fixedWidths.Length; i++)
                    list.Columns[i].Width = fixedWidths[i];
            }
        }

        private FillUpdateCompareItem Selected
            => _itemList.SelectedIndex >= 0 && _itemList.SelectedIndex < _items.Count
                ? _items[_itemList.SelectedIndex] : null;
    }
}
