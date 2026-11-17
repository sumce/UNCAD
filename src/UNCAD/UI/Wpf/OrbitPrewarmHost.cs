using System;
using System.Windows;
using UNCAD.Core.QuickLine;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 3D 预览**全量预热**（3.3.7.2，只读审计 R3）：环境级预热（3.3.6.2）
    /// 一直是空转——CreateAsync 只建环境对象，浏览器进程只在存在 ≥1 个
    /// 活动控制器时才存活；每个预览窗关闭都 dispose 控制器 → 浏览器退场
    /// → 用户的每次 U1U 都在付冷启动（本机实测控制器冷创建 33.5s）。
    ///
    /// 本宿主在 CAD 启动 Idle 做两件事：
    /// ①清扫历史孤儿——命令行指向我们的 WebView2 数据目录、且浏览器组
    /// 的根宿主已死的 msedgewebview2 组（上次会话被强杀留下的僵尸，锁
    /// 数据目录正是控制器挂死的根因；根宿主活着的组=别的 CAD 会话在用，
    /// 绝不碰）；
    /// ②屏幕外隐藏小窗里跑**真页面**（环境+控制器+three.js+ready 握手全
    /// 链路）并常驻整个会话（静态引用保活，进程退出随之回收）——浏览器
    /// 从此整会话热着，首个真实 U1U 审阅窗 0.5 秒级。窗口 1px 移出屏幕、
    /// 不进任务栏、不抢焦点；WebView2 绝不 Collapsed（3.3.6.1 白屏教训）。
    /// 失败只记日志：预热是优化不是正确性（未预热时首个使用方照常自建，
    /// 走 EnsureControllerAsync 的超时/换目录保护）。
    /// </summary>
    internal static class OrbitPrewarmHost
    {
        private static Window _window;
        private static QuickLineWebPreviewControl _control;

        /// <summary>火后不管；失败只记日志。</summary>
        internal static void WarmUp()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke((Action)WarmUp);
                return;
            }
            if (_window != null) return;
            try
            {
                SweepOrphanBrowsers();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                _control = new QuickLineWebPreviewControl
                {
                    Width = 160,
                    Height = 100
                };
                _window = new Window
                {
                    Title = "uncad-3d-prewarm",
                    Content = _control,
                    Width = 180,
                    Height = 120,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Topmost = false,
                    // 移出屏幕而非隐藏——WebView2 控件必须真经历 Loaded。
                    Left = -20000,
                    Top = -20000
                };
                _window.Show();
                // 迷你场景走真链路（SetModel→信封→页面→ready 握手→
                // 「[U1U] 3D 预览页面就绪」日志锚点即预热完成的判据）。
                _control.SetModel(new QuickLineOrbitPreviewModel(new[]
                {
                    new QuickLineOrbitPreviewSegment("prewarm",
                        new QuickLineSpatialPoint(0, 0, 0),
                        new QuickLineSpatialPoint(4800, 0, 0),
                        QuickLineSpatialAxis.X, 4800, false)
                }, "预热", null));
                Log.Info("[3D] 预热窗已建: " + clock.ElapsedMilliseconds
                    + " ms（页面就绪后浏览器整会话常驻，首个 U1U 预览 0.5 秒级）");
            }
            catch (Exception ex)
            {
                Log.Warn("[3D] 预热窗建立失败(首个 U1U 将自行冷启动): "
                    + ex.Message);
            }
        }

        /// <summary>
        /// 孤儿判定：候选=命令行 user-data-dir 指向我们数据目录的
        /// msedgewebview2；沿父链上溯过整串 webview 祖先——根宿主（第一个
        /// 非 webview 祖先）已死 ⇒ 整组是上次会话遗留的僵尸（锁数据目录），
        /// taskkill /T 连树杀组根；根宿主活着（本会话/别的 CAD 会话）⇒ 跳过。
        /// </summary>
        private static void SweepOrphanBrowsers()
        {
            try
            {
                int swept = 0;
                var candidates = new System.Collections.Generic.List<int>();
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process "
                    + "WHERE Name='msedgewebview2.exe'"))
                {
                    foreach (System.Management.ManagementBaseObject item
                        in searcher.Get())
                    {
                        string commandLine = Convert.ToString(item["CommandLine"]);
                        if (commandLine == null
                            || commandLine.IndexOf(@"\UNCAD\WebView2",
                                StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        candidates.Add(Convert.ToInt32(item["ProcessId"]));
                    }
                }
                foreach (int pid in candidates)
                {
                    int groupRoot = FindOrphanGroupRoot(pid);
                    if (groupRoot <= 0) continue;
                    // /T 连子进程整树终结——数据目录锁随组根释放。
                    var killer = System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = "/PID " + groupRoot + " /T /F",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    killer?.WaitForExit(5000);
                    swept++;
                }
                if (swept > 0)
                    Log.Info("[3D] 启动清扫孤儿 WebView2 组: " + swept);
            }
            catch (Exception ex)
            {
                Log.Warn("[3D] 孤儿清扫失败(不影响启动): " + ex.Message);
            }
        }

        /// <summary>
        /// 从 webview 进程沿父链上溯：全是 webview 且尽头宿主已死 → 返回该组
        /// 根（最顶层 webview）的 PID；宿主活着或链不完整 → 0（不碰）。
        /// </summary>
        private static int FindOrphanGroupRoot(int pid)
        {
            try
            {
                int current = pid;
                int root = pid;
                for (int hop = 0; hop < 16; hop++)
                {
                    int parent = ParentProcessId(current);
                    if (parent <= 0) return root;
                    string parentName = ProcessName(parent);
                    if (parentName == null) return root; // 宿主已死：整组孤儿
                    if (string.Equals(parentName, "msedgewebview2",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        current = parent;
                        root = parent;
                        continue;
                    }
                    return 0; // 宿主活着（acad/测试宿主/别的会话）：在用
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static int ParentProcessId(int pid)
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId="
                    + pid))
                {
                    foreach (System.Management.ManagementBaseObject item
                        in searcher.Get())
                        return Convert.ToInt32(item["ParentProcessId"]);
                }
            }
            catch { }
            return -1;
        }

        private static string ProcessName(int pid)
        {
            try
            {
                return System.Diagnostics.Process.GetProcessById(pid).ProcessName;
            }
            catch
            {
                return null;
            }
        }
    }
}
