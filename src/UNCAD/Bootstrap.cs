using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Infra;
using UNCAD.UI;

[assembly: ExtensionApplication(typeof(UNCAD.Bootstrap))]

namespace UNCAD
{
    /// <summary>插件启动：预热 WinForms IME 表、扫描 Feature 注册表、构建 Ribbon、输出横幅。</summary>
    public class Bootstrap : IExtensionApplication
    {
        private static bool _licenseIdleAttached;
        private static bool _licenseDialogOpen;

        public void Initialize()
        {
            WarmUpWinFormsImeConversion();
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                .MdiActiveDocument;
            doc?.Editor.WriteMessage(
                "\n[UNCAD] " + Branding.Nameplate + " | 已加载：" + FeatureRegistry.Summary);
            RibbonBuilder.Build();
            // D-033：云端上传/台账上报保持移除。D-041：网络机台数据下载恢复，
            // 但只在 U1SET"刷新"或 U1DATA 时显式触发，启动时不自动刷新。
            if (SupportsStartupUi())
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnStartupSplashIdle;
                _startupSplashIdleAttached = true;
            }
            if (ProductMetadata.RequiresOnlineLicense)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnLicenseIdle;
                _licenseIdleAttached = true;
                OnlineLicenseMonitor.Start();
            }
            Log.Info("UNCAD initialized; Ribbon registration requested");
        }

        private static bool _startupSplashIdleAttached;
        private static bool _startupSplashShown;

        /// <summary>
        /// AutoCAD 的 CMDACTIVE 非 0 表示有命令正在执行（1=命令、2=选择对象、4=脚本…）。
        /// 读不到系统变量时按"进行中"处理：宁可推迟提示，也绝不打断用户输入。
        /// </summary>
        private static bool IsCommandActive()
        {
            try
            {
                object value = Autodesk.AutoCAD.ApplicationServices.Application
                    .GetSystemVariable("CMDACTIVE");
                return Convert.ToInt32(value) != 0;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// CAD 界面就绪后(Idle)展示一次全屏品牌动画。U1SET 可关闭;Core Console、
        /// 后续文档切换不重复展示。更新日志弹窗在动画之后触发。
        /// </summary>
        private static void OnStartupSplashIdle(object sender, EventArgs args)
        {
            if (_startupSplashShown) return;
            var documentManager = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager;
            if (documentManager == null || documentManager.MdiActiveDocument == null) return;
            _startupSplashShown = true;
            DetachStartupSplashIdle();
            try
            {
                if (!Settings.GetBool(ConfigKeys.StartupSplashEnabled, true))
                {
                    Log.Info("启动动画已通过设置关闭");
                    return;
                }
                IntPtr handle = Autodesk.AutoCAD.ApplicationServices.Application.MainWindow?.Handle
                    ?? IntPtr.Zero;
                Rectangle bounds = handle == IntPtr.Zero
                    ? Screen.PrimaryScreen.Bounds : Screen.FromHandle(handle).Bounds;
                var splash = new StartupSplashWindow(bounds);
                // 3.0 WPF 版（D-038）：原 ShowModalDialog 是模态展示（动画期间
                // CAD 输入被挡住，更新日志在动画结束后才弹），ShowModalWindow 是
                // 它的 WPF 等价物 —— 只换皮，不改任何时序。
                UNCAD.UI.ModalWindowHost.Show(splash);
            }
            catch (System.Exception ex)
            {
                // 动画失败绝不能阻塞 CAD 使用。
                Log.Warn("启动动画展示失败: " + ex.Message);
            }
            finally
            {
                // 在当前 Idle 回调中才订阅，确保更新日志最早在下一次 Idle 展示。
                AttachUpdateNotesIdle();
            }
        }

        private static void DetachStartupSplashIdle()
        {
            if (!_startupSplashIdleAttached) return;
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnStartupSplashIdle;
            _startupSplashIdleAttached = false;
        }

        private static bool _updateNotesIdleAttached;

        private static void AttachUpdateNotesIdle()
        {
            if (_updateNotesIdleAttached || !SupportsStartupUi()) return;
            Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnUpdateNotesIdle;
            _updateNotesIdleAttached = true;
        }

        /// <summary>
        /// 更新到新版本后第一次打开 CAD 时展示版本更新日志(Idle 触发,不阻塞加载)。
        /// Core Console 没有 UI 消息循环,弹窗前先确认有活动文档和主窗口。
        /// </summary>
        private static void OnUpdateNotesIdle(object sender, EventArgs args)
        {
            try
            {
                if (!SupportsStartupUi())
                {
                    DetachUpdateNotesIdle();
                    return;
                }
                if (_updateNotesDialogOpen) return;
                string seen = Settings.Get(ConfigKeys.UpdateNotesSeenVersion, "").Trim();
                // 3.2.0.6（审计修复）：已看过当前版本也要退订——此前该分支裸
                // return，Idle 回调常驻订阅，之后每次 AutoCAD 空闲都白读一次
                // 注册表（与"条目为空"分支的退订语义不一致）。
                if (string.Equals(seen, ProductMetadata.VersionText, StringComparison.Ordinal))
                {
                    DetachUpdateNotesIdle();
                    return;
                }
                List<VersionChangeLogEntry> entries =
                    VersionChangeLog.EntriesNewerThan(seen);
                if (entries.Count == 0)
                {
                    Settings.Set(ConfigKeys.UpdateNotesSeenVersion, ProductMetadata.VersionText);
                    DetachUpdateNotesIdle();
                    return;
                }
                var application = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager;
                if (application == null || application.MdiActiveDocument == null) return;
                _updateNotesDialogOpen = true;
                try
                {
                    // 3.0 WPF 版（D-038，P5 内容阅读）；无返回结果，看完即关。
                    var window = new UpdateNotesWindow(entries);
                    UNCAD.UI.ModalWindowHost.Show(window);
                }
                finally { _updateNotesDialogOpen = false; }
                Settings.Set(ConfigKeys.UpdateNotesSeenVersion, ProductMetadata.VersionText);
                DetachUpdateNotesIdle();
            }
            catch (System.Exception ex)
            {
                Log.Warn("版本更新日志展示失败: " + ex.Message);
                DetachUpdateNotesIdle();
            }
        }

        private static bool _updateNotesDialogOpen;

        private static void DetachUpdateNotesIdle()
        {
            if (!_updateNotesIdleAttached) return;
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnUpdateNotesIdle;
            _updateNotesIdleAttached = false;
        }

        /// <summary>
        /// .NET Framework 的 ImeModeConversion 惰性初始化不是线程安全的：两个线程同时创建
        /// WinForms 控件（例如本插件对话框与 AutoCAD 自身 UI）时，同一字典被插入两次，
        /// 抛出“已添加了具有相同键的项”。加载时在主线程提前初始化一次即可消除竞争窗口。
        /// </summary>
        private static void WarmUpWinFormsImeConversion()
        {
            try
            {
                Type ime = typeof(Control).Assembly.GetType(
                    "System.Windows.Forms.ImeModeConversion");
                if (ime == null) return;
                const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
                object table = ime.GetProperty("ImeModeConversion", flags)
                        ?.GetValue(null, null)
                    ?? ime.GetField("ImeModeConversion", flags)?.GetValue(null);
                Log.Info("WinForms IME conversion warmup "
                    + (table != null ? "ok" : "skipped"));
            }
            catch (System.Exception ex)
            {
                Log.Warn("WinForms IME warmup failed: " + ex.Message);
            }
        }

        private static int _warmedUp;

        private static bool SupportsStartupUi()
            => StartupSplashWindow.SupportsHost(
                System.Diagnostics.Process.GetCurrentProcess().ProcessName,
                Environment.UserInteractive);

        /// <summary>
        /// 启动空闲时预热:第一次 U1F/U1U 等命令的"卡"主要来自一次性成本
        /// (内嵌固定清单解析、规划/Excel 模块 JIT)。挪到 Idle 执行,
        /// 不阻塞加载,第一条命令即受益。
        /// </summary>
        private static void WarmUp()
        {
            if (System.Threading.Interlocked.Exchange(ref _warmedUp, 1) != 0) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var machine = new UNCAD.Core.Excel.MachineRow();
                UNCAD.Core.Fill.TableGenerationModule.Plan(
                    new UNCAD.Core.Fill.TableGenerationRequest(machine,
                        new UNCAD.Core.Excel.BoqCatalogIndex(null),
                        new UNCAD.Core.Stat.CableStatResult(),
                        UNCAD.Core.Fill.FillPlanningOptions.Default));
                clock.Stop();
                Log.Info("启动预热完成: " + clock.ElapsedMilliseconds + " ms");
                // 3.3.7.2（审计 R3）：全量预热——环境级预热(3.3.6.2)是空转
                // （CreateAsync 不拉浏览器，锁在控制器创建才生效）。改为：
                // 清扫历史孤儿浏览器 + 屏幕外常驻真页面（环境+控制器+
                // three.js+握手全链路），首个真实审阅窗 0.5 秒级。
                UNCAD.UI.OrbitPrewarmHost.WarmUp();
            }
            catch (System.Exception ex)
            {
                clock.Stop();
                Log.Warn("启动预热失败(" + clock.ElapsedMilliseconds + " ms): " + ex.Message);
            }
        }

        private static void OnLicenseIdle(object sender, EventArgs args)
        {
            WarmUp();
            if (!SupportsStartupUi() || _startupSplashIdleAttached) return;
            if (_licenseDialogOpen) return;

            var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                ?.MdiActiveDocument;
            // 没有活动文档时先不取，避免把提示从队列里取走却没法显示（提示会丢）
            if (document == null) return;

            if (OnlineLicenseMonitor.TryTakeActivationRequest(out string reason))
            {
                try
                {
                    _licenseDialogOpen = true;
                    // v2：激活/被顶掉必须走 ClientLoginWindow（WebView2 登录 → 设备绑定 → 令牌写回），
                    // 不能再用 OnlineLicenseWindow（只输入账号 + 验证并保存，既没有设备标识也没有令牌，
                    // 被其它设备顶掉的用户在这个入口永远无法重新上线）。
                    //
                    // 只有**用户主动操作**（执行被停用的 UNCAD 命令，或运行 UNC_LOGIN）才会走到这里，
                    // 因此这个模态窗口不会打断用户正在做的绘图工作；
                    // 周期性心跳不再触发它（见 OnlineLicenseMonitor.Publish 的说明）。
                    _ = reason;
                    var window = new ClientLoginWindow();
                    UNCAD.UI.ModalWindowHost.Show(window);
                }
                finally { _licenseDialogOpen = false; }
                return;
            }

            if (!OnlineLicenseMonitor.TryTakeNotice(out OnlineLicenseNotice notice)) return;

            // **不要用 MessageBox**：它是应用程序模态的，弹出期间用户无法使用 AutoCAD。
            // 授权问题只应停用 UNCAD 命令，不能连 CAD 本身一起挡住；
            // 因此改成写到命令行（非阻塞），用户随时能看到，也不会被打断。
            string title = string.IsNullOrWhiteSpace(notice.Title) ? "账号授权" : notice.Title;
            string level = notice.Level == "error" ? "错误"
                : notice.Level == "info" ? "提示" : "提醒";
            document.Editor.WriteMessage("\n[" + title + "·" + level + "] " + notice.Message
                + "\n（此提示不影响 AutoCAD 使用；如需登录或注册账号，请运行 UNC_LOGIN）");
        }

        public void Terminate()
        {
            RibbonBuilder.Terminate();
            if (_licenseIdleAttached)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnLicenseIdle;
                _licenseIdleAttached = false;
            }
            DetachStartupSplashIdle();
            DetachUpdateNotesIdle();
            OnlineLicenseMonitor.Stop();
        }
    }
}
