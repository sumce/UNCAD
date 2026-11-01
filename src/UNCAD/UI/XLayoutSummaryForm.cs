using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace UNCAD.UI
{
    public sealed class XLayoutMachineSummary
    {
        public XLayoutMachineSummary(string machineId)
        {
            MachineId = machineId ?? "";
        }

        public string MachineId { get; }
        public int CircuitCount { get; set; }
        public int ExpectedCircuitCount { get; set; }
        public int DuplicateCircuitCount { get; set; }
        public bool StatusKnown { get; set; }
        public bool IsComplete { get; set; }
        public string StatusText => !StatusKnown ? "未判断"
            : IsComplete ? "完整" : "缺少回路";
    }

    internal sealed class XLayoutSummaryForm : DpiAwareForm
    {
        public XLayoutSummaryForm(IReadOnlyList<XLayoutMachineSummary> summaries,
            int frameCount)
        {
            DialogLayout.Apply(this, "XLAYOUT · 排版统计", new Size(600, 440),
                new Size(500, 340), false);

            var frame = new UiDialogFrame(this,
                "排版完成  ·  " + frameCount + " 个图框  /  "
                    + (summaries?.Count ?? 0) + " 个机台",
                "按机台查看回路数量与完整性；缺少回路的机台不会自动补齐。");

            var grid = new DataGridView
            {
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
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "机台ID",
                DataPropertyName = nameof(XLayoutMachineSummary.MachineId),
                FillWeight = 68
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "回路数量",
                DataPropertyName = nameof(XLayoutMachineSummary.CircuitCount),
                FillWeight = 32,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight
                }
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "状态",
                DataPropertyName = nameof(XLayoutMachineSummary.StatusText),
                FillWeight = 28
            });
            grid.DataSource = (summaries ?? Array.Empty<XLayoutMachineSummary>()).ToList();
            UiTheme.FixGridRows(grid, 30);
            frame.Body.Controls.Add(grid);

            Button close = frame.AddSecondary("关闭", DialogResult.Cancel);
            frame.Wire(close, close);
            AcceptButton = close;
            CancelButton = close;
            Shown += (sender, args) => close.Select();
        }
    }
}
