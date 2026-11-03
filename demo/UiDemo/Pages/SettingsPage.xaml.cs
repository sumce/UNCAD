using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;

namespace UNCAD.UiDemo.Pages
{
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();
        }

        private void OnLightClick(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        }

        private void OnDarkClick(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }
    }
}
