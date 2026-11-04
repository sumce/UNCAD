using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>U1U 强制 BOQ 选择的一行机台（D-048，2026-11-04）：缺回路机台
    /// 默认不勾选（跳过其 BOQ），勾选表示按图面已画回路强制生成。</summary>
    public sealed class BoqForceMachineChoice
    {
        public string MachineId { get; set; } = "";
        /// <summary>本次选中的回路数（已画）。</summary>
        public int SelectedCircuitCount { get; set; }
        /// <summary>机台数据里的应有回路数。</summary>
        public int ExpectedCircuitCount { get; set; }
        /// <summary>机台数据里有、本次没画到的回路名。</summary>
        public List<string> MissingNames { get; set; } = new List<string>();
        /// <summary>是否强制生成该机台的 BOQ（复选框）。</summary>
        public bool Force { get; set; }

        public string CoverageText
            => SelectedCircuitCount + " / " + ExpectedCircuitCount;

        public string MissingText
        {
            get
            {
                if (MissingNames.Count == 0) return "—";
                const int shown = 6;
                List<string> pick = MissingNames.Take(shown).ToList();
                string text = string.Join("、", pick);
                return MissingNames.Count > shown
                    ? text + " 等 " + MissingNames.Count + " 个" : text;
            }
        }
    }

    /// <summary>
    /// U1U 缺回路机台的 BOQ 处理窗（D-048，2026-11-04）：存在回路不全的机台时
    /// 弹出。默认全部不勾选 = 照旧跳过这些机台的 BOQ（只更新图纸）；勾选的机台
    /// 由用户确认后按已画回路强制生成 BOQ，结果窗备注列会标注。确定时允许空
    /// 选择（= 全部跳过），取消等价于全部跳过。
    /// </summary>
    public sealed partial class BoqForceSelectionWindow : FluentWindow
    {
        /// <summary>用户点了"确定"为 true；取消/Esc/关闭为 false（两者都不强制）。</summary>
        public bool Confirmed { get; private set; }

        /// <summary>确定后要强制生成 BOQ 的机台 ID（可能为空 = 全部跳过）。</summary>
        public IReadOnlyList<string> ForcedMachineIds { get; private set; }
            = Array.Empty<string>();

        private readonly List<BoqForceMachineChoice> _choices;

        /// <summary>机台行（测试用）。</summary>
        internal IReadOnlyList<BoqForceMachineChoice> Choices => _choices;

        public BoqForceSelectionWindow(IReadOnlyList<BoqForceMachineChoice> choices)
        {
            _choices = (choices ?? Array.Empty<BoqForceMachineChoice>())
                .Where(choice => choice != null).ToList();
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            MachineGrid.ItemsSource = _choices;
            HeaderText.Text = "有 " + _choices.Count + " 台机台回路不全";
        }

        /// <summary>勾选强制生成的机台 ID。</summary>
        internal IReadOnlyList<string> ComputeForcedMachineIds()
            => _choices.Where(choice => choice.Force)
                .Select(choice => choice.MachineId).ToList();

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            // 空选择是合法的（= 全部跳过），不做拦截——与导出窗不同，
            // 这里的语义是"决定要不要强制"，不是"至少选一个"。
            Confirmed = true;
            ForcedMachineIds = ComputeForcedMachineIds();
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
