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
        // 3.3.7.1 现场修复：创建必须串行——同数据目录并发 CreateAsync 会互相
        // 等浏览器进程启动锁（启动预热 + 首个窗口冷启动竞态即挂死）。
        private static readonly System.Threading.SemaphoreSlim Gate =
            new System.Threading.SemaphoreSlim(1, 1);
        private static CoreWebView2Environment _cached;

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
                // 主目录可能被历史会话的孤儿浏览器进程占着僵尸锁：环境创建
                // 秒回（attach 旧进程）但控制器创建永远挂死（2026-11-18 现场
                // 05:15 两次 U1U 全挂、确认窗被拖白）。15s 超时后改用按进程号
                // 隔离的备用目录——独立目录必起全新浏览器进程，绝不碰僵尸。
                _cached = await CreateWithTimeoutAsync(
                    Path.Combine(localAppData, "UNCAD", "WebView2"));
                if (_cached == null)
                {
                    Log.Warn("[3D] WebView2 主数据目录无响应（疑似孤儿进程占用），"
                        + "切换本进程独立备用目录重试。");
                    _cached = await CreateWithTimeoutAsync(Path.Combine(
                        localAppData, "UNCAD",
                        "WebView2-fallback-" + System.Diagnostics.Process
                            .GetCurrentProcess().Id));
                }
                if (_cached == null)
                    throw new InvalidOperationException(
                        "WebView2 环境创建超时（主/备用目录均无响应）。");
                return _cached;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static async Task<CoreWebView2Environment> CreateWithTimeoutAsync(
            string userDataFolder)
        {
            Directory.CreateDirectory(userDataFolder);
            Task<CoreWebView2Environment> create =
                CoreWebView2Environment.CreateAsync(null, userDataFolder);
            Task done = await Task.WhenAny(create,
                Task.Delay(TimeSpan.FromSeconds(15)));
            if (done != create) return null;
            return create.Status == TaskStatus.RanToCompletion
                ? create.Result : null;
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
