using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>U1U 批量结果的一行：一个机台一条，聚合它所有图框的写入量。</summary>
    public sealed class BatchUpdateMachineRow
    {
        public BatchUpdateMachineRow(string machineId)
        {
            MachineId = machineId ?? "";
        }

        public string MachineId { get; }
        public int Frames { get; set; }
        public int TableRows { get; set; }
        public int AttributeValues { get; set; }
        /// <summary>已生成 / 跳过（回路不全） / —</summary>
        public string BoqStatus { get; set; } = "—";
        public string BoqPath { get; set; } = "";
        public List<string> Notes { get; } = new List<string>();

        /// <summary>备注去重后拼接（同一机台多张图框常产生相同提示）。</summary>
        public string NoteText => string.Join("；", Notes.Distinct(
            StringComparer.OrdinalIgnoreCase));

        /// <summary>同一机台多张图框时把提示按原文去重合并。</summary>
        public void AddNote(string note)
        {
            if (string.IsNullOrWhiteSpace(note)) return;
            Notes.Add(note.Trim());
        }
    }

    /// <summary>
    /// U1U 批量更新结果窗（3.0 WPF，D-038/D-042）：批量“做了什么、结果如何、
    /// 文件在哪”都在这里看，命令行只留一行摘要。只读汇报；打开输出目录、
    /// 关闭（Esc/Enter 同关）是仅有的两个操作。
    /// </summary>
    public sealed partial class BatchUpdateResultWindow : FluentWindow
    {
        private readonly string _outputRoot;

        public string Heading => "批量完成 · " + _frameCount + " 个图框 / "
            + _machineCount + " 个机台";
        public string SummaryText => "表格写入 " + _tableRows + " 行 · 块 " + _blocks
            + " 个共更新 " + _attributeValues + " 项属性 · BOQ 新建 " + _boqAdded
            + " / 覆盖 " + _boqReplaced;
        public string Footer => "BOQ / DWG / PDF 都在输出目录的机台文件夹里；"
            + "跳过 BOQ 的机台把回路画全后重跑 U1U 即可补齐。";

        private readonly int _frameCount;
        private readonly int _machineCount;
        private readonly int _tableRows;
        private readonly int _blocks;
        private readonly int _attributeValues;
        private readonly int _boqAdded;
        private readonly int _boqReplaced;

        public BatchUpdateResultWindow(IReadOnlyList<BatchUpdateMachineRow> rows,
            int frameCount, int tableRows, int blocks, int attributeValues,
            int boqAdded, int boqReplaced, string outputRoot)
        {
            _frameCount = frameCount;
            _machineCount = rows?.Count ?? 0;
            _tableRows = tableRows;
            _blocks = blocks;
            _attributeValues = attributeValues;
            _boqAdded = boqAdded;
            _boqReplaced = boqReplaced;
            _outputRoot = outputRoot ?? "";
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            RowsGrid.ItemsSource = rows ?? new List<BatchUpdateMachineRow>();
        }

        private void OnOpenFolder(object sender, System.Windows.RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_outputRoot)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _outputRoot,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warn("打开输出目录失败: " + ex.Message);
            }
        }

        private void OnClose(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
