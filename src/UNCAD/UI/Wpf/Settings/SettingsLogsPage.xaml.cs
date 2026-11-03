using System.Windows.Controls;

namespace UNCAD.UI
{
    /// <summary>U1SET 日志与记录页：级别/保留策略与最近操作记录（只读表格）。</summary>
    public partial class SettingsLogsPage : UserControl
    {
        public SettingsLogsPage()
        {
            InitializeComponent();
            OperationLogGrid.Visibility = System.Windows.Visibility.Collapsed;
        }
    }
}
