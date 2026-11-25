using System.Windows;

namespace UNCAD.UI
{
    /// <summary>
    /// WPF 设计令牌字典（内容在 FluentKit.xaml；与 2.10 WinForms 组件库
    /// 同一套设计语言，旧组件库已在 3.8.0 删除）。由 UiShell.ApplyFluentTheme
    /// 按窗口合并。
    /// </summary>
    public partial class FluentKit : ResourceDictionary
    {
        public FluentKit()
        {
            InitializeComponent();
        }
    }
}
