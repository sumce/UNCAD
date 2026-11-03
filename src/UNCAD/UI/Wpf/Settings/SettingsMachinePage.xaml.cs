using System.Windows.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// U1SET 机台数据页：本地/UNC 数据源与快照状态（2.10.0 起不再支持 HTTP 下载）。
    /// 浏览/刷新/快照状态联动由 SettingsWindow 接线，本页只承载布局。
    /// </summary>
    public partial class SettingsMachinePage : UserControl
    {
        public SettingsMachinePage()
        {
            InitializeComponent();
        }
    }
}
