using System.Windows.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// U1SET「图框与排版」页（D-057）：制图信息表六列内容模板、清单（BOQ）标题
    /// 模板、XLAYOUT 排版参数。页面只承载布局；占位符插入条、预览、读写与校验
    /// 全部在 SettingsWindow。
    /// </summary>
    public partial class SettingsFramePage : UserControl
    {
        public SettingsFramePage()
        {
            InitializeComponent();
        }
    }
}
