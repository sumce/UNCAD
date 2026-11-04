using System.Collections.Generic;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 升级后首次打开 CAD 时展示的版本更新日志（3.0 WPF 版，D-038，P5 内容阅读）。
    /// 版本列表与详情由共用的 VersionNotesView 呈现；“关于”的更新记录页同源。
    /// </summary>
    public sealed partial class UpdateNotesWindow : FluentWindow
    {
        public string Heading => "已更新到 " + ProductMetadata.VersionText;

        public UpdateNotesWindow(IReadOnlyList<VersionChangeLogEntry> entries)
        {
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            Notes.Load(entries);
            Loaded += (sender, args) => CloseButton.Focus();
        }

        private void OnClose(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
