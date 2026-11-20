using System.Windows.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// U1SET 绘制标注页：集中编辑线段、桥架、线管、软管及 Xauto 默认参数。
    /// 规格由 U1Q1/U1Q2/U1Q4 命令固定传入，本页不再提供“默认规格”配置。
    /// </summary>
    public partial class SettingsDrawPage : UserControl
    {
        public SettingsDrawPage()
        {
            InitializeComponent();
        }
    }
}
