using System;
using System.Collections.Generic;
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
