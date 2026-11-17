using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 进程级共享 WebView2 环境（3.3.6.2，D-054 Amendment 3.3.5 的生命周期
    /// 修正）：U1U 预览/U1L 3D/登录页此前各自开窗各自 CreateAsync，且共用
    /// %LOCALAPPDATA%\UNCAD\WebView2——上一轮窗口关闭后浏览器进程仍在退场
    /// 并锁着用户数据目录，新一轮 CreateAsync 排队等锁再拉新进程。现场实测
    /// （2026-11-18 日志）「页面目录→内核就绪」6~10 秒，用户在审阅窗点确认
    /// （5~10 秒）之前 3D 永远起不来；同一进程存活期间复用则 0.5 秒。
    /// 环境与浏览器进程在首个使用方创建后随静态引用常驻整个 CAD 会话；
    /// Bootstrap 启动 Idle 预热让首次 U1U 也吃到热进程。渲染进程崩溃由
    /// 使用方 Invalidate 后下次重建（当前窗口走既有降级文案口径）。
    /// </summary>
    internal static class SharedWebView2Environment
    {
        private static CoreWebView2Environment _cached;

        /// <summary>取共享环境；首次调用创建（之后即时返回）。</summary>
        internal static async Task<CoreWebView2Environment> GetAsync()
        {
            CoreWebView2Environment cached = _cached;
            if (cached != null) return cached;
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "UNCAD", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            CoreWebView2Environment created =
                await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            // 并发兜底：两个窗口同时冷启动时两个环境句柄连的是同一浏览器
            // 进程（同数据目录），留一个常驻即可。
            if (_cached == null) _cached = created;
            return _cached;
        }

        /// <summary>渲染进程崩溃后作废缓存，下一次使用方重建（本方法不碰活窗口）。</summary>
        internal static void Invalidate()
        {
            _cached = null;
        }

        /// <summary>
        /// 启动 Idle 预热（Bootstrap.WarmUp 调用）：后台拉起共享环境与浏览器
        /// 进程，首次 U1U/U1L 开窗即 0.5 秒级就绪。火后不管，失败只记日志——
        /// 预热是优化不是正确性（未预热时首个使用方照常自建）。
        /// </summary>
        internal static void WarmUpForStartup()
        {
            _ = WarmUpCoreAsync();
        }

        private static async Task WarmUpCoreAsync()
        {
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                await GetAsync();
                Log.Info("[3D] WebView2 内核启动预热完成: "
                    + clock.ElapsedMilliseconds + " ms");
            }
            catch (Exception ex)
            {
                Log.Warn("[3D] WebView2 内核启动预热失败(首次使用时将重试): "
                    + ex.Message);
            }
        }
    }
}
