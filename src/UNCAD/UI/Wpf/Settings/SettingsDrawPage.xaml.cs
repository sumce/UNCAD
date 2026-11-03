using System.Windows.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// U1SET 绘制标注页：U1L 线段、U1Q 桥架、U1C 线管、U1R 拱桥合并为一页四卡。
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
