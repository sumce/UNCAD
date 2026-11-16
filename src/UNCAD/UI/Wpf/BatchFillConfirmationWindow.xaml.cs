using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using UNCAD.Core.QuickLine;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>One row shown before a multi-frame U1U update is committed.</summary>
    public sealed class BatchFillConfirmationRow
    {
        public BatchFillConfirmationRow(string frameHandle, string machineId,
            string deviceName, string deviceState, int rowCount,
            string connectivityText = null)
        {
            FrameHandle = frameHandle ?? "";
            MachineId = machineId ?? "";
            DeviceName = deviceName ?? "";
            DeviceState = deviceState ?? "";
            RowCount = rowCount;
            ConnectivityText = string.IsNullOrWhiteSpace(connectivityText)
                ? "—" : connectivityText;
        }

        public string FrameHandle { get; }
        public string MachineId { get; }
        public string DeviceName { get; }
        public string DeviceState { get; }
        public int RowCount { get; }
        /// <summary>3.3.0（D-054 特性②）：连通性列文案（✔ N 段一条链 / ⚠ 孤立段…）。</summary>
        public string ConnectivityText { get; }
    }

    /// <summary>
    /// 批量 U1U 写入前的整批确认窗（3.0 WPF 版，D-038 首个迁移窗体）。
    /// 模式 P2 工作台：上下文条 + 表格 + 底部操作条（docs/UI-3.0-DESIGN.md）。
    /// 3.3.0（D-054）：加「连通性」列（报告级，不拦截）；选中行左侧 3D 路由
    /// 预览切换到该图框场景（previewsByFrameHandle 由批量链路预建后传入）；
    /// 底部可显示图层矫正报告（特性①）。
    /// </summary>
    public sealed partial class BatchFillConfirmationWindow : FluentWindow
    {
        /// <summary>用户点了“确认更新”为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        public string Heading => "将按图框边界批量更新 " + _rowCount + " 个已填图框";
        public string Footer => _rowCount + " 个图框 · 一个事务提交 · 任一失败整批回滚";

        private readonly int _rowCount;
        private readonly Dictionary<string, QuickLineOrbitPreviewModel> _previews;

        public BatchFillConfirmationWindow(IReadOnlyList<BatchFillConfirmationRow> rows,
            IReadOnlyDictionary<string, QuickLineOrbitPreviewModel> previewsByFrameHandle = null,
            string layerCorrectionText = null)
        {
            _rowCount = rows?.Count ?? 0;
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            BatchGrid.ItemsSource = rows;
            _previews = new Dictionary<string, QuickLineOrbitPreviewModel>(
                StringComparer.OrdinalIgnoreCase);
            if (previewsByFrameHandle != null)
            {
                foreach (KeyValuePair<string, QuickLineOrbitPreviewModel> pair
                    in previewsByFrameHandle)
                    if (pair.Key != null && !_previews.ContainsKey(pair.Key))
                        _previews[pair.Key] = pair.Value;
            }
            if (_previews.Count > 0)
            {
                PreviewColumn.Width = new GridLength(320, GridUnitType.Pixel);
                PreviewHost.Visibility = Visibility.Visible;
                // 默认选中第一行：预览开窗即有内容，点行切换。
                if (_rowCount > 0) BatchGrid.SelectedIndex = 0;
                ShowPreviewForSelectedRow();
            }
            if (!string.IsNullOrWhiteSpace(layerCorrectionText))
            {
                LayerGovernorText.Text = layerCorrectionText;
                LayerGovernorText.Visibility = Visibility.Visible;
            }
        }

        /// <summary>当前预览展示的场景模型（测试断言用；无预览为 null）。</summary>
        internal QuickLineOrbitPreviewModel CurrentPreview => OrbitPreview.Model;

        private void OnGridSelectionChanged(object sender,
            SelectionChangedEventArgs e)
            => ShowPreviewForSelectedRow();

        /// <summary>选中行 → 该图框的预建场景；没有场景的行显示占位提示。</summary>
        private void ShowPreviewForSelectedRow()
        {
            if (PreviewHost.Visibility != Visibility.Visible) return;
            string handle = (BatchGrid.SelectedItem as BatchFillConfirmationRow)
                ?.FrameHandle;
            _previews.TryGetValue(handle ?? "", out QuickLineOrbitPreviewModel model);
            OrbitPreview.SetModel(model);
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
