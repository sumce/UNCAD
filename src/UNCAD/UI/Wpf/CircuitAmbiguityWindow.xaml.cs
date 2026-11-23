using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using UNCAD.Core.Excel;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>同名回路候选的显示行（D-056）；包一层避免 UI 直接绑定 MachineRow。</summary>
    public sealed class CircuitAmbiguityRow
    {
        public CircuitAmbiguityRow(MachineRow row)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
        }

        public MachineRow Row { get; }

        public string CircuitName => Row.CircuitName ?? "";
        public string UpstreamId => Row.Fr ?? "";
        public string FacilitySwitch => Row.FacilitySwitch ?? "";
        public string Cable => Row.Cable ?? "";
        public string UpstreamType => Row.Next ?? "";
        public string Batch => Row.Batch ?? "";

        public string FloorAxis
        {
            get
            {
                string floor = Row.DeviceFloor ?? "";
                string axis = Row.DownstreamAxis ?? "";
                if (floor.Length == 0) return axis;
                if (axis.Length == 0) return floor;
                return floor + " · " + axis;
            }
        }

        /// <summary>详情条全文：全部 U_ 字段，选中行时显示。</summary>
        public string DetailText => Row.ToString() + " | 上游编号:" + UpstreamId
            + " | 厂务开关:" + FacilitySwitch
            + " | 设备楼层:" + (Row.DeviceFloor ?? "") + " | 上游楼层:" + (Row.PanelFloor ?? "")
            + " | 配电信息:" + (Row.Detail ?? "");
    }

    /// <summary>
    /// D-056 同名回路选择窗：同一机台存在多条同名回路（上游编号不同）且图框
    /// 无回路身份键时，由用户指认本图框对应的那一条。密排候选表 + 底部全字段
    /// 详情条；双击行或“确认选择”返回所选行，取消返回 Confirmed=false。
    /// </summary>
    public sealed partial class CircuitAmbiguityWindow : FluentWindow
    {
        /// <summary>用户确认了选择且存在选中行为 true。</summary>
        public bool Confirmed { get; private set; }

        /// <summary>用户选中的候选行（未确认为 null）。</summary>
        public MachineRow SelectedRow { get; private set; }

        public string Heading => "机台 " + _machineId + " · 同名回路 “"
            + _deviceName + "” 有 " + _count + " 条";

        public string Caption => "这些回路同名但上游编号（取电点）不同，属于不同回路。"
            + "请按图框实际取电的上级编号选择对应行；确认后 frameinfo_json 会写入"
            + "回路身份键，下次 U1U 不再询问。";

        private readonly string _machineId;
        private readonly string _deviceName;
        private readonly int _count;

        public CircuitAmbiguityWindow(string machineId, string deviceName,
            IReadOnlyList<MachineRow> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                throw new ArgumentException("候选回路不能为空。", nameof(candidates));
            _machineId = machineId ?? "";
            _deviceName = deviceName ?? "";
            _count = candidates.Count;
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            CandidateGrid.ItemsSource = new List<CircuitAmbiguityRow>(
                System.Linq.Enumerable.Select(candidates, row => new CircuitAmbiguityRow(row)));
            CandidateGrid.SelectedIndex = 0;
            UpdateDetail();
        }

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateDetail();

        private void OnGridDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => OnConfirm(null, null);

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            if (CandidateGrid.SelectedItem is CircuitAmbiguityRow row)
            {
                SelectedRow = row.Row;
                Confirmed = true;
                Close();
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            SelectedRow = null;
            Close();
        }

        private void UpdateDetail()
        {
            DetailText.Text = (CandidateGrid.SelectedItem as CircuitAmbiguityRow)
                ?.DetailText ?? "（未选中候选）";
        }
    }
}
