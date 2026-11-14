using System.Windows.Controls;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// U1SET 概览页：一眼看清版本、数据源与 Downstream 规则，再按需进入各页。
    /// 只读展示；产品/版本在代码里填充（ProductMetadata 单一来源）。
    /// </summary>
    public partial class SettingsOverviewPage : UserControl
    {
        public SettingsOverviewPage()
        {
            InitializeComponent();
            ProductText.Text = ProductMetadata.ProductName;
            VersionText.Text = ProductMetadata.DetailedVersionText
                + "（" + ProductMetadata.ReleaseDateUtc + "）";
        }
    }
}
