using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UNCAD.Features.DwgExport;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>U1DWG 结果窗一行：一个机台一份 DWG。</summary>
    public sealed class DwgExportMachineRow
    {
        public DwgExportMachineRow(DwgExportMachine machine)
        {
            MachineId = machine.MachineId ?? "";
            Frames = machine.Frames;
            CircuitsText = machine.Circuits.Count == 0
                ? "—"
                : string.Join("、", machine.Circuits);
            CircuitCount = machine.Circuits.Count;
            FilePath = machine.FilePath ?? "";
        }

        public string MachineId { get; }
        public int Frames { get; }
        public int CircuitCount { get; }
        public string CircuitsText { get; }
        public string FilePath { get; }
    }

    /// <summary>
    /// U1DWG 导出结果窗（D-042 UI 信息完整性）：按机台列出本次导出的图框/回路/文件，
    /// 打开输出目录一键直达；命令行只留一行摘要。
    /// </summary>
    public sealed partial class DwgExportResultWindow : FluentWindow
    {
        private readonly string _outputRoot;

        public string Heading => "导出完成 · " + _frameCount + " 个图框 / "
            + _machineCount + " 个机台文件";
        public string Footer => "同机台图框已横向排列在各自 DWG 中；打开机台文件夹即可取图。";

        private readonly int _frameCount;
        private readonly int _machineCount;

        public DwgExportResultWindow(IReadOnlyList<DwgExportMachine> machines,
            int frameCount, string outputRoot)
        {
            _frameCount = frameCount;
            _machineCount = machines?.Count ?? 0;
            _outputRoot = outputRoot ?? "";
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            List<DwgExportMachineRow> rows = (machines ?? new List<DwgExportMachine>())
                .Select(machine => new DwgExportMachineRow(machine)).ToList();
            RowsGrid.ItemsSource = rows;
            StatusBadgeBar.Build(BadgeBar, new[]
            {
                new StatusBadge("机台 " + _machineCount + " 台", "success"),
                new StatusBadge("图框 " + _frameCount + " 个", "neutral"),
                new StatusBadge("DWG " + _machineCount + " 份", "neutral")
            });
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
