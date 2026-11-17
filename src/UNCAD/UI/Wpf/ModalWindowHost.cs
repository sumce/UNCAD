using System;
using System.Windows;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 模态窗宿主（3.3.6.2）：全部模态窗统一从这里弹出，收口一个 WPF-UI
    /// 4.0.2 已知竞态——TitleBar.OnWindowContentRendered 里的
    /// HwndSource.CriticalFromHwnd：ContentRendered 是投递到调度器队列的
    /// 回调，窗口在它执行前被关闭（现场实测审阅窗 0.4 秒内被关）时句柄已
    /// 归零，CriticalFromHwnd(0) 抛 ArgumentException 并从 ShowDialog 一路
    /// 炸穿，把整条命令变成「U1U failed」（2026-11-18 日志全库唯一一例，
    /// 出现在用户快速连跑 U1U 期间）。竞态成立时窗口必然已被用户关闭：
    /// 按取消返回（调用方读 Confirmed=false 走既有取消路径），不失败命令。
    /// </summary>
    internal static class ModalWindowHost
    {
        internal static void Show(Window window)
        {
            try
            {
                Autodesk.AutoCAD.ApplicationServices.Application
                    .ShowModalWindow(window);
            }
            catch (ArgumentException ex) when (IsTitleBarFastCloseRace(ex))
            {
                Log.Warn("模态窗快关渲染竞态（窗口已关，按取消处理）: "
                    + ex.Message);
            }
        }

        /// <summary>竞态指纹：ArgumentException 且堆栈来自 TitleBar 的
        /// OnWindowContentRendered；其余异常照常上抛不吞。</summary>
        internal static bool IsTitleBarFastCloseRace(ArgumentException ex)
        {
            return ex != null && ex.StackTrace != null
                && ex.StackTrace.IndexOf("OnWindowContentRendered",
                    StringComparison.Ordinal) >= 0;
        }
    }
}
