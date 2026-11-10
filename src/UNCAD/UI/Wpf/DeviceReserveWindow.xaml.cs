using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using UNCAD.Core.Text;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>批量 U1U 待填行：一个还没有设备内预留记录的图框。</summary>
    public sealed class DeviceReserveRow : INotifyPropertyChanged
    {
        public DeviceReserveRow(string machineId, string deviceName)
            : this(machineId, deviceName, null, "")
        {
        }

        /// <summary>
        /// D-052-B：可带预填值与来源标注（复制图框用源框 CABLE_INFO 解析值预填，
        /// Note 显示“复制源预填，请核对”）。用户仍可改——预填只是免除“明明图上
        /// 有预留却让从 0 填起”的困扰，不构成静默继承。
        /// </summary>
        public DeviceReserveRow(string machineId, string deviceName,
            string prefillReserveMm, string note)
        {
            MachineId = machineId ?? "";
            DeviceName = deviceName ?? "";
            Note = note ?? "";
            _reserveMm = string.IsNullOrWhiteSpace(prefillReserveMm) ? "0"
                : prefillReserveMm.Trim();
        }

        public string MachineId { get; }

        public string DeviceName { get; }

        /// <summary>来源标注（如“复制源预填，请核对”）；空 = 常规新框。</summary>
        public string Note { get; }

        /// <summary>显示序号（1 起，按构造顺序）。</summary>
        public int Index { get; set; }

        /// <summary>界面上的毫米输入；确认时由窗体验证并解析。</summary>
        public string ReserveMm
        {
            get { return _reserveMm; }
            set { _reserveMm = value ?? ""; OnPropertyChanged(); }
        }

        private string _reserveMm;

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 批量 U1U 首次使用：逐图框输入设备内预留（毫米）。不同设备的预留通常
    /// 不同，因此一行一框；顶部“统一值”只是快捷填充，确认仍按行取值。
    /// 只应用于本次还没有设备内预留记录的图框；已有记录保持原值、不进列表。
    /// 3.0 WPF 版（D-038，P3 事务确认模式）；权威校验在“确定”按钮里。
    /// </summary>
    public sealed partial class DeviceReserveWindow : FluentWindow
    {
        /// <summary>与现场一致的上限：设备内预留最大 100000 毫米。</summary>
        internal const double MaxReserveMillimeters = 100000d;

        /// <summary>用户点了“确定”为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        public string Heading => "有 " + _rows.Count + " 个图框还没有设备内预留记录。"
            + "逐行填写，或先统一填入再单独调整；确认后写入 frameinfo_json，后续 U1U 不再询问。";

        private readonly List<DeviceReserveRow> _rows;

        public DeviceReserveWindow(IReadOnlyList<DeviceReserveRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            _rows = rows.ToList();
            for (int i = 0; i < _rows.Count; i++) _rows[i].Index = i + 1;
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
            ReserveGrid.ItemsSource = _rows;
            Loaded += (sender, args) => UniformBox.Focus();
        }

        /// <summary>第 index 行（与构造顺序一致）确认后的设备内预留（毫米）。</summary>
        public double ReserveMillimeters(int index)
        {
            return Math.Max(0d, Math.Min(MaxReserveMillimeters,
                ParseReserve((_rows[index].ReserveMm ?? "").Trim())));
        }

        internal static string ReserveError(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!(double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value)
                || double.TryParse(text, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out value))
                || value < 0d || value > MaxReserveMillimeters)
                return "必须是 0~100000 的毫米数。";
            return null;
        }

        private static double ParseReserve(string text)
        {
            if (text.Length == 0) return 0d;
            if (double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value)
                || double.TryParse(text, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out value))
                return value;
            return 0d;
        }

        private void OnUniformKeyDown(object sender, KeyEventArgs e)
        {
            // 统一值框里按回车 = 应用到全部，而不是直接确认整窗。
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ApplyAll();
            }
        }

        private void OnApplyAll(object sender, RoutedEventArgs e) => ApplyAll();

        private void ApplyAll()
        {
            string text = (UniformBox.Text ?? "").Trim();
            string error = ReserveError(text);
            if (error != null)
            {
                ShowError("统一值" + error);
                UniformBox.Focus();
                UniformBox.SelectAll();
                return;
            }
            string formatted = TextFormatter.FormatNum(ParseReserve(text));
            foreach (DeviceReserveRow row in _rows) row.ReserveMm = formatted;
            HideError();
        }

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            // 权威校验在这里按每行最终值执行（绑定是 PropertyChanged 即时回写，
            // 无悬挂编辑问题）；任一行非法就提示并留在窗口，绝不带着坏值关闭。
            for (int i = 0; i < _rows.Count; i++)
            {
                string text = (_rows[i].ReserveMm ?? "").Trim();
                string error = ReserveError(text);
                if (error != null)
                {
                    ShowError("第 " + (i + 1) + " 行（机台 " + _rows[i].MachineId
                        + "）" + error);
                    // 单元格级选区：直接选中出问题的那个输入格。
                    ReserveGrid.SelectedCells.Clear();
                    ReserveGrid.SelectedCells.Add(new System.Windows.Controls
                        .DataGridCellInfo(_rows[i], ReserveGrid.Columns[3]));
                    ReserveGrid.Focus();
                    return;
                }
            }
            HideError();
            Confirmed = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorBar.Message = message;
            ErrorBar.IsOpen = true;
        }

        private void HideError() => ErrorBar.IsOpen = false;
    }
}
