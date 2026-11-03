using System;
using System.Linq;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace UNCAD.UI
{
    /// <summary>
    /// 插件内 WPF 窗体的统一主题入口（D-038）。
    ///
    /// AutoCAD 进程里的 <c>Application.Current</c> 属于 CAD 自己（Ribbon 就是
    /// WPF），WPF-UI 的主题/控件字典**只能按窗口合并**，绝不能进应用级资源，
    /// 否则等于改 CAD 的界面。所有 3.0 WPF 窗体必须在构造后调用本类。
    /// </summary>
    internal static class UiShell
    {
        /// <summary>窗口尺寸最多占工作区的比例（自适应：小屏/低分屏不被截断）。</summary>
        private const double WorkAreaCoverage = 0.92;

        /// <summary>按窗口合并 Fluent 主题、控件样式与 UiKit 设计令牌；重复调用以
        /// 最新主题为准（换主题 = 移除旧 ThemesDictionary 再挂新的，外观页切换
        /// 全靠它）。合并后把窗口尺寸钳制到工作区内并居中。</summary>
        internal static void ApplyFluentTheme(Window window, ApplicationTheme theme)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            var stale = window.Resources.MergedDictionaries
                .OfType<ThemesDictionary>().ToList();
            foreach (var dictionary in stale)
            {
                window.Resources.MergedDictionaries.Remove(dictionary);
            }
            window.Resources.MergedDictionaries.Add(
                new ThemesDictionary { Theme = theme });
            if (!window.Resources.MergedDictionaries.OfType<ControlsDictionary>().Any())
            {
                window.Resources.MergedDictionaries.Add(new ControlsDictionary());
            }
            if (!window.Resources.MergedDictionaries.OfType<FluentKit>().Any())
            {
                window.Resources.MergedDictionaries.Add(new FluentKit());
            }
            AdaptWindowToWorkArea(window);
        }

        /// <summary>默认浅色（跟随 U1SET 外观页之前的最安全起点）。</summary>
        internal static void ApplyFluentTheme(Window window)
            => ApplyFluentTheme(window, ApplicationTheme.Light);

        /// <summary>
        /// 自适应（2026-11-03）：窗口尺寸钳制到工作区 92%（下限 MinWidth/MinHeight），
        /// 未显式定位的窗口居中启动。显式设置过 Left/Top 的窗口（如记忆位置的窗）
        /// 不挪动；异常环境（无桌面会话）静默跳过。
        /// </summary>
        private static void AdaptWindowToWorkArea(Window window)
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                if (workArea.Width <= 0 || workArea.Height <= 0) return;
                double maxWidth = Math.Max(window.MinWidth, workArea.Width * WorkAreaCoverage);
                double maxHeight = Math.Max(window.MinHeight, workArea.Height * WorkAreaCoverage);
                // SizeToContent 交给内容定尺寸的边不要碰：显式赋 Width/Height 会把它
                // 打回 Manual，窗口从此不再随内容伸缩。
                bool sizesToWidth = window.SizeToContent == SizeToContent.Width
                    || window.SizeToContent == SizeToContent.WidthAndHeight;
                bool sizesToHeight = window.SizeToContent == SizeToContent.Height
                    || window.SizeToContent == SizeToContent.WidthAndHeight;
                if (!sizesToWidth && (double.IsNaN(window.Width) || window.Width > maxWidth))
                    window.Width = maxWidth;
                if (!sizesToHeight && (double.IsNaN(window.Height) || window.Height > maxHeight))
                    window.Height = maxHeight;
                if (window.WindowStartupLocation == WindowStartupLocation.Manual
                    && (double.IsNaN(window.Left) || double.IsNaN(window.Top)))
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            catch (Exception)
            {
                // 无桌面/异常宿主下保持设计器尺寸即可，不做自适应。
            }
        }
    }
}
