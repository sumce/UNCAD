using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 关于与授权（3.0 WPF 版，D-038，P7 品牌页）：品牌页头 + 概览卡片
    /// （授权 / 产品 / 命令统计 / 公司）、更新记录（与更新日志弹窗同源面板）、
    /// 使用条款。数据仍只来自 AboutInfo / FeatureRegistry / LegalTerms。
    /// </summary>
    public sealed partial class AboutWindow : FluentWindow
    {
        public string Footer => "客户与到期数据来自 pro.key / 在线授权响应，窗体只读展示。";

        private readonly AboutInfo _info;

        public AboutWindow()
        {
            _info = AboutInfo.Current();
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            FillOverview(_info);
            UpdatesPage.Load(VersionChangeLog.Entries);
            TermsText.Text = LegalTerms.FullText;
            NavList.SelectedIndex = 0;
            Loaded += (sender, args) => CloseButton.Focus();
        }

        private void FillOverview(AboutInfo info)
        {
            ProductTitle.Text = info.ProductName;
            ProductSubtitle.Text = info.Subtitle + " · " + info.CompanyName;

            LicenseSnapshot license = info.License;
            AuthorizationStatus.Text = license.StatusText;
            AuthorizationStatus.Foreground = license.IsExpired
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x7B, 0x0F));
            LicenseModeText.Text = ModeText(license.Mode);
            LicenseExpiryText.Text = license.ExpiryText;
            LicenseDaysText.Text = license.DaysRemaining.HasValue
                ? license.DaysRemaining.Value + " 天" : "无期限";
            LicenseeCompanyText.Text = ValueOrDash(info.LicenseeCompany);
            LicenseeNameText.Text = ValueOrDash(info.LicenseeName);
            ExpectedYearsText.Text = info.ExpectedAuthorizationYears.HasValue
                ? info.ExpectedAuthorizationYears.Value + " 年" : "—";
            LicenseFooter.Text = license.IsExpired
                ? "CAD 写入、BOQ 输出和 DWG 导出已停止。"
                : license.Mode == LicenseMode.Perpetual
                    ? "当前版本无到期限制。" : "当前授权在有效期内。";
            if (ProductMetadata.RequiresOnlineLicense)
                LoginButton.Visibility = Visibility.Visible;

            ProductNameText.Text = info.ProductName;
            VersionText.Text = info.Version;
            BuildTimeText.Text = info.BuildTime;
            UpdatedOnText.Text = info.UpdatedOn;
            CustomerCodeText.Text = info.CustomerCode;
            CompanyText.Text = info.CompanyName;

            FeatureCountText.Text = Convert.ToString(FeatureRegistry.Features.Count);
            CommandCountText.Text = Convert.ToString(FeatureRegistry.Features.Sum(
                feature => feature.Commands.Length));
            CommandSummaryText.Text = FeatureRegistry.Summary;

            CompanyNameText.Text = info.CompanyName;
            CopyrightText.Text = info.Copyright;
            WebsiteText.Text = info.Website;
            WebsiteLink.Text = info.WebsiteUrl;
        }

        private void OnNavSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0 || !(e.AddedItems[0] is ListBoxItem item)) return;
            string tag = Convert.ToString(item.Tag);
            OverviewPage.Visibility = tag == "overview" ? Visibility.Visible : Visibility.Collapsed;
            UpdatesPage.Visibility = tag == "updates" ? Visibility.Visible : Visibility.Collapsed;
            TermsPage.Visibility = tag == "terms" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnLogin(object sender, RoutedEventArgs e)
        {
            // v2：授权主体是账号，且一个账号同一时刻只允许一台设备在线。
            // 这里必须走 ClientLoginWindow（WebView2 登录 + 设备绑定 + 令牌写回），
            // 而不是旧的“输入授权码 → 验证并保存”：后者既没有设备标识也没有令牌，
            // 被其它设备顶掉后用户在这个入口无法重新登录抢回。
            var login = new ClientLoginWindow { Owner = this };
            login.ShowDialog();
            if (!login.Succeeded) return;
            Close();
        }

        private void OnWebsiteClick(object sender, MouseButtonEventArgs e)
            => OpenWebsite(_info.WebsiteUrl);

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private static string ValueOrDash(string value)
            => string.IsNullOrWhiteSpace(value) ? "—" : value;

        private static string ModeText(LicenseMode mode)
        {
            switch (mode)
            {
                case LicenseMode.Project: return "项目授权版";
                default: return "正式版";
            }
        }

        private static void OpenWebsite(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn("Open website failed: " + ex.Message);
            }
        }
    }
}
