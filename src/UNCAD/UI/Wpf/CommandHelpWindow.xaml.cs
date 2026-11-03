using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 只读命令参考（3.0 WPF 版，D-038，P5 内容阅读：左侧命令列表 + 右侧说明）。
    /// 帮助内容仍只来自 CommandHelpCatalog，窗体不维护第二份文案；
    /// 搜索沿用旧窗语义：命令/名称/使用方法/功能/注意事项 全字段不区分大小写匹配。
    /// </summary>
    public sealed partial class CommandHelpWindow : FluentWindow
    {
        private readonly IReadOnlyList<CommandHelpEntry> _catalog;

        public CommandHelpWindow(IReadOnlyList<CommandHelpEntry> entries)
        {
            _catalog = entries ?? Array.Empty<CommandHelpEntry>();
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            ApplyFilter("");
            Loaded += (sender, args) => CloseButton.Focus();
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
            => ApplyFilter(SearchBox.Text);

        private void OnCommandSelected(object sender, SelectionChangedEventArgs e)
            => ShowDetail(CommandList.SelectedItem as CommandHelpEntry);

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        internal void ApplyFilter(string query)
        {
            string keyword = (query ?? "").Trim();
            List<CommandHelpEntry> matches = _catalog
                .Where(entry => keyword.Length == 0
                    || Contains(entry.Command, keyword)
                    || Contains(entry.Name, keyword)
                    || Contains(entry.Usage, keyword)
                    || Contains(entry.Function, keyword)
                    || Contains(entry.Notes, keyword))
                .ToList();
            CommandList.ItemsSource = matches;
            Count.Text = keyword.Length == 0
                ? "共 " + _catalog.Count + " 条命令"
                : "匹配 " + matches.Count + " / " + _catalog.Count + " 条命令";
            if (matches.Count > 0 && CommandList.SelectedIndex < 0)
                CommandList.SelectedIndex = 0;
        }

        internal void ShowDetail(CommandHelpEntry entry)
        {
            bool has = entry != null;
            DetailCommand.Text = has ? entry.Command : "";
            DetailName.Text = has ? entry.Name : "";
            DetailUsage.Text = has ? entry.Usage : "";
            DetailFunction.Text = has ? entry.Function : "";
            DetailNotes.Text = has ? entry.Notes : "";
            DetailUsageHeading.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            DetailFunctionHeading.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            DetailNotesHeading.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool Contains(string value, string keyword)
            => (value ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
