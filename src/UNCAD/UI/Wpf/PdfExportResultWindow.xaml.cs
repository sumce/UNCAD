using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UNCAD.Core.Submission;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>U1PDF 结果窗一行：一份 PDF（一个图框 = 一个回路）。</summary>
    public sealed class PdfExportResultRow
    {
        public PdfExportResultRow(PdfExportTarget target)
        {
            MachineId = target.MachineId ?? "";
            CircuitName = string.IsNullOrWhiteSpace(target.CircuitName)
                ? "(未识别回路)" : target.CircuitName;
            bool ok = string.IsNullOrWhiteSpace(target.LastError);
            Status = ok ? "已出图" : "失败";
            Error = ok ? "" : target.LastError;
            FilePath = ok ? target.FilePath : "";
        }

        public string MachineId { get; }
        public string CircuitName { get; }
        public string Status { get; }
        public string Error { get; }
        public string FilePath { get; }
    }

    /// <summary>
    /// U1PDF 出图结果窗（D-042 UI 信息完整性）：每份 PDF 一行（状态/原因/文件），
    /// 失败不再只给“首个失败诊断”；进度在进度窗、明细在这里。
    /// </summary>
    public sealed partial class PdfExportResultWindow : FluentWindow
    {
        private readonly string _outputRoot;

        public string Heading => "出图完成 · 成功 " + _rendered + " / 失败 "
            + _failed + "（共 " + _total + " 份）";

        /// <summary>下一步指引：失败的说先看原因，成功的说交付方式。</summary>
        public string NextStep => _failed > 0
            ? "下一步：先看失败行的原因（通常是打印设备/图框异常），修复后重跑 U1PDF 即可补出。"
            : "下一步：PDF 已在各机台文件夹里，与 BOQ / DWG 同夹，可直接交付。";

        public string Footer => "一个图框 = 一个回路 = 一份 PDF；同名回路文件名自动去重。";

        private readonly int _rendered;
        private readonly int _failed;
        private readonly int _total;

        public PdfExportResultWindow(IReadOnlyList<PdfExportTarget> targets,
            int rendered, int failed, string outputRoot)
        {
            _rendered = rendered;
            _failed = failed;
            _total = targets?.Count ?? 0;
            _outputRoot = outputRoot ?? "";
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            List<PdfExportResultRow> rows = (targets ?? new List<PdfExportTarget>())
                .Select(target => new PdfExportResultRow(target)).ToList();
            RowsGrid.ItemsSource = rows;
            StatusBadgeBar.Build(BadgeBar, new[]
            {
                new StatusBadge("成功 " + _rendered + " 份",
                    _rendered > 0 ? "success" : "neutral"),
                new StatusBadge("失败 " + _failed + " 份",
                    _failed > 0 ? "critical" : "neutral"),
                new StatusBadge("合计 " + _total + " 份", "neutral")
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
