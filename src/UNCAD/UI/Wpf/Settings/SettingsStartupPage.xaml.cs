using System.Windows.Controls;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>U1SET 界面与启动页：启动动画开关与版本信息（只读）。</summary>
    public partial class SettingsStartupPage : UserControl
    {
        public SettingsStartupPage()
        {
            InitializeComponent();
            ProductText.Text = ProductMetadata.ProductName;
            VersionText.Text = ProductMetadata.VersionLabel
                + "（" + ProductMetadata.ReleaseDateUtc + "）";
        }
    }
}
