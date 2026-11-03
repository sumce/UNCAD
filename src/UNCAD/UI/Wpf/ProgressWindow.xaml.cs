using System;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 长任务进度窗（2026-11-02 大批量响应性）：千/万级图框的批处理必须让用户
    /// 看到进展，否则 CAD 主线程被命令占满、界面“未响应”，最后一次性蹦完。
    ///
    /// 用法（命令主线程）：new ProgressWindow(title) → Show() → 循环里
    /// Update(status, current, total) → Close()。Update 内部在 Render 优先级
    /// 泵一次调度器，让本窗体在同步命令循环里也能即时重绘；窗体无交互控件，
    /// 不接受输入，不产生命令重入。
    /// </summary>
    public sealed partial class ProgressWindow : FluentWindow
    {
        public ProgressWindow()
        {
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
        }

        public ProgressWindow(string title) : this()
        {
            TitleText.Text = title ?? "处理进度";
            Title = title ?? "处理进度";
        }

        /// <summary>更新进度并立即重绘（同线程安全；跨线程自动调度）。</summary>
        public void Update(string status, int current, int total)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Update(status, current, total));
                return;
            }
            Bar.IsIndeterminate = false;
            StatusText.Text = status ?? "";
            double safeTotal = Math.Max(1, total);
            Bar.Maximum = safeTotal;
            Bar.Value = Math.Min(current, safeTotal);
            CountText.Text = current + " / " + total;
            // 同步命令循环里 WPF 不会自行重绘：在 Render 优先级上泵一次，
            // 强制处理挂起的布局与渲染消息。
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        }

        /// <summary>不确定态（总量未知/由 AutoCAD 内部主导的阶段，如事务提交）。</summary>
        public void SetIndeterminate(string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetIndeterminate(status));
                return;
            }
            Bar.IsIndeterminate = true;
            StatusText.Text = status ?? "";
            CountText.Text = "";
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        }
    }
}
