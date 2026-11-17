using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 进程级共享 WebView2 环境（3.3.6.2 引入，3.3.7.2 按只读审计重构）。
    ///
    /// 机制事实（2026-11-18 审计 R1，纠正 3.3.7.1 的错误假设）：
    /// CoreWebView2Environment.CreateAsync 只创建环境对象——不启动浏览器
    /// 进程、不碰数据目录锁；**锁在控制器创建（EnsureCoreWebView2Async）时
    /// 才生效**。历史会话遗留的孤儿 msedgewebview2 锁着主数据目录时，环境
    /// 创建永远秒回（预热「40ms 完成」的真相=空转），控制器创建则挂死——
    /// 所以逃生口必须开在控制器层，而不是 3.3.7.1 的环境层（结构性死代码）。
    ///
    /// 本类职责：①环境串行复用（SemaphoreSlim——同目录并发 CreateAsync 互相
    /// 等启动锁）；②EnsureControllerAsync 控制器创建超时——超时即判定数据
    /// 目录被僵尸锁住，本会话**标记嫌疑**（后续 GetAsync 直接跳过主目录，
    /// 斩断「超时→重建同目录→再挂」的循环），当前窗口走降级文案（3.3.7.2
    /// 起文案可见：控件致命态会收起 WebView2）。
    /// </summary>
    internal static class SharedWebView2Environment
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static CoreWebView2Environment _cached;
        private static bool _mainDirectorySuspect;
        private static int _attemptIndex;

        /// <summary>取共享环境；首次调用创建（之后即时返回）。</summary>
        internal static async Task<CoreWebView2Environment> GetAsync()
        {
            CoreWebView2Environment cached = _cached;
            if (cached != null) return cached;
            await Gate.WaitAsync();
            try
            {
                if (_cached != null) return _cached;
                string localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                // 主目录嫌疑 → 本会话只用按进程号+序号隔离的独立目录
                // （必起全新浏览器进程，绝不碰僵尸锁）。
                string folder = _mainDirectorySuspect
                    ? Path.Combine(localAppData, "UNCAD", "WebView2-fallback-"
                        + System.Diagnostics.Process.GetCurrentProcess().Id
                        + "-" + Math.Max(_attemptIndex, 1))
                    : Path.Combine(localAppData, "UNCAD", "WebView2");
                Directory.CreateDirectory(folder);
                _cached = await CoreWebView2Environment.CreateAsync(null, folder);
                return _cached;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// 控制器创建（三宿主统一入口，取代各处复制的超时代码）：超时=数据
        /// 目录被僵尸浏览器锁住的信号——标记嫌疑（本会话后续直奔独立目录）、
        /// 作废缓存、抛错由宿主降级。同一个 WebView2 控件不二次 Ensure（首次
        /// 挂死后控件状态未定义，重试交给下一个窗口/预热）。
        /// 超时 30s：开发机实测冷控制器 33.5s 属极端——多数冷启动 6~10s，
        /// 预热常驻后正常路径永远热；30s 是「僵尸锁」与「极慢冷启」的折中。
        /// </summary>
        internal static async Task EnsureControllerAsync(WebView2 webView,
            int timeoutSeconds = 30)
        {
            CoreWebView2Environment environment = await GetAsync();
            Task init = webView.EnsureCoreWebView2Async(environment);
            Task finished = await Task.WhenAny(init,
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
            if (finished == init)
            {
                await init;
                return;
            }
            _cached = null;
            _mainDirectorySuspect = true;
            _attemptIndex++;
            Log.Warn("[3D] WebView2 控制器创建超时(" + timeoutSeconds
                + "s)——数据目录疑似被僵尸浏览器锁住；本会话后续预览将自动"
                + "改用独立目录。");
            throw new InvalidOperationException(
                "WebView2 控制器创建超时（" + timeoutSeconds + " 秒）；数据目录"
                + "疑似被历史进程锁住，本会话下一次打开预览将自动换独立目录。");
        }

        /// <summary>渲染进程崩溃后作废缓存，下一次使用方重建（不碰活窗口）。</summary>
        internal static void Invalidate()
        {
            _cached = null;
        }
    }
}
