using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// U1DWG 导出选择的一行机台（D-047，2026-11-04）：回路齐全默认勾选导出；
    /// 缺回路默认不勾选，用户可勾选强制导出。Export 由 DataGrid 复选框双向
    /// 绑定写回，窗口确认后由 Features 读取勾选结果。
    /// </summary>
    public sealed class DwgExportMachineChoice
    {
        public string MachineId { get; set; } = "";
        /// <summary>整机回路校验是否通过（机台数据里所有回路都在本次选集中）。</summary>
        public bool Complete { get; set; }
        /// <summary>本次选中的回路（去重后）数量。</summary>
        public int CircuitCount { get; set; }
        /// <summary>机台数据里有、本次没选中的回路名。</summary>
        public List<string> MissingNames { get; set; } = new List<string>();
        /// <summary>是否导出该机台（复选框）。</summary>
        public bool Export { get; set; }

        public string StatusText => Complete ? "回路齐全" : "缺回路";
        public string CircuitCountText => CircuitCount + " 个";
        public string MissingText
        {
            get
            {
                if (Complete || MissingNames.Count == 0) return "—";
                const int shown = 6;
                List<string> pick = MissingNames.Take(shown).ToList();
                string text = string.Join("、", pick);
                return MissingNames.Count > shown ? text + " 等 " + MissingNames.Count + " 个" : text;
            }
        }
    }

    /// <summary>
    /// U1DWG 导出机台选择窗（D-047，2026-11-04）：存在回路不全的机台时弹出。
    /// 旧行为是"一台缺回路整条命令中止"，完整机台也被扣住不出图；现在按机台
    /// 勾选：完整机台默认导出，缺回路的默认跳过、可勾选强制。空选择不允许
    /// 确认；"开始导出"才写盘，取消不生成任何文件。
    /// </summary>
    public sealed partial class DwgExportSelectionWindow : FluentWindow
    {
        /// <summary>用户点了"开始导出"为 true；关闭/取消/Esc 一律 false。</summary>
        public bool Confirmed { get; private set; }

        /// <summary>确定后要导出的机台 ID（去空格大小写不敏感的原文列表）。</summary>
        public IReadOnlyList<string> SelectedMachineIds { get; private set; }
            = Array.Empty<string>();

        private readonly List<DwgExportMachineChoice> _choices;

        /// <summary>机台行（测试用）。</summary>
        internal IReadOnlyList<DwgExportMachineChoice> Choices => _choices;

        public DwgExportSelectionWindow(List<DwgExportMachineChoice> choices)
        {
            _choices = (choices ?? new List<DwgExportMachineChoice>())
                .Where(choice => choice != null).ToList();
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            MachineGrid.ItemsSource = _choices;
            HeaderText.Text = "有 " + _choices.Count(choice => !choice.Complete)
                + " 台机台回路不全（共 " + _choices.Count + " 台）";
        }

        /// <summary>勾选导出的机台 ID（树的权威输出：确认校验也用它）。</summary>
        internal IReadOnlyList<string> ComputeSelectedMachineIds()
            => _choices.Where(choice => choice.Export)
                .Select(choice => choice.MachineId).ToList();

        private void OnExport(object sender, RoutedEventArgs e)
        {
            // 权威校验：空选择绝不带着 Confirmed 关窗。
            IReadOnlyList<string> selected = ComputeSelectedMachineIds();
            if (selected.Count == 0)
            {
                WarningBar.Message = "至少勾选一个机台再开始导出。";
                WarningBar.IsOpen = true;
                return;
            }
            Confirmed = true;
            SelectedMachineIds = selected;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
