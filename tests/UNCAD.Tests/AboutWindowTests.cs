using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 关于窗（D-038，P7 品牌页）：品牌页头 + 授权/产品/命令统计
    /// 卡片，更新记录与使用条款仍可达；数据只来自 AboutInfo/FeatureRegistry/
    /// VersionChangeLog/LegalTerms。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class AboutWindowTests
    {
        [Fact]
        public void Overview_ShowsLicenseProductAndCommandStatsCards()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    AboutInfo info = AboutInfo.Current();
                    var window = new AboutWindow();
                    window.Show();
                    Pump(3);

                    Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                        "Fluent 主题字典必须按窗口合并");
                    Assert.Equal(info.ProductName, window.ProductTitle.Text);
                    Assert.Equal(info.Subtitle + " · " + info.CompanyName,
                        window.ProductSubtitle.Text);

                    // 三个导航页，默认停在概览。
                    Assert.Equal(3, window.NavList.Items.Count);
                    Assert.Equal("概览",
                        ((ListBoxItem)window.NavList.SelectedItem).Content);
                    Assert.Equal(Visibility.Visible, window.OverviewPage.Visibility);
                    Assert.Equal(Visibility.Collapsed, window.UpdatesPage.Visibility);
                    Assert.Equal(Visibility.Collapsed, window.TermsPage.Visibility);

                    // 授权卡片：状态与 AboutInfo 同源。
                    Assert.Equal(info.License.StatusText,
                        window.AuthorizationStatus.Text);
                    Assert.Equal(ModeText(info.License.Mode), window.LicenseModeText.Text);
                    Assert.Equal(info.License.ExpiryText, window.LicenseExpiryText.Text);
                    Assert.Equal(info.Version, window.VersionText.Text);
                    Assert.Equal(info.CustomerCode, window.CustomerCodeText.Text);

                    // 命令统计卡片：与 FeatureRegistry 一致。
                    Assert.Equal(Convert.ToString(FeatureRegistry.Features.Count),
                        window.FeatureCountText.Text);
                    Assert.Equal(Convert.ToString(FeatureRegistry.Features.Sum(
                            feature => feature.Commands.Length)),
                        window.CommandCountText.Text);
                    Assert.Equal(FeatureRegistry.Summary, window.CommandSummaryText.Text);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void UpdatesAndTermsPages_RemainReachableFromNavigation()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new AboutWindow();
                    window.Show();
                    Pump(2);

                    window.NavList.SelectedIndex = 1;
                    Pump(2);
                    Assert.Equal(Visibility.Visible, window.UpdatesPage.Visibility);
                    Assert.Equal(Visibility.Collapsed, window.OverviewPage.Visibility);
                    Assert.Equal(VersionChangeLog.Entries.Count,
                        window.UpdatesPage.VersionList.Items.Count);
                    Assert.Equal(ProductMetadata.VersionLabel,
                        ((VersionChangeLogEntry)window.UpdatesPage.VersionList.Items[0]).Version);
                    Assert.Contains(VersionChangeLog.Entries[0].Changes[0],
                        window.UpdatesPage.DetailChanges.Text);

                    window.NavList.SelectedIndex = 2;
                    Pump(2);
                    Assert.Equal(Visibility.Visible, window.TermsPage.Visibility);
                    Assert.False(string.IsNullOrWhiteSpace(window.TermsText.Text));
                    Assert.Contains("许可", window.TermsText.Text);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static string ModeText(LicenseMode mode)
        {
            switch (mode)
            {
                case LicenseMode.Project: return "项目授权版";
                default: return "正式版";
            }
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }
    }
}
