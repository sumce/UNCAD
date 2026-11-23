using System;
using System.Globalization;
using System.IO;
using System.Windows;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    public sealed partial class XLayoutFailureWindow : FluentWindow
    {
        public string Heading { get; }
        public string Details { get; }
        public string LogPath { get; }

        public XLayoutFailureWindow(string heading, string details)
        {
            Heading = heading ?? "XLAYOUT 执行失败";
            Details = details ?? "未提供诊断详情。";
            string directory = FileLogger.Instance.LogDirectory;
            LogPath = string.IsNullOrWhiteSpace(directory)
                ? "本机文件日志路径当前不可用"
                : Path.Combine(directory, "uncad-"
                    + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                    + ".log");

            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            DataContext = this;
        }

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(Details);
            }
            catch (Exception ex)
            {
                Log.Error("复制 XLAYOUT 诊断失败", ex);
            }
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
