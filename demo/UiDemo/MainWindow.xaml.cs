using System;
using System.Windows;
using UNCAD.UiDemo.Pages;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace UNCAD.UiDemo
{
    public partial class MainWindow : FluentWindow
    {
        private readonly SettingsPage _settings = new SettingsPage();
        private readonly FillReviewPage _review = new FillReviewPage();
        private readonly BatchConfirmPage _batch = new BatchConfirmPage();

        public MainWindow()
        {
            InitializeComponent();
            Navi.SetServiceProvider(new PageProvider(_settings, _review, _batch));
            // NavigationView 的内容区在模板应用后才存在，首次导航必须等 Loaded。
            Loaded += (sender, args) => ShowPage("settings");
        }

        /// <summary>演示页切换：外部（截图模式）也走同一条路，保证所见即所截。</summary>
        public void ShowPage(string key)
        {
            Navi.Navigate(PageProvider.TypeOf(key), null);
        }

        private void OnNavItemInvoked(NavigationView sender, RoutedEventArgs args)
        {
            // v4 的 ItemInvoked 只给 RoutedEventArgs：被点项从 OriginalSource 或当前选中项里取。
            var item = args.OriginalSource as NavigationViewItem
                ?? sender.SelectedItem as NavigationViewItem;
            switch (item?.Tag as string)
            {
                case "login":
                    var login = new LoginWindow { Owner = this };
                    login.ShowDialog();
                    break;
                case "theme":
                    var current = ApplicationThemeManager.GetAppTheme();
                    ApplicationThemeManager.Apply(
                        current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark);
                    break;
            }
        }

        /// <summary>把预建页面实例按 Type 提供给 NavigationView（免 DI 容器）。</summary>
        private sealed class PageProvider : IServiceProvider
        {
            private readonly SettingsPage _settings;
            private readonly FillReviewPage _review;
            private readonly BatchConfirmPage _batch;

            public PageProvider(SettingsPage settings, FillReviewPage review, BatchConfirmPage batch)
            {
                _settings = settings;
                _review = review;
                _batch = batch;
            }

            public static Type TypeOf(string key)
            {
                switch (key)
                {
                    case "review": return typeof(FillReviewPage);
                    case "batch": return typeof(BatchConfirmPage);
                    default: return typeof(SettingsPage);
                }
            }

            public object GetService(Type serviceType)
            {
                if (serviceType == typeof(SettingsPage)) return _settings;
                if (serviceType == typeof(FillReviewPage)) return _review;
                if (serviceType == typeof(BatchConfirmPage)) return _batch;
                return null;
            }
        }
    }
}
