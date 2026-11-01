using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Stat;

namespace UNCAD.UI
{
    /// <summary>Review window for XSTS circuit coverage before/after Excel export.</summary>
    public sealed class XstsForm : DpiAwareForm
    {
        public XstsForm(XstsReport report, string outputPath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            DialogLayout.Apply(this, "XSTS · 机台回路统计", new Size(860, 560),
                new Size(700, 440));

            string coverage = report.ExpectedDataAvailable && !report.HasUnknownMachineBaseline
                ? "有效回路 " + report.SelectedCircuitCount + " / 应有 "
                    + report.ExpectedCircuitCount + " · 缺少 "
                    + report.MissingCircuitCount + " · 多出 "
                    + report.UnexpectedCircuitCount + " · 异常 " + report.Issues.Count
                : "有效回路 " + report.SelectedCircuitCount
                    + " · 应有/缺少/多出 "
                    + (report.ExpectedDataAvailable ? "部分无法判断" : "无法判断")
                    + " · " + report.ExpectedDataDetail
                    + " · 异常 " + report.Issues.Count;
            var frame = new UiDialogFrame(this,
                "机台回路统计 · " + report.Machines.Count + " 个机台 / "
                    + report.FrameCount + " 张图纸",
                coverage);

            DataGridView grid = BuildTable("XstsGrid",
                new[] { "机台 ID", "批次", "序号", "已选回路", "应有回路", "缺少回路", "多出回路" },
                new[] { 25f, 14f, 14f, 15f, 15f, 45f, 35f });
            BindRows(grid, report.Machines.Cast<object>().Select(machine => new object[]
            {
                ((XstsMachineSummary)machine).MachineId,
                ((XstsMachineSummary)machine).Batch,
                ((XstsMachineSummary)machine).Seq,
                ((XstsMachineSummary)machine).SelectedCircuitCount,
                ((XstsMachineSummary)machine).ExpectedCircuitText,
                ((XstsMachineSummary)machine).MissingText,
                ((XstsMachineSummary)machine).UnexpectedText
            }));

            DataGridView issueGrid = BuildTable("XstsIssueGrid",
                new[] { "图框", "机台 ID", "回路名称", "问题" },
                new[] { 12f, 22f, 24f, 42f });
            BindRows(issueGrid, report.Issues.Cast<object>().Select(issue => new object[]
            {
                ((XstsFrameIssue)issue).FrameNumber,
                ((XstsFrameIssue)issue).MachineId,
                ((XstsFrameIssue)issue).CircuitName,
                ((XstsFrameIssue)issue).Description
            }));

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var coverageTab = new TabPage("回路统计");
            var issuesTab = new TabPage("异常图框 (" + report.Issues.Count + ")");
            coverageTab.Controls.Add(grid);
            issuesTab.Controls.Add(issueGrid);
            tabs.TabPages.Add(coverageTab);
            tabs.TabPages.Add(issuesTab);

            var pathLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(outputPath)
                    ? "未导出 Excel" : "Excel: " + outputPath,
                AutoEllipsis = true,
                // 必须是 Bottom 而不是 Fill：Fill 会与报表 TabControl 争抢同一块
                // 剩余空间，两个 Fill 兄弟会得到完全相同的边界，后加入的页脚渲染在
                // 上层，把整张报表遮住。Dock 按加入顺序逆序生效，这里配合下方
                // Controls.Add 的顺序（tabs → pathLabel）即可让页脚固定在命令栏上方。
                Dock = DockStyle.Bottom,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(0, UiTheme.SpaceS, 0, 0)
            };
            frame.Body.Controls.Add(tabs);
            frame.Body.Controls.Add(pathLabel);

            Button close = frame.AddSecondary("关闭", DialogResult.Cancel);
            frame.Wire(close, close);
            AcceptButton = close;
            CancelButton = close;
            Shown += (sender, args) => close.Select();
        }

        private static DataGridView BuildTable(string name, string[] headers, float[] weights)
        {
            var grid = new DataGridView
            {
                Name = name,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.None
            };
            UiKit.StyleTable(grid, readOnly: true);
            for (int index = 0; index < headers.Length; index++)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = headers[index],
                    FillWeight = weights[index],
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
            }
            return grid;
        }

        private static void BindRows(DataGridView grid, IEnumerable<object[]> rows)
        {
            foreach (object[] values in rows) grid.Rows.Add(values);
            UiTheme.FixGridRows(grid, 32);
        }
    }
}
