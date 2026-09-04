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
        public int ChangedCount { get; set; }
        public List<FrameInfoFieldDiff> Diffs { get; set; } = new List<FrameInfoFieldDiff>();
    }

    /// <summary>
    /// U1U 更新对比窗体:展示每个待更新图框的上次更新时间/用户,以及
    /// 上次记录与本次将写入值的逐字段对比。多框时左侧列表选择、右侧详情,
    /// 单框时直接显示详情。
    /// </summary>
    public sealed class FillUpdateCompareForm : Form
    {
        private readonly ListBox _itemList;
        private readonly Label _title;
        private readonly Label _history;
        private readonly ListView _diffs;
        private readonly Button _ok;
        private readonly List<FillUpdateCompareItem> _items;

        public FillUpdateCompareForm(List<FillUpdateCompareItem> items)
        {
            _items = items ?? new List<FillUpdateCompareItem>();
            bool multiple = _items.Count > 1;
            DialogLayout.Apply(this, "U1U · 更新对比",
                new Size(multiple ? 960 : 760, 620), new Size(720, 480));

            // 左侧:图框列表(仅多框)
            var listPanel = new Panel { Dock = DockStyle.Left, Width = 250 };
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
                Height = 44,
                Font = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
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
            _diffs.Columns.Add("字段", 140);
            _diffs.Columns.Add("上次记录", 260);
            _diffs.Columns.Add("本次写入", 260);

            var detail = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            detail.Controls.Add(_diffs);
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
                string changedSuffix = item.HasHistory && item.ChangedCount > 0
                    ? "  ·  " + item.ChangedCount + " 项变化" : "";
                _itemList.Items.Add(item.MachineId + " · " + item.DeviceName + changedSuffix);
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
                ? "上次更新时间: " + item.LastUpdatedText
                    + "    上次更新用户: " + (string.IsNullOrWhiteSpace(item.LastUpdatedUser)
                        ? "未知" : item.LastUpdatedUser)
                    + "    变化 " + item.ChangedCount + " / " + item.Diffs.Count + " 项"
                : "首次更新 · 无历史记录";
            foreach (FrameInfoFieldDiff diff in item.Diffs)
            {
                var row = new ListViewItem(diff.Label) { UseItemStyleForSubItems = false };
                row.SubItems.Add(string.IsNullOrWhiteSpace(diff.Old) ? "—" : diff.Old);
                row.SubItems.Add(string.IsNullOrWhiteSpace(diff.New) ? "—" : diff.New);
                if (diff.Changed)
                {
                    row.SubItems[1].ForeColor = UiTheme.DangerFg;
                    row.SubItems[2].ForeColor = UiTheme.SuccessFg;
                    row.ForeColor = UiTheme.TextPrimary;
                }
                else
                {
                    row.ForeColor = UiTheme.TextSecondary;
                }
                _diffs.Items.Add(row);
            }
            _diffs.EndUpdate();
            ResizeColumns();
        }

        private void ResizeColumns()
        {
            int width = Math.Max(420, _diffs.ClientSize.Width - 8);
            int labelWidth = 140;
            _diffs.Columns[0].Width = labelWidth;
            _diffs.Columns[1].Width = Math.Max(180, (width - labelWidth) / 2);
            _diffs.Columns[2].Width = Math.Max(180, width - labelWidth - _diffs.Columns[1].Width);
        }

        private FillUpdateCompareItem Selected
            => _itemList.SelectedIndex >= 0 && _itemList.SelectedIndex < _items.Count
                ? _items[_itemList.SelectedIndex] : null;
    }
}
