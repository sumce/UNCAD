using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 版本更新日志面板（3.0 WPF 版，D-038，P5 内容阅读）：左侧版本列表 +
    /// 右侧该版本详情。更新日志弹窗与“关于”的更新记录页共用，文案只来自
    /// <see cref="VersionChangeLogEntry"/>，与旧 UpdateNotesForm.CreateVersionPages 同源。
    /// </summary>
    public sealed partial class VersionNotesView : UserControl
    {
        private IReadOnlyList<VersionChangeLogEntry> _entries;

        public VersionNotesView()
        {
            InitializeComponent();
        }

        public VersionNotesView(IReadOnlyList<VersionChangeLogEntry> entries) : this()
        {
            Load(entries);
        }

        /// <summary>加载版本列表并默认选中第一个（最新）版本。</summary>
        public void Load(IReadOnlyList<VersionChangeLogEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("更新日志内容为空。", nameof(entries));
            _entries = entries;
            VersionList.ItemsSource = entries;
            VersionList.SelectedIndex = 0;
            ShowVersion(entries[0]);
        }

        /// <summary>当前选中的版本；未加载时为 null。</summary>
        public VersionChangeLogEntry SelectedVersion => VersionList.SelectedItem
            as VersionChangeLogEntry;

        private void OnVersionSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is VersionChangeLogEntry entry)
                ShowVersion(entry);
        }

        private void ShowVersion(VersionChangeLogEntry entry)
        {
            DetailVersion.Text = "版本 " + entry.Version;
            DetailDate.Text = "发布日期 " + entry.DateUtc + " · "
                + entry.Changes.Length + " 项更新";
            DetailChanges.Text = string.Join(Environment.NewLine,
                entry.Changes.Select(change => "· " + change));
        }
    }
}
