using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.QuickLine;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.3.0（D-054 特性③）STA 实窗测试、3.3.5（D-054 Amendment）随渲染内核
    /// 换 WebView2+three.js 调整断言口径：窗口宿主存在、模型数据正确传入、
    /// 布局回退保持。**不在 xunit 里断言真实 3D 渲染结果**——对控件的断言
    /// 全部是数据级：SetModel 同步构建的信封计数（柱网/标签/通道在 Core 侧
    /// 算死，与 WebView2 初始化、GPU 光栅化完全解耦；测试进程关闭
    /// QuickLineWebPreviewControl.DefaultAutoInitialize，页面永不加载）。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class FillOrbitPreviewWindowTests
    {
        [Fact]
        public void FillReviewWindow_ShowsPreviewAndLayerTextOnlyWhenProvided()
        {
            WpfStaRunner.Run(delegate
            {
                    var data = FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A" },
                        new List<TableFillRow>());
                    var plain = new FillReviewWindow(data,
                        new Core.Excel.BoqCatalogIndex(null),
                        FillPlanningOptions.Default, true);
                    plain.Show();
                    Pump(2);
                    // 无预览/无报告（U1F/旧行为）：两侧附加区都隐藏；预览列
                    // 宽回 0、分隔条隐藏（清单独占整行）。
                    Assert.Equal(Visibility.Collapsed, plain.PreviewHost.Visibility);
                    Assert.Equal(Visibility.Collapsed,
                        plain.PreviewSplitter.Visibility);
                    Assert.Equal(0.0, plain.PreviewColumn.Width.Value);
                    Assert.Equal(Visibility.Collapsed,
                        plain.LayerGovernorText.Visibility);
                    plain.Close();

                    var withPreview = new FillReviewWindow(data,
                        new Core.Excel.BoqCatalogIndex(null),
                        FillPlanningOptions.Default, true,
                        PreviewModel("上游 48/V ↔ 下游 37/V · Δ11 跨 × 4.8m ≈ 52.8m"),
                        "图层矫正：6 个实体移正（电缆 3/桥架 2/线管 1/无法判定 0）");
                    withPreview.Show();
                    Pump(3);
                    Assert.Equal(Visibility.Visible, withPreview.PreviewHost.Visibility);
                    // 3.3.1（D-054 Amendment）：预览与清单对半分——两个 Star 列 +
                    // GridSplitter（可拖），最小宽度保护约 30%（3.3.2 布局不变）。
                    Assert.Equal(Visibility.Visible,
                        withPreview.PreviewSplitter.Visibility);
                    Assert.Equal(GridUnitType.Star,
                        withPreview.PreviewColumn.Width.GridUnitType);
                    Assert.Equal(GridUnitType.Star,
                        withPreview.ReviewColumn.Width.GridUnitType);
                    Assert.True(withPreview.PreviewColumn.MinWidth >= 250.0,
                        "预览列最小宽度保护（拖不穿）");
                    Assert.True(withPreview.ReviewColumn.MinWidth >= 250.0,
                        "清单列最小宽度保护（拖不穿）");
                    Assert.True(double.IsNaN(withPreview.PreviewHost.Width),
                        "预览宿主不再固定 300px 窄条（Fill 占满左列）");
                    Assert.Equal(Visibility.Visible,
                        withPreview.LayerGovernorText.Visibility);
                    // 模型正确传入（数据级，不断言渲染像素）。
                    Assert.Contains("Δ11 跨", withPreview.OrbitPreview.Model.AxisSummary,
                        StringComparison.Ordinal);
                    Assert.Equal(2, withPreview.OrbitPreview.Model.Segments.Count);
                    Assert.Equal(2, withPreview.OrbitPreview.BuiltSegmentLineCount);
                    Assert.Contains("图层矫正", withPreview.LayerGovernorText.Text,
                        StringComparison.Ordinal);
                    withPreview.Close();
            });
        }

        [Fact]
        public void BatchWindow_PreviewFollowsSelectedRowAndShowsColumns()
        {
            WpfStaRunner.Run(delegate
            {
                    var previewByHandle = new Dictionary<string, QuickLineOrbitPreviewModel>
                    {
                        ["H1"] = PreviewModel("上游 48/V ↔ 下游 37/V"),
                        ["H2"] = PreviewModel("上游 50/W ↔ 下游 49/W")
                    };
                    var window = new BatchFillConfirmationWindow(new List<BatchFillConfirmationRow>
                        {
                            new BatchFillConfirmationRow("H1", "M1", "设备A", "Running", 4,
                                "✔ 5 段一条链"),
                            new BatchFillConfirmationRow("H2", "M2", "设备B", "有", 6,
                                "⚠ 孤立段：2A0F、3B1E")
                        }, previewByHandle,
                        "图层矫正：3 个实体移正（电缆 1/桥架 2/线管 0/无法判定 0）");
                    window.Show();
                    Pump(3);

                    Assert.Equal(Visibility.Visible, window.PreviewHost.Visibility);
                    // 3.3.1（D-054 Amendment）：对半分布局（Star 列 + Splitter）。
                    Assert.Equal(Visibility.Visible,
                        window.PreviewSplitter.Visibility);
                    Assert.Equal(GridUnitType.Star,
                        window.PreviewColumn.Width.GridUnitType);
                    Assert.Equal(GridUnitType.Star,
                        window.ReviewColumn.Width.GridUnitType);
                    Assert.True(double.IsNaN(window.PreviewHost.Width),
                        "预览宿主不再固定 320px 窄条");
                    Assert.Contains("图层矫正", window.LayerGovernorText.Text,
                        StringComparison.Ordinal);
                    Assert.Equal("✔ 5 段一条链",
                        ((BatchFillConfirmationRow)window.BatchGrid.Items[0])
                            .ConnectivityText);
                    Assert.Equal("⚠ 孤立段：2A0F、3B1E",
                        ((BatchFillConfirmationRow)window.BatchGrid.Items[1])
                            .ConnectivityText);

                    // 选中第二行 → 预览切到 H2 的场景。
                    window.BatchGrid.SelectedItem = window.BatchGrid.Items[1];
                    Pump(2);
                    Assert.Equal(previewByHandle["H2"], window.CurrentPreview);
                    Assert.Contains("50/W", window.CurrentPreview.AxisSummary,
                        StringComparison.Ordinal);
                    Assert.Equal(2, window.OrbitPreview.BuiltSegmentLineCount);

                    // 切回第一行。
                    window.BatchGrid.SelectedItem = window.BatchGrid.Items[0];
                    Pump(2);
                    Assert.Equal(previewByHandle["H1"], window.CurrentPreview);
                    window.Close();
            });
        }

        [Fact]
        public void BatchWindow_WithoutPreviews_KeepsLegacyLayout()
        {
            WpfStaRunner.Run(delegate
            {
                    var window = new BatchFillConfirmationWindow(
                        new List<BatchFillConfirmationRow>
                        {
                            new BatchFillConfirmationRow("3A5F", "M-2F-01", "注塑机",
                                "Running", 42)
                        });
                    window.Show();
                    Pump(2);
                    // 没有预建场景（如整批图纸都没有带毫米标的线段）→ 预览区隐藏，
                    // 行为与旧版一致；连通性列默认「—」。3.3.1/3.3.2：预览列宽
                    // 回 0、分隔条隐藏，清单独占整行。
                    Assert.Equal(Visibility.Collapsed, window.PreviewHost.Visibility);
                    Assert.Equal(Visibility.Collapsed,
                        window.PreviewSplitter.Visibility);
                    Assert.Equal(0.0, window.PreviewColumn.Width.Value);
                    Assert.Equal(Visibility.Collapsed,
                        window.LayerGovernorText.Visibility);
                    Assert.Equal("—",
                        ((BatchFillConfirmationRow)window.BatchGrid.Items[0])
                            .ConnectivityText);
                    window.Close();
            });
        }

        [Fact]
        public void WebControl_HostsEnvelopeAndFallsBackToHint()
        {
            WpfStaRunner.Run(delegate
            {
                    var control = new QuickLineWebPreviewControl();
                    control.Width = 320;
                    control.Height = 260;
                    var host = new Window
                    {
                        Content = control,
                        Width = 360,
                        Height = 320,
                        WindowStyle = WindowStyle.ToolWindow
                    };
                    host.Show();
                    Pump(3);

                    // 无模型：提示文字可见、信封不构建（数据级断言——与
                    // WebView2 初始化完全解耦，无头宿主不装运行时不开页面）。
                    Assert.Null(control.Model);
                    Assert.Equal(0, control.BuiltSegmentLineCount);

                    control.SetModel(PreviewModel("摘要"));
                    Pump(2);
                    Assert.NotNull(control.Model);
                    Assert.Equal(2, control.BuiltSegmentLineCount);

                    // 切回空场景 → 提示回退、信封清空。
                    control.SetModel(null);
                    Pump(2);
                    Assert.Null(control.Model);
                    Assert.Equal(0, control.BuiltSegmentLineCount);
                    host.Close();
            });
        }

        [Fact]
        public void WebControl_BuildsPreviewEnhancementsAndTogglesGrid()
        {
            WpfStaRunner.Run(delegate
            {
                    // 3.3.1（D-054 Amendment）四特性齐备的模型：段毫米标（B）+
                    // 柱网/端点轴位/两向标签（C，3.3.2 数字轴×字母轴）+
                    // 桥架/线管通道（D）。3.3.5：全部经 Core 信封算好，控件
                    // 计数器取信封（渲染内核无关）。
                    var model = new QuickLineOrbitPreviewModel(new List<QuickLineOrbitPreviewSegment>
                    {
                        new QuickLineOrbitPreviewSegment("s1",
                            new QuickLineSpatialPoint(0, 0, 0),
                            new QuickLineSpatialPoint(9600, 0, 0),
                            QuickLineSpatialAxis.X, 9600, true,
                            "9600mm",
                            new QuickLineOrbitPreviewChannel(400.0, false, false)),
                        new QuickLineOrbitPreviewSegment("s2",
                            new QuickLineSpatialPoint(9600, 0, 0),
                            new QuickLineSpatialPoint(9600, 0, 2500),
                            QuickLineSpatialAxis.Z, 2500, true,
                            "2500mm",
                            new QuickLineOrbitPreviewChannel(null, false, true))
                    }, "上游 48/V ↔ 下游 37/V", new List<string>(), "48/V", "37/V");
                    var control = new QuickLineWebPreviewControl();
                    control.Width = 320;
                    control.Height = 260;
                    var host = new Window
                    {
                        Content = control,
                        Width = 360,
                        Height = 320,
                        WindowStyle = WindowStyle.ToolWindow
                    };
                    host.Show();
                    Pump(3);

                    control.SetModel(model);
                    Pump(2);
                    Assert.Equal(2, control.BuiltSegmentLineCount);
                    // 特性B：两条段毫米标都构建（Billboard 世界尺寸，无 2D 阈值）。
                    Assert.Equal(2, control.BuiltLabelCount);
                    // 特性C：柱网线（X/Y 两向覆盖包络）+ 数字轴刻度（48→37 沿 X
                    // 可插值）+ 字母轴标签（V 锚定）+ 两端端点轴位钉。
                    Assert.True(control.BuiltColumnGridLineCount >= 3,
                        "柱网线至少覆盖包络（X/Y 两向）");
                    Assert.True(control.BuiltAxisTickCount >= 2,
                        "两端数字轴可解析时数字刻度要画");
                    Assert.True(control.BuiltLetterLabelCount >= 1,
                        "两端字母轴可解析时字母标签要画（3.3.2 两向柱网）");
                    Assert.Equal(2, control.BuiltEndpointPinCount);
                    // 特性D：桥架双线 1 段 + 线管双线 1 段。
                    Assert.Equal(1, control.BuiltTrayChannelCount);
                    Assert.Equal(1, control.BuiltConduitChannelCount);

                    // 柱网开关：关掉只影响柱网/两向标签，路由与通道照常。
                    Assert.True(control.ColumnGridEnabled);
                    control.ToggleColumnGrid();
                    Pump(2);
                    Assert.False(control.ColumnGridEnabled);
                    Assert.Equal(0, control.BuiltColumnGridLineCount);
                    Assert.Equal(0, control.BuiltAxisTickCount);
                    Assert.Equal(0, control.BuiltLetterLabelCount);
                    Assert.Equal(2, control.BuiltSegmentLineCount);
                    Assert.Equal(2, control.BuiltLabelCount);
                    Assert.Equal(1, control.BuiltTrayChannelCount);
                    Assert.Equal(1, control.BuiltConduitChannelCount);

                    // 重新开回：柱网与两向标签恢复（开关是显示语义，规划不丢）。
                    control.ToggleColumnGrid();
                    Pump(2);
                    Assert.True(control.BuiltColumnGridLineCount >= 3);
                    Assert.True(control.BuiltAxisTickCount >= 2);
                    host.Close();
            });
        }


    /// <summary>
    /// 共享单 STA 线程运行器（3.3.2 为 Helix 视口而立，3.3.5 内核换 WebView2
    /// 后保留）：WPF 窗体/主题资源在同一进程多 STA 线程间复用会炸测试宿主，
    /// WpfUiSerial 集合内全部窗体测试收敛到本运行器的单一 STA 线程。运行器
    /// 同时关掉 QuickLineWebPreviewControl.DefaultAutoInitialize——无头测试
    /// 不装 WebView2 运行时、不真开页面（数据级断言不需要渲染）。
    /// </summary>
    internal static class WpfStaRunner
    {
        private static Dispatcher _dispatcher;

        private static void Ensure()
        {
            QuickLineWebPreviewControl.DefaultAutoInitialize = false;
            if (_dispatcher != null) return;
            using (var ready = new ManualResetEvent(false))
            {
                var thread = new Thread(new ThreadStart(delegate
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                }));
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                ready.WaitOne();
            }
        }

        public static void Run(Action body)
        {
            Ensure();
            Exception failure = null;
            _dispatcher.Invoke(delegate
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            if (failure != null) throw failure;
        }
    }

        [Fact]
        public void WebControl_ComputesEnvelopeOnlyWithModel()
        {
            WpfStaRunner.Run(delegate
            {
                var control = new QuickLineWebPreviewControl();
                control.Width = 320;
                control.Height = 260;
                var host = new Window
                {
                    Content = control,
                    Width = 360,
                    Height = 320,
                    WindowStyle = WindowStyle.ToolWindow
                };
                host.Show();
                Pump(2);
                // 无模型（U1F/无预览路径）：信封不构建、WebView2 不启动
                //（测试进程 DefaultAutoInitialize=false；真机的惰性初始化
                // 同样以首个非 null SetModel 为触发点）。
                Assert.Null(control.Model);
                Assert.Equal(0, control.BuiltSegmentLineCount);
                control.SetModel(null);
                Pump(1);
                Assert.Null(control.Model);
                Assert.Equal(0, control.BuiltSegmentLineCount);

                // 首个非 null 模型：信封同步构建（数据级，不等页面）。
                control.SetModel(PreviewModel("摘要"));
                Pump(2);
                Assert.NotNull(control.Model);
                Assert.Equal(2, control.BuiltSegmentLineCount);
                host.Close();
            });
        }

        [Fact]
        public void U1L3DProbe_RealWebView2_ControlExperiment()
        {
            // 对照实验：同宿主跑生产验证过的 U1L3DPreviewWindow（同为 WebView2、
            // 同为 Loaded 后初始化）。若它也白屏 → 测试宿主环境本身显示不出
            // WebView2 合成（控件无缺陷，生产空白另有原因）；若它正常而
            // Orbit 探针白 → 控件与参考实现的差异是真缺陷。
            WpfStaRunner.Run(delegate
            {
                string repoWeb = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "..",
                    "..", "src", "UNCAD", "Web"));
                var window = new U1L3DPreviewWindow(
                    new System.Collections.Generic.Dictionary<string, object>(),
                    System.IO.Path.Combine(repoWeb, "QuickLine3D"))
                {
                    AutoInitializeWebView = true
                };
                window.Show();
                for (int i = 0; i < 120; i++)
                {
                    Pump(1);
                    Thread.Sleep(100);
                }
                string dir = Path.Combine(Path.GetTempPath(), "uncad-orbit-probe");
                Directory.CreateDirectory(dir);
                CaptureWindow(window, Path.Combine(dir, "u1l3d.png"));
                window.Close();
            });
        }

        [Fact]
        public void OrbitProbe_RealWebView2_Diagnostic()
        {
            // 3.3.6 现场诊断（用户报告"展示3D的区域是空白的"）：真 WebView2 +
            // 真页面（WebRootOverride 指向源码树 Web/——测试输出目录没有 Web/，
            // 首版探针正是复现了"找不到页面资源"的致命路径）。控件直挂普通
            // 窗，转储内部状态并 GDI 截图到 %TEMP%\uncad-orbit-probe\。
            WpfStaRunner.Run(delegate
            {
                QuickLineWebPreviewControl.DefaultAutoInitialize = true;
                try
                {
                    string repoWeb = Path.GetFullPath(Path.Combine(
                        AppContext.BaseDirectory, "..", "..", "..", "..",
                        "..", "src", "UNCAD", "Web"));
                    var control = new QuickLineWebPreviewControl
                    {
                        Width = 880,
                        Height = 600,
                        WebRootOverride = Path.Combine(repoWeb, "QuickLineOrbit")
                    };
                    var window = new Window
                    {
                        Title = "orbit-probe",
                        Content = control,
                        Width = 920,
                        Height = 660,
                        Topmost = true
                    };
                    window.Show();
                    // 变体验证：等窗口 Loaded/布局后再 SetModel（U1L3D 的
                    // BeginInitialize 挂在 Loaded 上；生产 FillReviewWindow 是
                    // 构造期 SetModel——预 HWND 初始化疑似白屏根因）。
                    for (int i = 0; i < 15; i++)
                    {
                        Pump(1);
                        Thread.Sleep(100);
                    }
                    control.SetModel(PreviewModel("上游 48/V ↔ 下游 37/V"));
                    for (int i = 0; i < 120; i++)
                    {
                        Pump(1);
                        Thread.Sleep(100);
                    }
                    var flags = System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic;
                    var type = typeof(QuickLineWebPreviewControl);
                    string State(string field)
                        => Convert.ToString(type.GetField(field, flags)?.GetValue(control));
                    var webField = type.GetField("_webView", flags);
                    var webView = webField?.GetValue(control);
                    var core = webView?.GetType().GetProperty("CoreWebView2")?.GetValue(webView);
                    string report = "segments=" + control.BuiltSegmentLineCount
                        + " labels=" + control.BuiltLabelCount
                        + " tray=" + control.BuiltTrayChannelCount
                        + " grid=" + control.BuiltColumnGridLineCount
                        + "\n_started=" + State("_initializationStarted")
                        + "\n_pageReady=" + State("_pageReady")
                        + "\n_fatal=" + State("_fatal")
                        + "\n_closed=" + State("_closed")
                        + "\n_core=" + (core != null ? "created" : "null")
                        + "\n_webVisibility=" + (webView as UIElement)?.Visibility;
                    string dir = Path.Combine(Path.GetTempPath(), "uncad-orbit-probe");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "state.txt"), report);
                    // 页面内侧探针：DOM 状态/背景/canvas 尺寸/标签数/当前 URL。
                    if (core != null)
                    {
                        try
                        {
                            var exec = core.GetType().GetMethod("ExecuteScriptAsync",
                                new[] { typeof(string) });
                            var task = (System.Threading.Tasks.Task<string>)exec.Invoke(
                                core, new object[]
                                {
                                    "JSON.stringify({ready: document.readyState," +
                                    "bg: getComputedStyle(document.body).backgroundColor," +
                                    "canvas: (function(){var c=document.querySelector('#canvasHost canvas');" +
                                    "return c ? c.width+'x'+c.height : 'none';})()," +
                                    "labels: document.querySelectorAll('.scene-label').length," +
                                    "loading: document.getElementById('loading') ? " +
                                    "document.getElementById('loading').style.display : 'none'," +
                                    "host: !!window.chrome && !!window.chrome.webview})"
                                });
                            // 边泵边等：ExecuteScriptAsync 的 continuation 要回宿主
                            // UI 线程，裸 Wait(3000) 会堵死 STA 泵卡住整个测试。
                            var deadline = Environment.TickCount + 4000;
                            while (!task.IsCompleted
                                && Environment.TickCount < deadline)
                            {
                                Pump(1);
                                Thread.Sleep(50);
                            }
                            File.WriteAllText(Path.Combine(dir, "page.txt"),
                                task.IsCompleted
                                    ? Convert.ToString(task.Result)
                                    : "SCRIPT-TIMEOUT");
                            var sourceProp = core.GetType().GetProperty("Source");
                            File.AppendAllText(Path.Combine(dir, "page.txt"),
                                "\nsource=" + sourceProp?.GetValue(core));
                        }
                        catch (Exception ex)
                        {
                            File.WriteAllText(Path.Combine(dir, "page.txt"),
                                "EXEC-FAIL: " + ex.Message);
                        }
                    }
                    CaptureWindow(window, Path.Combine(dir, "window.png"));
                    window.Close();
                }
                finally
                {
                    QuickLineWebPreviewControl.DefaultAutoInitialize = false;
                }
            });
        }

        [Fact]
        public void ProductionSequence_CtorSetModel_ThenShow_RendersDarkScene()
        {
            // 3.3.7.2 终验探针（装机门禁）：完全复刻生产 FillReviewWindow 的
            // 时序——**构造期（窗口 Show 之前）就 SetModel**，之后才 Show。
            // 2026-11-18 10:31 现场：旧实现构造期即启动初始化=预 HWND 调
            // EnsureCoreWebView2Async（审计 R5「疑似白屏根因」）；3.3.7.2 的
            // Loaded 挂钩被构造期 SetModel 抢先、形同虚设（同日现场再白）。
            // 本探针锁死新语义：构造期 SetModel 只挂起请求，Loaded（hwnd
            // 存在）才放行初始化——真 WebView2+真页面+GDI 真像素，非纯白
            // 才算过。冷启动容忍 45s（本机控制器冷创建实测 33.5s）。
            WpfStaRunner.Run(delegate
            {
                QuickLineWebPreviewControl.DefaultAutoInitialize = true;
                try
                {
                    string repoWeb = Path.GetFullPath(Path.Combine(
                        AppContext.BaseDirectory, "..", "..", "..", "..",
                        "..", "src", "UNCAD", "Web"));
                    var control = new QuickLineWebPreviewControl
                    {
                        Width = 880,
                        Height = 600,
                        WebRootOverride = Path.Combine(repoWeb, "QuickLineOrbit")
                    };
                    // 生产顺序：先 SetModel（此时控件未加载——必须只挂起，
                    // 不得启动初始化）。
                    control.SetModel(PreviewModel("生产时序 48/V"));
                    var window = new Window
                    {
                        Title = "orbit-production-sequence",
                        Content = control,
                        Width = 920,
                        Height = 660,
                        Topmost = true
                    };
                    window.Show();
                    string dir = Path.Combine(Path.GetTempPath(),
                        "uncad-orbit-probe");
                    Directory.CreateDirectory(dir);
                    string png = Path.Combine(dir, "production-sequence.png");
                    bool pageReadySeen = false;
                    for (int i = 0; i < 450; i++)
                    {
                        Pump(1);
                        Thread.Sleep(100);
                        if (i == 30) CaptureWindow(window, png + ".early.png");
                        var flags = System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.NonPublic;
                        if (Convert.ToString(typeof(QuickLineWebPreviewControl)
                                .GetField("_pageReady", flags)?.GetValue(control))
                            .Equals("True"))
                        {
                            pageReadySeen = true;
                            Thread.Sleep(700);
                            break;
                        }
                    }
                    CaptureWindow(window, png);
                    window.Close();
                    Assert.True(pageReadySeen,
                        "生产时序下页面应就绪（45s 内）——预热/初始化链路不通");
                    // 真像素门禁：画面不得是纯白（未绑定的 WebView2 hwnd 是
                    // 255/255/255）。采样中心区域多点。
                    using (var bitmap = new System.Drawing.Bitmap(png))
                    {
                        int whiteish = 0;
                        int samples = 0;
                        for (int x = bitmap.Width / 4;
                             x < bitmap.Width * 3 / 4; x += 24)
                        {
                            for (int y = bitmap.Height / 4;
                                 y < bitmap.Height * 3 / 4; y += 24)
                            {
                                var pixel = bitmap.GetPixel(x, y);
                                samples++;
                                if (pixel.R > 240 && pixel.G > 240
                                    && pixel.B > 240) whiteish++;
                            }
                        }
                        Assert.True(samples > 0 && whiteish * 100 / samples < 20,
                            "中心区域白色占比 " + whiteish + "/" + samples
                            + " —— 生产时序下预览必须是深色 3D 场景而非白块");
                    }
                }
                finally
                {
                    QuickLineWebPreviewControl.DefaultAutoInitialize = false;
                }
            });
        }

        /// <summary>GDI 屏幕复制抓窗口真实合成画面（含 WebView2 的 HwndHost）。</summary>
        private static void CaptureWindow(Window window, string path)
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero) helper.EnsureHandle();
            Interop.SetForegroundWindow(helper.Handle);
            Thread.Sleep(300);
            Interop.RECT rect;
            if (!Interop.GetWindowRect(helper.Handle, out rect)) return;
            int width = Math.Max(1, rect.Right - rect.Left);
            int height = Math.Max(1, rect.Bottom - rect.Top);
            using (var bitmap = new System.Drawing.Bitmap(width, height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0,
                    new System.Drawing.Size(width, height),
                    System.Drawing.CopyPixelOperation.SourceCopy);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static class Interop
        {
            [System.Runtime.InteropServices.StructLayout(
                System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct RECT { public int Left, Top, Right, Bottom; }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetForegroundWindow(IntPtr hWnd);
        }

        internal static QuickLineOrbitPreviewModel PreviewModel(string axisSummary)
            => new QuickLineOrbitPreviewModel(new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(4000, 0, 0),
                    QuickLineSpatialAxis.X, 4000, true),
                new QuickLineOrbitPreviewSegment("s2",
                    new QuickLineSpatialPoint(4000, 0, 0),
                    new QuickLineSpatialPoint(4000, 0, 2500),
                    QuickLineSpatialAxis.Z, 2500, true)
            }, axisSummary, new List<string>());

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }
    }
}
