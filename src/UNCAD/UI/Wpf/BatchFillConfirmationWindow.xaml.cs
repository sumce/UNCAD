using System.Collections.Generic;
using System.Windows;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>One row shown before a multi-frame U1U update is committed.</summary>
    public sealed class BatchFillConfirmationRow
    {
        public BatchFillConfirmationRow(string frameHandle, string machineId,
            string deviceName, string deviceState, int rowCount)
        {
            FrameHandle = frameHandle ?? "";
            MachineId = machineId ?? "";
            DeviceName = deviceName ?? "";
            DeviceState = deviceState ?? "";
            RowCount = rowCount;
        }

        public string FrameHandle { get; }
        public string MachineId { get; }
        public string DeviceName { get; }
        public string DeviceState { get; }
        public int RowCount { get; }
    }

    /// <summary>
    /// 批量 U1U 写入前的整批确认窗（3.0 WPF 版，D-038 首个迁移窗体）。
    /// 模式 P2 工作台：上下文条 + 表格 + 底部操作条（docs/UI-3.0-DESIGN.md）。
    /// </summary>
    public sealed partial class BatchFillConfirmationWindow : FluentWindow
    {
        /// <summary>用户点了“确认更新”为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        internal string Heading => "将按图框边界批量更新 " + _rowCount + " 个已填图框";
        internal string Footer => _rowCount + " 个图框 · 一个事务提交 · 任一失败整批回滚";

        private readonly int _rowCount;

        public BatchFillConfirmationWindow(IReadOnlyList<BatchFillConfirmationRow> rows)
        {
            _rowCount = rows?.Count ?? 0;
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            BatchGrid.ItemsSource = rows;
        }

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
