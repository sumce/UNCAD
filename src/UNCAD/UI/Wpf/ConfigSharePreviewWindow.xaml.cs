using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 配置分享码预览窗（3.0 WPF 版，D-038，P3 事务确认）：展示来源产品/版本/生成
    /// 时间与“将变更”的配置项。只有用户点“导入”才写配置；取消不改变本机任何设置。
    /// 导入动作本身仍在 SettingsWindow 里执行（ConfigShareService.Import）。
    /// </summary>
    public sealed partial class ConfigSharePreviewWindow : FluentWindow
    {
        /// <summary>用户点了“导入”为 true；取消/关闭/Esc 一律 false。</summary>
        public bool ImportConfirmed { get; private set; }

        public ConfigSharePreviewWindow(ConfigSharePreview preview)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);

            SourceText.Text = preview.Product + " " + preview.Version;
            CreatedText.Text = preview.CreatedUtc.LocalDateTime.ToString(
                "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            ItemsCountText.Text = preview.Items.Count + " 项";
            FormatText.Text = "分享码 v" + preview.FormatVersion;

            ImportButton.Content = "导入 " + preview.Changes.Count + " 项";
            var rows = preview.Changes
                .Select(change => new ConfigShareRow(change.Key,
                    change.CurrentValue.Length == 0 ? "（未设置）" : change.CurrentValue,
                    change.SharedValue))
                .ToList();
            ConfigShareChanges.ItemsSource = rows;
            bool hasChanges = rows.Count > 0;
            ConfigShareChanges.Visibility = hasChanges
                ? Visibility.Visible : Visibility.Collapsed;
            EmptyPanel.Visibility = hasChanges
                ? Visibility.Collapsed : Visibility.Visible;
            if (preview.Items.Count == 0)
            {
                // 没有可应用项时不允许“导入”，但仍然允许关闭窗口。
                ImportButton.Visibility = Visibility.Collapsed;
            }
            // 默认焦点在“取消”，避免回车误触导入。
            Loaded += (sender, args) => CancelButton.Focus();
        }

        private void OnImport(object sender, RoutedEventArgs e)
        {
            ImportConfirmed = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            ImportConfirmed = false;
            Close();
        }
    }

    /// <summary>变更列表行（只读字符串，空当前值显示“（未设置）”）。</summary>
    internal sealed class ConfigShareRow
    {
        public ConfigShareRow(string key, string current, string shared)
        {
            Key = key ?? "";
            Current = current ?? "";
            Shared = shared ?? "";
        }

        public string Key { get; }
        public string Current { get; }
        public string Shared { get; }
    }
}
