using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.QuickLine;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.4.0：U1U/U13D 3D 预览内核换 **WPF 原生 Viewport3D**（用户指令
    /// 「算了 用 Viewport3D吧」——WebView2 用户数据目录锁在本机同步阻塞
    /// STA 线程，「正在启动三维渲染器」白屏反复复发；浏览器内核整条路
    /// 退役，D-054 3.3.5 的 WebView2 选型一并作废）。断言口径随内核调整：
    /// 窗口宿主存在、模型数据正确传入、布局回退保持，**全部数据级**——
    /// SetModel 同步建场景（无任何异步初始化，「启动中」这个状态不存在），
    /// 场景构建与 GPU 光栅化解耦；新增场景构建器/批量适配器的图元级断言。
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
        public void OrbitControl_HostsEnvelopeAndFallsBackToHint()
        {
            WpfStaRunner.Run(delegate
            {
                    var control = new QuickLineOrbitPreviewControl();
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

                    // 无模型：提示文字可见、信封不构建（SetModel 同步，无任何
                    // 异步初始化——「正在启动三维渲染器」这个状态不存在了）。
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
        public void OrbitControl_BuildsPreviewEnhancementsAndTogglesGrid()
        {
            WpfStaRunner.Run(delegate
            {
                // 3.3.1 四特性齐备的模型：段毫米标（B）+ 柱网/端点轴位/两向
                // 标签（C）+ 桥架/线管通道（D）。计数器取信封（渲染内核无关）。
                // 3.8.0（D-059）：轴位只在有楼层平面时画——平面没了就没有柱网。
                var model = new QuickLineOrbitPreviewModel(new List<QuickLineOrbitPreviewSegment>
                {
                        new QuickLineOrbitPreviewSegment("s1",
                            new QuickLineSpatialPoint(0, 0, 0),
                            new QuickLineSpatialPoint(0, 9600, 0),
                            QuickLineSpatialAxis.X, 9600, true,
                            "9600mm",
                            new QuickLineOrbitPreviewChannel(400.0, false, false)),
                        new QuickLineOrbitPreviewSegment("s2",
                            new QuickLineSpatialPoint(0, 9600, 0),
                            new QuickLineSpatialPoint(0, 9600, 2500),
                            QuickLineSpatialAxis.Z, 2500, true,
                            "2500mm",
                            new QuickLineOrbitPreviewChannel(null, false, true))
                    }, "上游 48/V ↔ 下游 46/V", new List<string>(),
                        "48/V", "46/V",
                        new QuickLineSpatialPoint(0, 0, 0),
                        new QuickLineSpatialPoint(0, 9600, 2500),
                        directionResolved: true,
                        floors: PreviewFloorPlan.Plan("2F", "3F", null,
                            PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList)));
                var control = new QuickLineOrbitPreviewControl();
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
                // 特性B：两条段毫米标都构建。
                Assert.Equal(2, control.BuiltLabelCount);
                // D-059：台账 2F→3F → 两块楼层平面。
                Assert.Equal(2, control.BuiltFloorCount);
                // 特性C：柱网线 + 数字轴刻度 + 字母轴标签 + 两端端点轴位钉。
                Assert.True(control.BuiltColumnGridLineCount >= 3,
                    "柱网线至少覆盖平面（X/Y 两向）");
                Assert.True(control.BuiltAxisTickCount >= 2,
                    "两端数字轴可解析时数字刻度要画");
                Assert.True(control.BuiltLetterLabelCount >= 1,
                    "两端字母轴可解析时字母标签要画（3.3.2 两向柱网）");
                Assert.Equal(2, control.BuiltEndpointPinCount);
                // 特性D：桥架双线 1 段 + 线管双线 1 段。
                Assert.Equal(1, control.BuiltTrayChannelCount);
                Assert.Equal(1, control.BuiltConduitChannelCount);

                // 轴位开关：关掉只影响柱网/两向标签，路由、通道、平面照常。
                Assert.True(control.ColumnGridEnabled);
                var viewport = FindViewport(control);
                control.ToggleColumnGrid();
                Pump(2);
                Assert.False(control.ColumnGridEnabled);
                Assert.False(viewport.IsGroupVisible(QuickLine3dSceneGroupKind.Grid));
                Assert.Equal(0, control.BuiltColumnGridLineCount);
                Assert.Equal(0, control.BuiltAxisTickCount);
                Assert.Equal(0, control.BuiltLetterLabelCount);
                Assert.Equal(2, control.BuiltSegmentLineCount);
                Assert.Equal(2, control.BuiltLabelCount);
                Assert.Equal(1, control.BuiltTrayChannelCount);
                Assert.Equal(1, control.BuiltConduitChannelCount);
                Assert.Equal(2, control.BuiltFloorCount);

                // 重新开回：柱网与两向标签恢复（开关是显示语义，规划不丢）。
                control.ToggleColumnGrid();
                Pump(2);
                Assert.True(control.BuiltColumnGridLineCount >= 3);
                Assert.True(control.BuiltAxisTickCount >= 2);

                // 参照建筑（华夫板/柱子/工字钢）已整体去掉：场景里没有这些组。
                Assert.False(viewport.HasGroup(QuickLine3dSceneGroupKind.Columns));
                Assert.False(viewport.HasGroup(QuickLine3dSceneGroupKind.Detail));
                host.Close();
            });
        }

        [Fact]
        public void OrbitControl_BuildsNativeSceneSynchronously()
        {
            WpfStaRunner.Run(delegate
            {
                // 3.4.0 内核换血的核心契约：SetModel 当场把场景画进 Viewport3D
                // （网格/标签同步可见），不存在任何「启动中」中间态。
                var control = new QuickLineOrbitPreviewControl();
                control.Width = 640;
                control.Height = 480;
                var host = new Window
                {
                    Content = control,
                    Width = 680,
                    Height = 540,
                    WindowStyle = WindowStyle.ToolWindow
                };
                host.Show();
                Pump(3);
                control.SetModel(FloorSampleModel());
                Pump(2);
                QuickLine3dViewportControl viewport = FindViewport(control);
                Assert.NotNull(viewport);
                Assert.True(viewport.MeshCount > 0, "路由网格必须同步建好");
                Assert.True(viewport.TriangleCount > 0);
                Assert.True(viewport.LabelCount >= 8,
                    "楼层名 + 两端牌子 + 米数 + 整趟标签必须同步进叠加层");
                // 双击复位口径：复位后相机仍在（复位是纯数学，不出异常即通）。
                viewport.ResetCamera();
                Pump(1);
                host.Close();
            });
        }

        /// <summary>
        /// 3.8.0 样例：2F 上游 I-Line 盘 → 桥架 → 立管上 3F → 线管 → 设备，
        /// 上游 48/V、设备 37/V。lift 是整条线路离楼层的抬升量（插座盘 1000）。
        /// </summary>
        private static QuickLineOrbitPreviewModel FloorSampleModel(
            PreviewCabinetKind cabinet = PreviewCabinetKind.ILinePanel,
            double lift = 0.0)
        {
            return new QuickLineOrbitPreviewModel(new[]
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, lift), new QuickLineSpatialPoint(14400, 0, lift),
                    QuickLineSpatialAxis.X, 14400, true, "14400mm",
                    new QuickLineOrbitPreviewChannel(400.0, false, false)),
                new QuickLineOrbitPreviewSegment("s2",
                    new QuickLineSpatialPoint(14400, 0, lift), new QuickLineSpatialPoint(14400, 4800, lift),
                    QuickLineSpatialAxis.Y, 4800, true, "4800mm",
                    new QuickLineOrbitPreviewChannel(400.0, false, false)),
                new QuickLineOrbitPreviewSegment("s3",
                    new QuickLineSpatialPoint(14400, 4800, lift), new QuickLineSpatialPoint(14400, 4800, 7800 + lift),
                    QuickLineSpatialAxis.Z, 7800, true, "7800mm"),
                new QuickLineOrbitPreviewSegment("s4",
                    new QuickLineSpatialPoint(14400, 4800, 7800 + lift), new QuickLineSpatialPoint(14400, 14400, 7800 + lift),
                    QuickLineSpatialAxis.Y, 9600, true, "9600mm",
                    new QuickLineOrbitPreviewChannel(null, false, true)),
                new QuickLineOrbitPreviewSegment("s5",
                    new QuickLineSpatialPoint(14400, 14400, 7800 + lift), new QuickLineSpatialPoint(4800, 14400, 7800 + lift),
                    QuickLineSpatialAxis.X, 9600, true, "9600mm")
            }, "上游 48/V ↔ 下游 37/V", new List<string>(), "48/V", "37/V",
                new QuickLineSpatialPoint(0, 0, lift),
                new QuickLineSpatialPoint(4800, 14400, 7800 + lift),
                directionResolved: true,
                hose: new QuickLineOrbitPreviewHose(2500.0, 100.0, "Ruanguan 块", false),
                floors: PreviewFloorPlan.Plan("2F", "3F", null,
                    PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList)),
                cabinetKind: cabinet,
                annotationRuns: new[]
                {
                    new QuickLineOrbitPreviewAnnotationRun(RouteAnnotationTextKind.Tray,
                        "梯形桥架400Wx200H", 19200.0, 400.0, new[] { "s1", "s2" }, "元数据"),
                    new QuickLineOrbitPreviewAnnotationRun(RouteAnnotationTextKind.Conduit,
                        "镀锌穿线管38mm", 9600.0, null, new[] { "s4" }, "元数据")
                });
        }

        [Fact]
        public void OrbitSceneBuilder_DrawsOnlyFloorsAxesRouteAndCabinet()
        {
            // D-059：场景里只有楼层平面、柱网、线路（含通道/软管）、上游柜和牌子。
            QuickLineOrbitPreviewEnvelope envelope = QuickLineOrbitPreviewEnvelope.Build(
                FloorSampleModel());
            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(envelope);

            // 参照建筑整体去掉：没有华夫板，也没有柱子/结构细节组。
            Assert.DoesNotContain(scene.Primitives, primitive =>
                primitive.Group == QuickLine3dSceneGroupKind.Columns
                || primitive.Group == QuickLine3dSceneGroupKind.Detail);
            // 楼层平面只画边框+柱网，不画半透明面——面会写深度，从楼上看会把
            // 另一侧的电缆/标签挡住（3.8.0.1）。盒子只有上游柜一个。
            Assert.DoesNotContain(scene.Primitives, primitive =>
                primitive.Kind == QuickLine3dPrimitiveKind.Quad
                && primitive.Group == QuickLine3dSceneGroupKind.Reference);
            QuickLine3dPrimitive cabinet = Assert.Single(scene.Primitives,
                primitive => primitive.Kind == QuickLine3dPrimitiveKind.Box);
            Assert.Equal(QuickLine3dSceneGroupKind.Reference, cabinet.Group);
            // I-Line 盘落在 2F 地面：底面 0、高 2200、立在上游端点（0,0）的背后。
            Assert.Equal(0.0, cabinet.A1z - cabinet.Sizez / 2.0, 6);
            Assert.Equal(2200.0, cabinet.Sizez, 6);
            Assert.True(cabinet.A1x < 0.0, "柜子放在路线的反方向");
            // 柱网：最低层和最高层各一套。
            Assert.Contains(scene.Primitives, primitive =>
                primitive.Group == QuickLine3dSceneGroupKind.Grid
                && Math.Abs(primitive.A1z) < 1e-6);
            Assert.Contains(scene.Primitives, primitive =>
                primitive.Group == QuickLine3dSceneGroupKind.Grid
                && Math.Abs(primitive.A1z - 7800.0) < 1e-6);

            // 牌子：楼层名、柜子、整趟桥架/线管、米数（改成米）、两端（带轴位）。
            Assert.Contains(scene.Labels, label => label.Kind == "floor"
                && label.Text.StartsWith("2F", StringComparison.Ordinal)
                && label.Text.EndsWith("±0.00", StringComparison.Ordinal));
            Assert.Contains(scene.Labels, label => label.Kind == "floor"
                && label.Text.StartsWith("3F", StringComparison.Ordinal)
                && label.Text.EndsWith("+7.80", StringComparison.Ordinal));
            Assert.Contains(scene.Labels, label => label.Kind == "cabinet"
                && label.Text.StartsWith("I-Line盘", StringComparison.Ordinal));
            Assert.Contains(scene.Labels, label => label.Kind == "tray"
                && label.Text == "桥架 400 · 19.2m");
            Assert.Contains(scene.Labels, label => label.Kind == "conduit"
                && label.Text == "线管 · 9.6m");
            Assert.Contains(scene.Labels, label => label.Kind == "hose"
                && label.Text == "软管 ⌀100 · 2.5m");
            foreach (string metres in new[] { "14.4 m", "4.8 m", "7.8 m" })
                Assert.Contains(scene.Labels, label => label.Kind == "mm"
                    && label.Text == metres);
            Assert.Contains(scene.Labels, label => label.Kind == "directionUp"
                && label.Text == "上游端  48/V");
            Assert.Contains(scene.Labels, label => label.Kind == "directionDown"
                && label.Text == "设备端  37/V");
            // 轴位边标：数字一排、字母一排，贴在 3F 平面的两条外边上。
            Assert.Contains(scene.Labels, label => label.Kind == "axisEdge"
                && label.Text == "48");
            Assert.Contains(scene.Labels, label => label.Kind == "axisEdge"
                && label.Text == "V");
            // 旧的参照层标签不再出现。
            Assert.DoesNotContain(scene.Labels, label =>
                label.Text.IndexOf("母线", StringComparison.Ordinal) >= 0
                || label.Text.IndexOf("1F地面", StringComparison.Ordinal) >= 0
                || label.Text.IndexOf("华夫", StringComparison.Ordinal) >= 0
                || label.Text.IndexOf("葡萄架", StringComparison.Ordinal) >= 0);
            // 取景把楼层平面和柜顶都框进来：中心在两层平面之间（略抬高），
            // 相机距离按整块场景算。
            Assert.True(scene.Centerz > 3900.0 && scene.Centerz < 5200.0,
                "实际 " + scene.Centerz);
            Assert.True(scene.CameraDistance > 20000.0);
        }

        [Fact]
        public void OrbitSceneBuilder_SceneStaysLight()
        {
            // 卡顿的根源是参照建筑的三角形数和半透明材质；只剩平面+柱网+线路后，
            // 同样 5 段路线的场景必须远小于 3.7.0 的 7880 个三角形。
            WpfStaRunner.Run(delegate
            {
                var control = new QuickLineOrbitPreviewControl();
                control.Width = 640;
                control.Height = 480;
                var host = new Window
                {
                    Content = control,
                    Width = 680,
                    Height = 540,
                    WindowStyle = WindowStyle.ToolWindow
                };
                host.Show();
                Pump(3);
                control.SetModel(FloorSampleModel());
                Pump(2);
                QuickLine3dViewportControl viewport = FindViewport(control);
                Assert.True(viewport.TriangleCount > 0);
                Assert.True(viewport.TriangleCount < 2500,
                    "三角形数应远小于参照建筑时代，实际 " + viewport.TriangleCount);
                Assert.Equal(2, control.BuiltFloorCount);
                Assert.Equal(2, control.BuiltTrayChannelCount);
                Assert.Equal(1, control.BuiltConduitChannelCount);
                host.Close();
            });
        }

        [Fact]
        public void OrbitSceneBuilder_CabinetFollowsKindFloorAndRiserBase()
        {
            // D-059：I-Line 盘落地；插座盘离地 1 m；母线插接口按端点高度；
            // 端点段是向上的立管时，基础点取立管底。
            QuickLineOrbitPreviewEnvelope panel = QuickLineOrbitPreviewEnvelope.Build(
                FloorSampleModel(PreviewCabinetKind.ILinePanel));
            Assert.Equal(0.0, panel.Cabinet.BaseZ, 6);

            // 3.8.0.2：插座盘——抬的是整条线路（上游末端落在 2F 地面 + 1 m），
            // 方块只是占位：柜底与上游末端同高，柜下的支架把 1 m 空隙画出来。
            QuickLineOrbitPreviewEnvelope socket = QuickLineOrbitPreviewEnvelope.Build(
                FloorSampleModel(PreviewCabinetKind.SocketPanel, 1000.0));
            Assert.Equal(1000.0, socket.Cabinet.BaseZ, 6);
            Assert.Equal(1000.0, socket.Cabinet.Height, 6);
            Assert.Equal(0.0, socket.Cabinet.MountingFloorZ.Value, 6);
            Assert.Equal(1000.0, socket.Cabinet.Connection.Z, 6);
            Assert.Equal(1000.0, socket.Segments.Min(segment =>
                Math.Min(segment.Start.Z, segment.End.Z)), 6);
            // 占位方块按场景放大（几十米的楼层里真实尺寸只有十来个像素）。
            Assert.True(socket.Cabinet.SizeX > 600.0 || socket.Cabinet.SizeY > 600.0,
                "placeholder footprint is scaled up: " + socket.Cabinet.SizeX
                + " x " + socket.Cabinet.SizeY);
            QuickLine3dScene socketScene = QuickLineOrbitSceneBuilder.Build(socket);
            QuickLine3dSceneLabel socketLabel = Assert.Single(socketScene.Labels,
                label => label.Kind == "cabinet"
                    && label.Text.IndexOf("离地 1 m", StringComparison.Ordinal) >= 0);
            Assert.Equal(socket.Cabinet.CenterX, socketLabel.X, 6);
            Assert.True(socketLabel.Z >= socket.Cabinet.BaseZ
                && socketLabel.Z <= socket.Cabinet.TopZ,
                "柜子标签在柜子高度范围内，实际 Z " + socketLabel.Z);
            // 从楼层地面（0）到柜底（1000）有一根细支架，离地 1 m 一眼可见。
            Assert.Contains(socketScene.Primitives, primitive =>
                primitive.Kind == QuickLine3dPrimitiveKind.Tube
                && primitive.Group == QuickLine3dSceneGroupKind.Reference
                && Math.Abs(primitive.A1z) < 1e-6
                && Math.Abs(primitive.B1z - 1000.0) < 1e-6);

            QuickLineOrbitPreviewEnvelope plug = QuickLineOrbitPreviewEnvelope.Build(
                FloorSampleModel(PreviewCabinetKind.BusPlugBox));
            Assert.Equal(0.0 - 350.0, plug.Cabinet.BaseZ, 6);

            // 立管起点：上游端点在立管顶（2200），楼层定不出来 → 基础点取立管底。
            var riser = new QuickLineOrbitPreviewModel(new[]
            {
                new QuickLineOrbitPreviewSegment("up",
                    new QuickLineSpatialPoint(0, 0, 0), new QuickLineSpatialPoint(0, 0, 2200),
                    QuickLineSpatialAxis.Z, 2200, true, "2200mm"),
                new QuickLineOrbitPreviewSegment("run",
                    new QuickLineSpatialPoint(0, 0, 2200), new QuickLineSpatialPoint(4800, 0, 2200),
                    QuickLineSpatialAxis.X, 4800, true, "4800mm")
            }, "摘要", new List<string>(), "48/V", "48/W",
                new QuickLineSpatialPoint(0, 0, 2200),
                new QuickLineSpatialPoint(4800, 0, 2200),
                directionResolved: true, cabinetKind: PreviewCabinetKind.ILinePanel);
            QuickLineOrbitPreviewEnvelope riserEnvelope =
                QuickLineOrbitPreviewEnvelope.Build(riser);
            Assert.Equal(0.0, riserEnvelope.Cabinet.BaseZ, 6);
            // 柜子放在立管顶上那根水平段的反方向（−X 一侧），不在立管正上方。
            Assert.True(riserEnvelope.Cabinet.CenterX < 0.0);
            Assert.Equal(0.0, riserEnvelope.Cabinet.CenterY, 6);

            Assert.Equal(PreviewCabinetKind.None, PreviewCabinet.KindOf(""));
            Assert.Equal(PreviewCabinetKind.ILinePanel, PreviewCabinet.KindOf("I-Line盘"));
            Assert.Equal(PreviewCabinetKind.SocketPanel, PreviewCabinet.KindOf("插座盘"));
            Assert.Equal(PreviewCabinetKind.BusPlugBox, PreviewCabinet.KindOf("母线插接口"));
            Assert.Null(QuickLineOrbitPreviewEnvelope.Build(
                FloorSampleModel(PreviewCabinetKind.None)).Cabinet);
        }

        [Fact]
        public void OrbitControl_LongRouteRendersWithoutBayLimit()
        {
            // 3.7.0 及之前参照建筑按「占用柱跨」铺，超过 4096 跨直接报错不出图；
            // 楼层平面是一整块矩形，再长的路线也照常出图。
            WpfStaRunner.Run(delegate
            {
                var control = new QuickLineOrbitPreviewControl();
                control.SetModel(new QuickLineOrbitPreviewModel(new[]
                {
                    new QuickLineOrbitPreviewSegment("long",
                        new QuickLineSpatialPoint(0, 0, 0),
                        new QuickLineSpatialPoint(1000000000, 0, 0),
                        QuickLineSpatialAxis.X, 1000000000, true)
                }, "", new List<string>()));
                Assert.True(FindViewport(control).TriangleCount > 0);
                Assert.DoesNotContain(control.Children.OfType<TextBlock>(),
                    text => text.Visibility == Visibility.Visible
                        && text.Text.IndexOf("4096", StringComparison.Ordinal) >= 0);
                control.SetModel(null);
                Assert.Equal(0, FindViewport(control).TriangleCount);
            });
        }

        [Fact]
        public void OrbitControl_ShowsAtMostThreeIssuesThenACount()
        {
            // D-058：窗格点名线路识别问题（最多 3 条 + 计数），放在不挡鼠标的
            // 半透明底衬上；没有问题时问题行收起。
            WpfStaRunner.Run(delegate
            {
                var control = new QuickLineOrbitPreviewControl();
                control.SetModel(PreviewModel(""));
                var withIssues = new QuickLineOrbitPreviewModel(new[]
                {
                    new QuickLineOrbitPreviewSegment("s1",
                        new QuickLineSpatialPoint(0, 0, 0),
                        new QuickLineSpatialPoint(3000, 0, 0),
                        QuickLineSpatialAxis.X, 3000, true)
                }, "", new List<string>(), recognitionSummary: "识别 1 段",
                    routeIssues: new[] { "问题一", "问题二", "问题三", "问题四", "问题五" });
                control.SetModel(withIssues);
                Assert.StartsWith("⚠ 问题一\n⚠ 问题二\n⚠ 问题三\n…另有 2 项，详见命令行输出",
                    control.BuildIssueText(control.CurrentEnvelope),
                    StringComparison.Ordinal);
                Border info = Assert.Single(control.Children.OfType<Border>());
                Assert.Equal(Visibility.Visible, info.Visibility);
                Assert.False(info.IsHitTestVisible);
                Assert.Contains("识别 1 段", QuickLineOrbitSceneBuilder.BuildSummary(
                    control.CurrentEnvelope), StringComparison.Ordinal);

                control.SetModel(PreviewModel(""));
                Assert.Equal(string.Empty, control.BuildIssueText(control.CurrentEnvelope));
            });
        }


        [Fact]
        public void BatchSceneAdapter_BuildsSceneAndPanelFromFeatureEnvelope()
        {
            // U13D 字典信封（QuickLine3dPreviewFeature 构造形态）→ 原生场景：
            // 路由管/节点球/方向标记/轴位线齐备；面板数据（回路/桥架/图框/诊断）
            // 与标签自动显隐口径（批量 ≤24 段显示）对齐退役页面。
            var envelope = new Dictionary<string, object>
            {
                ["type"] = "initialize",
                ["schemaVersion"] = 2,
                ["mode"] = "batch",
                ["scene"] = new Dictionary<string, object>
                {
                    ["rootNodeId"] = "F1_N1",
                    ["projectionMode"] = "Auto",
                    ["nodes"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["id"] = "F1_N1",
                            ["position"] = new Dictionary<string, object>
                                { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0 }
                        },
                        new Dictionary<string, object>
                        {
                            ["id"] = "F1_N2",
                            ["position"] = new Dictionary<string, object>
                                { ["x"] = 4800.0, ["y"] = 0.0, ["z"] = 0.0 }
                        }
                    },
                    ["segments"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["id"] = "F1_s1", ["frameLabel"] = "M1",
                            ["circuitName"] = "设备A",
                            ["startNodeId"] = "F1_N1", ["endNodeId"] = "F1_N2",
                            ["routeGroupId"] = "F1_R0", ["axis"] = "X",
                            ["distanceMm"] = 4800.0
                        }
                    },
                    ["directionMarkers"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["kind"] = "downstream", ["title"] = "下游",
                            ["axis"] = "48/W",
                            ["position"] = new Dictionary<string, object>
                                { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0 }
                        },
                        new Dictionary<string, object>
                        {
                            ["kind"] = "upstream", ["title"] = "上游",
                            ["axis"] = "49/X",
                            ["position"] = new Dictionary<string, object>
                                { ["x"] = 0.0, ["y"] = 4800.0, ["z"] = 0.0 }
                        }
                    },
                    ["diagnostics"] = new object[] { "测试诊断" }
                },
                ["frames"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = "F1", ["machineId"] = "M1",
                        ["deviceName"] = "设备A",
                        ["upstreamAxis"] = "49/X", ["downstreamAxis"] = "48/W",
                        ["offsetXMillimetres"] = 0.0, ["offsetYMillimetres"] = 0.0
                    }
                },
                ["bridges"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["frameId"] = "F1", ["spec"] = "梯形桥架200Wx100H",
                        ["lengthMm"] = 9600.0, ["lengthM"] = 9.6,
                        ["associated"] = true
                    }
                },
                ["axisGrid"] = new Dictionary<string, object>
                {
                    ["upstreamAxis"] = "49/X", ["downstreamAxis"] = "48/W",
                    ["spacingMillimetres"] = 4800.0,
                    ["numericMarks"] = new object[]
                    {
                        new Dictionary<string, object>
                            { ["label"] = "48", ["offsetMillimetres"] = 0.0 },
                        new Dictionary<string, object>
                            { ["label"] = "49", ["offsetMillimetres"] = 4800.0 }
                    },
                    ["alphabeticMarks"] = new object[]
                    {
                        new Dictionary<string, object>
                            { ["label"] = "W", ["offsetMillimetres"] = 0.0 },
                        new Dictionary<string, object>
                            { ["label"] = "X", ["offsetMillimetres"] = 4800.0 }
                    },
                    ["diagnostics"] = new object[0]
                },
                ["metadata"] = new Dictionary<string, object>
                {
                    ["frameCount"] = 1, ["routeFrameCount"] = 1
                },
                ["diagnostics"] = new object[] { "测试诊断" }
            };

            QuickLine3dBatchScene batch = QuickLine3dBatchScene.Build(envelope);
            Assert.NotNull(batch);
            Assert.True(batch.BatchMode);
            QuickLine3dScene scene = batch.Scene;
            Assert.True(scene.Orthographic, "U13D 批量页是正交投影");
            Assert.Contains(scene.Primitives,
                primitive => primitive.Group == QuickLine3dSceneGroupKind.Routes);
            Assert.Contains(scene.Primitives,
                primitive => primitive.Group == QuickLine3dSceneGroupKind.Direction);
            Assert.Contains(scene.Primitives,
                primitive => primitive.Group == QuickLine3dSceneGroupKind.Grid);
            // 路由聚合标签（1 条路由）+ 端点方向标签×2 + 轴位标签 + 罗盘×4。
            Assert.Equal(1, scene.Labels.Count(label => label.Kind == "route"));
            Assert.Equal(2, scene.Labels.Count(label =>
                label.Kind == "directionDown" || label.Kind == "directionUp"));
            var axes = scene.Labels.Where(label => label.Kind == "axis").ToArray();
            Assert.Equal(3, axes.Length);
            Assert.Contains(axes, label => label.Text == "48/W" && label.X == 0 && label.Y == 0);
            Assert.Contains(axes, label => label.Text == "49/W" && label.X == 4800 && label.Y == 0);
            Assert.Contains(axes, label => label.Text == "48/X" && label.X == 0 && label.Y == 4800);
            Assert.Equal(4, scene.Labels.Count(label => label.Kind == "compass"));
            // 面板：回路/桥架/图框各 1 行 + 诊断文案。
            Assert.Equal("多图框", batch.MachineId);
            Assert.Single(batch.Routes);
            Assert.Contains("设备A", batch.Routes[0].Title, StringComparison.Ordinal);
            Assert.Contains("4800", batch.Routes[0].Distance, StringComparison.Ordinal);
            Assert.Single(batch.Bridges);
            Assert.True(batch.Bridges[0].Associated);
            Assert.Single(batch.Frames);
            Assert.Contains("测试诊断", batch.Diagnostics);
            Assert.Equal("已载入多图框只读预览", batch.StatusText);
            // 批量且 ≤24 段 → 标签默认显示。
            Assert.True(batch.ShowRouteLabels);
            Assert.True(batch.ShowDirectionLabels);
        }

        [Fact]
        public void BatchSceneAdapter_NullAndEmptyInputsReturnNull()
        {
            Assert.Null(QuickLine3dBatchScene.Build(null));
            Assert.Null(QuickLine3dBatchScene.Build(
                new Dictionary<string, object>()));
            Assert.Null(QuickLine3dBatchScene.Build(
                new Dictionary<string, object>
                {
                    ["scene"] = new Dictionary<string, object>
                    {
                        ["nodes"] = new object[0]
                    }
                }));
        }

        [Fact]
        public void BatchWindow_HidesEmptyInformationSections()
        {
            WpfStaRunner.Run(delegate
            {
                var window = new QuickLine3dWindow(new Dictionary<string, object>
                {
                    ["mode"] = "batch",
                    ["scene"] = new Dictionary<string, object>
                    {
                        ["nodes"] = new object[]
                        {
                            new Dictionary<string, object>
                            {
                                ["id"] = "N1",
                                ["position"] = new Dictionary<string, object>
                                {
                                    ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0
                                }
                            }
                        },
                        ["segments"] = new object[0],
                        ["directionMarkers"] = new object[0],
                        ["diagnostics"] = new object[0]
                    },
                    ["axisGrid"] = new Dictionary<string, object>(),
                    ["metadata"] = new Dictionary<string, object>(),
                    ["bridges"] = new object[0],
                    ["frames"] = new object[0],
                    ["diagnostics"] = new object[0]
                });
                window.Show();
                Pump(2);

                Assert.Equal(Visibility.Collapsed, FindTextBlock(window, "回路").Visibility);
                Assert.Equal(Visibility.Collapsed, FindTextBlock(window, "桥架").Visibility);
                Assert.Equal(Visibility.Collapsed, FindTextBlock(window, "图框").Visibility);
                Assert.Equal(Visibility.Collapsed, FindTextBlock(window, "诊断").Visibility);
                window.Close();
            });
        }

        [Fact]
        public void ViewportControl_CursorZoomAndFullResetPreserveCameraContract()
        {
            WpfStaRunner.Run(() =>
            {
                var scene = new QuickLine3dScene { Diagonal = 4000, CameraDistance = 12000 };
                scene.AddTube(QuickLine3dSceneGroupKind.Routes, 0, 0, 0, 4000, 0, 0, 10, Colors.Red);
                var viewport = new QuickLine3dViewportControl();
                viewport.Measure(new Size(800, 600));
                viewport.Arrange(new Rect(0, 0, 800, 600));
                viewport.SetScene(scene);
                var camera = (PerspectiveCamera)viewport.Children.OfType<Viewport3D>().Single().Camera;
                Point3D original = camera.Position;
                Vector3D originalDirection = camera.LookDirection;
                viewport.OrbitBy(80, 40);
                Assert.Equal(originalDirection, camera.LookDirection);
                viewport.ZoomAt(new Point(400, 300), 0);
                Point3D cursorAnchor = CameraPlanePoint(camera, new Point(600, 200));
                viewport.ZoomAt(new Point(600, 200), 120);
                Assert.True((CameraPlanePoint(camera, new Point(600, 200)) - cursorAnchor).Length < 1e-6);
                Assert.NotEqual(originalDirection, camera.LookDirection);
                Assert.NotEqual(new Point3D(0, 0, 0), camera.Position + camera.LookDirection);
                Assert.True(camera.LookDirection.Length < originalDirection.Length);
                viewport.ResetCamera();
                Assert.True((camera.Position - original).Length < 1e-6);
                Assert.True((camera.LookDirection - originalDirection).Length < 1e-6);

                scene.Orthographic = true;
                scene.OrthoViewHeight = 6000;
                viewport.SetScene(scene);
                var ortho = (OrthographicCamera)viewport.Children.OfType<Viewport3D>().Single().Camera;
                double before = ortho.Width;
                cursorAnchor = CameraPlanePoint(ortho, new Point(600, 200));
                viewport.ZoomAt(new Point(600, 200), 120);
                Assert.True((CameraPlanePoint(ortho, new Point(600, 200)) - cursorAnchor).Length < 1e-6);
                Assert.True(ortho.Width < before);
                Assert.NotEqual(new Point3D(0, 0, 0), ortho.Position + ortho.LookDirection);
                viewport.ResetCamera();
                Assert.Equal(before, ortho.Width, 6);
                Assert.True(((ortho.Position + ortho.LookDirection) - new Point3D()).Length < 1e-6);
                viewport.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            });
        }

        private static Point3D CameraPlanePoint(ProjectionCamera camera, Point pixel)
        {
            Vector3D forward = camera.LookDirection;
            forward.Normalize();
            Vector3D right = Vector3D.CrossProduct(forward, camera.UpDirection);
            right.Normalize();
            Vector3D up = Vector3D.CrossProduct(right, forward);
            double width = camera is OrthographicCamera orthographic ? orthographic.Width
                : 2 * camera.LookDirection.Length
                    * Math.Tan(((PerspectiveCamera)camera).FieldOfView * Math.PI / 360);
            return camera.Position + camera.LookDirection
                + right * ((pixel.X - 400) * width / 800)
                + up * ((300 - pixel.Y) * width / 800);
        }

        [Fact]
        public void ViewportControl_CoalescesOrbitUntilRenderingAndStopsWhenIdle()
        {
            WpfStaRunner.Run(() =>
            {
                var viewport = new QuickLine3dViewportControl();
                viewport.SetScene(new QuickLine3dScene { Diagonal = 4000, CameraDistance = 12000 });
                var window = new Window { Content = viewport, Width = 800, Height = 600 };
                window.Show();
                Pump(2);
                var camera = (PerspectiveCamera)viewport.Children.OfType<Viewport3D>().Single().Camera;
                Vector3D original = camera.LookDirection;
                for (int i = 0; i < 50; i++) viewport.OrbitBy(1, 0);
                Assert.Equal(original, camera.LookDirection);
                Pump(3);
                Assert.NotEqual(original, camera.LookDirection);
                Vector3D rendered = camera.LookDirection;
                Pump(2);
                Assert.Equal(rendered, camera.LookDirection);
                window.Close();
            });
        }

        [Fact]
        public void ViewportControl_HiddenGeometryIsBuiltOnlyOnceWhenEnabled()
        {
            WpfStaRunner.Run(() =>
            {
                var scene = new QuickLine3dScene();
                scene.AddTube(QuickLine3dSceneGroupKind.Routes, 0, 0, 0, 100, 0, 0, 1, Colors.Red);
                scene.AddBox(QuickLine3dSceneGroupKind.Detail, 50, 0, 20, 100, 20, 20,
                    Colors.Gray, 1, Colors.Transparent);
                var viewport = new QuickLine3dViewportControl();
                viewport.SetScene(scene);
                int initial = viewport.TriangleCount;
                Assert.True(viewport.HasGroup(QuickLine3dSceneGroupKind.Detail));
                Assert.False(viewport.IsGroupVisible(QuickLine3dSceneGroupKind.Detail));
                viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Detail, true);
                Assert.True(viewport.TriangleCount > initial);
                int built = viewport.TriangleCount;
                viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Detail, false);
                viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Detail, true);
                Assert.Equal(built, viewport.TriangleCount);
            });
        }

        [Fact]
        public void OrbitControl_ReusesSameModelAndRetainsHiddenAxesBetweenFrames()
        {
            // 3.8.0：柱子/参照/结构三个开关随参照建筑一起去掉，显隐记忆只留
            // 「轴位」——同一模型不重建场景，换图框后轴位仍保持关。
            WpfStaRunner.Run(() =>
            {
                var control = new QuickLineOrbitPreviewControl();
                var model = PreviewModel("first");
                control.SetModel(model);
                control.ToggleColumnGrid();
                var viewport = FindViewport(control);
                var surface = viewport.Children.OfType<Viewport3D>().Single();
                var visuals = surface.Children.Cast<Visual3D>().ToArray();
                control.SetModel(model);
                Assert.Equal(visuals, surface.Children.Cast<Visual3D>());
                control.SetModel(PreviewModel("second"));
                Assert.False(control.ColumnGridEnabled);
                Assert.False(viewport.IsGroupVisible(QuickLine3dSceneGroupKind.Grid));
                Assert.True(viewport.IsGroupVisible(QuickLine3dSceneGroupKind.Routes));
            });
        }

        [Fact]
        public void ViewportControl_TogglesGroupsAndLabelKinds()
        {
            WpfStaRunner.Run(delegate
            {
                var scene = new QuickLine3dScene { Diagonal = 4000.0 };
                scene.AddTube(QuickLine3dSceneGroupKind.Routes,
                    0, 0, 0, 4000, 0, 0, 10.0,
                    Color_FromRgb(0xE0, 0x6C, 0x00));
                scene.AddTube(QuickLine3dSceneGroupKind.Grid,
                    0, -500, 0, 0, 500, 0, 8.0,
                    Color_FromRgb(0x55, 0x60, 0x6E));
                scene.AddBox(QuickLine3dSceneGroupKind.Reference,
                    2000, 0, 0, 4000, 1000, 200, Color_FromRgb(0x7D, 0x93, 0xA6),
                    0.45, Color_FromRgb(0xAA, 0xB6, 0xC2));
                scene.AddTube(QuickLine3dSceneGroupKind.Detail,
                    0, 0, -400, 4000, 0, -400, 60.0,
                    Color_FromRgb(0xA5, 0xB2, 0xC3));
                scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "mm",
                    "4000mm", 2000, 0, 100);
                scene.AddLabel(QuickLine3dSceneGroupKind.Grid, "axis",
                    "48", 0, -500, 0);
                scene.Centerx = 2000;
                scene.CameraDistance = 10000.0;
                scene.Near = 1.0;
                scene.Far = 1000000.0;

                var viewport = new QuickLine3dViewportControl();
                viewport.SetScene(scene);
                Viewport3D surface = viewport.Children.OfType<Viewport3D>().Single();
                Assert.All(surface.Children.OfType<ModelVisual3D>(),
                    visual => Assert.True(visual.Content.IsFrozen));
                Assert.Equal(3, viewport.MeshCount);
                Assert.Equal(2, viewport.LabelCount);
                Assert.True(viewport.IsGroupVisible(
                    QuickLine3dSceneGroupKind.Routes));
                // 结构组默认藏。
                Assert.False(viewport.IsGroupVisible(
                    QuickLine3dSceneGroupKind.Detail));
                viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Grid, false);
                Assert.False(viewport.IsGroupVisible(
                    QuickLine3dSceneGroupKind.Grid));
                viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Grid, true);
                Assert.True(viewport.IsGroupVisible(
                    QuickLine3dSceneGroupKind.Grid));
                // 标签种类过滤（U13D 路线/端点标签开关用同一入口）。
                viewport.SetLabelKindVisible("mm", false);
                viewport.SetLabelKindVisible("mm", true);
                viewport.ResetCamera();
            });
        }

        [Fact]
        public void ViewportControl_ShortRouteCanZoomOutFromFullBuilding()
        {
            WpfStaRunner.Run(delegate
            {
                var model = new QuickLineOrbitPreviewModel(new[]
                {
                    new QuickLineOrbitPreviewSegment("short",
                        new QuickLineSpatialPoint(0, 0, 0),
                        new QuickLineSpatialPoint(10, 0, 0),
                        QuickLineSpatialAxis.X, 10, true)
                }, "", new List<string>());
                var envelope = QuickLineOrbitPreviewEnvelope.Build(model,
                    PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList),
                    "2F地面");
                var viewport = new QuickLine3dViewportControl();
                viewport.SetScene(QuickLineOrbitSceneBuilder.Build(envelope));
                var surface = viewport.Children.OfType<Viewport3D>().Single();
                var camera = (PerspectiveCamera)surface.Camera;
                double before = camera.LookDirection.Length;
                viewport.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,
                    Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent });
                Assert.Equal(before * 1.15, camera.LookDirection.Length, 6);
            });
        }

        [Fact]
        public void ViewportControl_DefersHiddenLabelsAndUsesLowPolyNodes()
        {
            WpfStaRunner.Run(delegate
            {
                const int count = 1000;
                var scene = new QuickLine3dScene { Diagonal = 100000.0 };
                for (int index = 0; index < count; index++)
                {
                    scene.AddSphere(QuickLine3dSceneGroupKind.Routes,
                        index * 100.0, 0, 0, 20,
                        Color_FromRgb(0xE7, 0xEE, 0xF7));
                    scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "route",
                        "route " + index, index * 100.0, 0, 0);
                }

                var viewport = new QuickLine3dViewportControl();
                viewport.SetScene(scene, new[] { "route" });

                Assert.Equal(count, viewport.LabelCount);
                Assert.Equal(0, viewport.MaterializedLabelCount);
                Assert.Equal(count * 8, viewport.TriangleCount);

                viewport.SetLabelKindVisible("route", true);
                Assert.Equal(count, viewport.MaterializedLabelCount);
            });
        }

        [Fact]
        public void ViewportControl_ProjectsAndCentersLabelsAtTheirAnchors()
        {
            WpfStaRunner.Run(delegate
            {
                Point3D eye = new Point3D(120, -90, 75);
                Point3D target = new Point3D(10, 20, 30);
                Matrix3D view = QuickLine3dViewportControl.CreateViewMatrix(
                    eye, target);
                Point3D cameraTarget = view.Transform(target);
                Point3D cameraEye = view.Transform(eye);
                Assert.Equal(0.0, cameraTarget.X, 6);
                Assert.Equal(0.0, cameraTarget.Y, 6);
                Assert.Equal(-(eye - target).Length, cameraTarget.Z, 6);
                Assert.Equal(0.0, cameraEye.X, 6);
                Assert.Equal(0.0, cameraEye.Y, 6);
                Assert.Equal(0.0, cameraEye.Z, 6);

                var scene = new QuickLine3dScene
                {
                    Diagonal = 4000.0,
                    Centerx = 2000.0,
                    CameraDistance = 10000.0,
                    Near = 1.0,
                    Far = 1000000.0
                };
                scene.AddTube(QuickLine3dSceneGroupKind.Routes,
                    0, 0, 0, 4000, 0, 0, 10,
                    Color_FromRgb(0xE0, 0x6C, 0x00));
                scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "mm",
                    "4000mm", 2000, 0, 0);
                scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "route",
                    "route A", 2000, 0, 0);
                scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "route",
                    "route B", 2000, 0, 0);

                var viewport = new QuickLine3dViewportControl();
                var window = new Window
                {
                    Width = 420,
                    Height = 320,
                    Content = viewport,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 20,
                    Top = 20
                };
                viewport.SetScene(scene);
                window.Show();
                Pump(3);

                Canvas labels = viewport.Children.OfType<Canvas>().Single();
                // D-059：U1U 窗格的标签是「牌子」（Border 包 TextBlock），U13D
                // 批量窗仍是裸 TextBlock——两种形态都认。
                FrameworkElement mm = FindLabelElement(labels, "4000mm");
                Assert.NotNull(mm);
                TranslateTransform mmPosition =
                    (TranslateTransform)mm.RenderTransform;
                Assert.Equal(viewport.ActualWidth / 2.0,
                    mmPosition.X + mm.DesiredSize.Width / 2.0, 1);
                Assert.Equal(viewport.ActualHeight / 2.0,
                    mmPosition.Y + mm.DesiredSize.Height / 2.0, 1);

                double[] routeCenters = LabelsWithPrefix(labels, "route ")
                    .Select(element =>
                    {
                        var position =
                            (TranslateTransform)element.RenderTransform;
                        return position.Y + element.DesiredSize.Height / 2.0;
                    }).ToArray();
                Assert.Equal(2, routeCenters.Length);
                Assert.True(Math.Abs(routeCenters[0] - routeCenters[1]) >= 15.0,
                    "重叠路线标签应保留屏幕避让偏移");
                window.Close();
            });
        }

        [Fact]
        public void ViewportControl_LineLabelsSitBesideTheirLineAlongItsDirection()
        {
            // 3.8.0.2：文字贴在线上——按线段在屏幕上的投影方向排字、固定在线旁，
            // 不再被防重叠推着乱跑。相机从 −Y 正对着看：X 向右、Z 向上。
            WpfStaRunner.Run(delegate
            {
                var scene = new QuickLine3dScene
                {
                    Diagonal = 8000.0,
                    Centerx = 3500.0,
                    Centery = 2000.0,
                    Centerz = 1500.0,
                    CameraDistance = 16000.0,
                    Near = 1.0,
                    Far = 1000000.0,
                    Orbitx = 0.0,
                    Orbitsy = -1.0,
                    Orbitz = 0.0
                };
                // 水平线（沿 X）：米数在线上方、桥架牌子在线下方，都水平排字。
                scene.AddTube(QuickLine3dSceneGroupKind.Routes,
                    0, 2000, 1000, 4000, 2000, 1000, 10,
                    Color_FromRgb(0xE0, 0x6C, 0x00));
                scene.AddLineLabel(QuickLine3dSceneGroupKind.Routes, "mm",
                    "4 m", 2000, 2000, 1000, 1, 0, 0);
                scene.AddLineLabel(QuickLine3dSceneGroupKind.Routes, "tray",
                    "桥架 400 · 4m", 2000, 2000, 1000, -1, 0, 0);
                // 竖线（沿 Z）：从下往上读（−90°），放在线的左侧。
                scene.AddTube(QuickLine3dSceneGroupKind.Routes,
                    7000, 2000, 0, 7000, 2000, 3000, 10,
                    Color_FromRgb(0xE0, 0x6C, 0x00));
                scene.AddLineLabel(QuickLine3dSceneGroupKind.Routes, "mm",
                    "3 m", 7000, 2000, 1500, 0, 0, 1);

                var viewport = new QuickLine3dViewportControl();
                var window = new Window
                {
                    Width = 420,
                    Height = 320,
                    Content = viewport,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 20,
                    Top = 20
                };
                viewport.SetScene(scene);
                window.Show();
                Pump(3);

                Assert.True(viewport.TryGetLineLabel("4 m", out Point anchor,
                    out Point centre, out double angle));
                Assert.Equal(0.0, angle, 1);
                Assert.Equal(anchor.X, centre.X, 1);
                Assert.True(centre.Y < anchor.Y - 5.0,
                    "米数在线的上方一侧，实际 dy " + (centre.Y - anchor.Y));
                // 桥架牌子：同一根线，反方向给的也是同一条线 → 水平排字、线的下方。
                Assert.True(viewport.TryGetLineLabel("桥架 400 · 4m",
                    out Point trayAnchor, out Point trayCentre, out double trayAngle));
                Assert.Equal(0.0, trayAngle, 1);
                Assert.Equal(trayAnchor.X, trayCentre.X, 1);
                Assert.True(trayCentre.Y > trayAnchor.Y + 5.0,
                    "桥架牌子在线的下方一侧，实际 dy " + (trayCentre.Y - trayAnchor.Y));

                Assert.True(viewport.TryGetLineLabel("3 m", out Point riserAnchor,
                    out Point riserCentre, out double riserAngle));
                Assert.Equal(-90.0, riserAngle, 1);
                Assert.Equal(riserAnchor.Y, riserCentre.Y, 1);
                Assert.True(riserCentre.X < riserAnchor.X - 5.0,
                    "竖线的牌子在左侧，实际 dx " + (riserCentre.X - riserAnchor.X));
                window.Close();
            });
        }

        [Fact]
        public void ViewportControl_CollinearLineLabelsFlipToTheOtherSide()
        {
            // 同一条线上前后两段，牌子比线段还长：第二块的首选侧撞上了第一块，
            // 就翻到线的另一侧（仍然贴在这条线上）。
            WpfStaRunner.Run(delegate
            {
                var scene = new QuickLine3dScene
                {
                    Diagonal = 8000.0,
                    Centerx = 3000.0,
                    Centery = 2000.0,
                    Centerz = 1000.0,
                    CameraDistance = 6000.0,
                    Near = 1.0,
                    Far = 1000000.0,
                    Orbitx = 0.0,
                    Orbitsy = -1.0,
                    Orbitz = 0.0
                };
                scene.AddTube(QuickLine3dSceneGroupKind.Routes,
                    0, 2000, 1000, 1500, 2000, 1000, 10,
                    Color_FromRgb(0xE0, 0x6C, 0x00));
                scene.AddTube(QuickLine3dSceneGroupKind.Routes,
                    1500, 2000, 1000, 3000, 2000, 1000, 10,
                    Color_FromRgb(0xE0, 0x6C, 0x00));
                scene.AddLineLabel(QuickLine3dSceneGroupKind.Routes, "mm",
                    "first long label", 750, 2000, 1000, 1, 0, 0);
                scene.AddLineLabel(QuickLine3dSceneGroupKind.Routes, "mm",
                    "second long label", 2250, 2000, 1000, 1, 0, 0);

                var viewport = new QuickLine3dViewportControl();
                var window = new Window
                {
                    Width = 420,
                    Height = 320,
                    Content = viewport,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 20,
                    Top = 20
                };
                viewport.SetScene(scene);
                window.Show();
                Pump(3);

                Assert.True(viewport.TryGetLineLabel("first long label",
                    out Point first, out Point firstCentre, out _));
                Assert.True(viewport.TryGetLineLabel("second long label",
                    out Point second, out Point secondCentre, out _));
                Assert.True(firstCentre.Y < first.Y, "第一块在线上方");
                Assert.True(secondCentre.Y > second.Y,
                    "第二块翻到线的下方，实际 dy " + (secondCentre.Y - second.Y));
                window.Close();
            });
        }

        [Fact]
        public void OrbitSceneBuilder_MillimetreLabelsCarryTheirSegmentDirection()
        {
            // 场景里每块米数/整趟/软管牌子都带上自己那条线的方向，锚点就是线段中点。
            QuickLineOrbitPreviewEnvelope envelope = QuickLineOrbitPreviewEnvelope.Build(
                FloorSampleModel());
            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(envelope);
            QuickLine3dSceneLabel riser = Assert.Single(scene.Labels,
                label => label.Kind == "mm" && label.Text == "7.8 m");
            Assert.True(riser.HasDirection);
            Assert.Equal(0.0, riser.DirectionX, 6);
            Assert.Equal(0.0, riser.DirectionY, 6);
            Assert.Equal(7800.0, riser.DirectionZ, 6);
            Assert.Equal(7800.0 / 2.0, riser.Z, 6);
            QuickLine3dSceneLabel tray = Assert.Single(scene.Labels,
                label => label.Kind == "tray");
            Assert.True(tray.HasDirection);
            Assert.Equal(0.0, tray.Z, 6);
            Assert.True(scene.Labels.Where(label => label.Kind == "mm"
                    || label.Kind == "tray" || label.Kind == "conduit"
                    || label.Kind == "hose")
                .All(label => label.HasDirection));
        }

        [Fact]
        public void ViewportControl_CrowdedChipsStayVisible()
        {
            // 3.8.0.2：桥架整趟、楼层名这些牌子挤不下也照常显示（全撞时落在撞得
            // 最少的位置）——桥架牌子被藏起来会被当成「桥架没识别」。轴号边标
            // 数量多、是次要信息，照旧可以隐藏。
            WpfStaRunner.Run(delegate
            {
                var scene = new QuickLine3dScene
                {
                    Diagonal = 4000.0,
                    Centerx = 2000.0,
                    CameraDistance = 10000.0,
                    Near = 1.0,
                    Far = 1000000.0
                };
                scene.AddTube(QuickLine3dSceneGroupKind.Routes,
                    0, 0, 0, 4000, 0, 0, 10,
                    Color_FromRgb(0xE0, 0x6C, 0x00));
                for (int index = 1; index <= 14; index++)
                    scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "mm",
                        index + "000mm", 2000, 0, 0);
                scene.AddLabel(QuickLine3dSceneGroupKind.Routes, "tray",
                    "桥架 400 · 19.2m", 2000, 0, 0);
                scene.AddLabel(QuickLine3dSceneGroupKind.Reference, "floor",
                    "3F  +7.80", 2000, 0, 0);

                var viewport = new QuickLine3dViewportControl();
                var window = new Window
                {
                    Width = 420,
                    Height = 320,
                    Content = viewport,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 20,
                    Top = 20
                };
                viewport.SetScene(scene);
                window.Show();
                Pump(3);

                Canvas labels = viewport.Children.OfType<Canvas>().Single();
                foreach (string text in new[] { "桥架 400 · 19.2m", "3F  +7.80" })
                {
                    FrameworkElement chip = Assert.Single(LabelsWithText(labels, text));
                    Assert.Equal(Visibility.Visible, chip.Visibility);
                }
                window.Close();
            });
        }

        private static System.Windows.Media.Color Color_FromRgb(byte r, byte g,
            byte b)
        {
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }

        /// <summary>标签叠加层里按文字找元素（裸 TextBlock 或包着它的牌子）。</summary>
        private static FrameworkElement FindLabelElement(Canvas layer, string text)
            => LabelsWithText(layer, text).FirstOrDefault();

        private static IEnumerable<FrameworkElement> LabelsWithText(Canvas layer,
            string text)
        {
            foreach (object child in layer.Children)
            {
                if (child is TextBlock block && block.Text == text)
                    yield return block;
                else if (child is Border border && border.Child is TextBlock inner
                    && inner.Text == text)
                    yield return border;
            }
        }

        private static IEnumerable<FrameworkElement> LabelsWithPrefix(Canvas layer,
            string prefix)
        {
            foreach (object child in layer.Children)
            {
                if (child is TextBlock block && block.Text.StartsWith(prefix,
                    StringComparison.Ordinal))
                    yield return block;
                else if (child is Border border && border.Child is TextBlock inner
                    && inner.Text.StartsWith(prefix, StringComparison.Ordinal))
                    yield return border;
            }
        }

        /// <summary>
        /// 从宿主控件里取出视口。3.8.0.5 起窗口挂的是 QuickLineOrbitWebHost
        /// （WebView2 层 + WPF 兜底层），视口在兜底层里，递归查找。
        /// </summary>
        private static QuickLine3dViewportControl FindViewport(
            DependencyObject control)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(control))
            {
                if (child is QuickLine3dViewportControl viewport) return viewport;
                if (child is DependencyObject node)
                {
                    QuickLine3dViewportControl nested = FindViewport(node);
                    if (nested != null) return nested;
                }
            }
            return null;
        }

        private static TextBlock FindTextBlock(DependencyObject control,
            string text)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(control))
            {
                if (child is TextBlock block && block.Text == text) return block;
                if (child is DependencyObject nested)
                {
                    TextBlock match = FindTextBlock(nested, text);
                    if (match != null) return match;
                }
            }
            return null;
        }

    /// <summary>
    /// 共享单 STA 线程运行器（3.3.2 为 Helix 视口而立，3.3.5/3.4.0 两换内核
    /// 后保留）：WPF 窗体/主题资源在同一进程多 STA 线程间复用会炸测试宿主，
    /// WpfUiSerial 集合内全部窗体测试收敛到本运行器的单一 STA 线程。
    /// 3.4.0 起没有 WebView2 自启开关可关——原生场景构建天然无头安全。
    /// </summary>
    internal static class WpfStaRunner
    {
        private static Dispatcher _dispatcher;

        private static void Ensure()
        {
            if (_dispatcher != null) return;
            // 3.8.0.5：无头测试不启动 WebView2——预览由 WPF 兜底层照常出图，
            // 场景数据/计数/标签断言全部走同一份信封。
            QuickLineOrbitWebHost.DefaultAutoInitialize = false;
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
        public void WebHost_ForwardsModelCountersAndFallsBackToWpf()
        {
            // 3.8.0.5：新宿主＝WebView2 主渲染 + WPF 兜底。无头测试关掉页面
            // 自动初始化：SetModel 同步建 WPF 场景并转发全部数据级计数器；
            // 页面侧失败时收起浏览器、亮出原因，WPF 场景照常可用（不空白）。
            WpfStaRunner.Run(delegate
            {
                var host = new QuickLineOrbitWebHost();
                var window = new Window { Content = host, Width = 420, Height = 320 };
                window.Show();
                Pump(2);
                Assert.Null(host.Model);
                Assert.Equal(0, host.BuiltSegmentLineCount);
                Assert.NotNull(FindViewport(host));

                host.SetModel(PreviewModel("上游 48/V ↔ 下游 37/V"));
                Assert.NotNull(host.Model);
                Assert.Equal(2, host.BuiltSegmentLineCount);
                Assert.Equal(0, host.BuiltLabelCount);   // 该样例段没有毫米标
                Assert.Equal(0, host.BuiltFloorCount);   // 该样例没有楼层平面
                Assert.False(host.PageFailed);

                host.ShowFatalError("模拟：页面不可用，已改用内置渲染");
                Assert.True(host.PageFailed);
                Assert.Contains("模拟", host.ErrorBannerText);
                // 兜底场景不受影响：模型/计数/视口都还在。
                Assert.NotNull(host.Model);
                Assert.Equal(2, host.BuiltSegmentLineCount);
                Assert.NotNull(FindViewport(host));
                window.Close();
            });
        }

        [Fact]
        public void FillReviewWindow_HalfAndHalf_ListOnTheRight()
        {
            // 3.8.0.5（用户口径「对半砍，清单确认在那一半」）：窗口放大到
            // 1440×900，左右两个 Star 列各占一半；3D 最小 420px（拖不窄）；
            // 有预览时右半默认就是清单选择表格。
            WpfStaRunner.Run(delegate
            {
                var data = FillReviewData.Create(
                    new MachineRow { MachineId = "M1", CircuitName = "设备A" },
                    new List<TableFillRow>());
                var window = new FillReviewWindow(data,
                    new Core.Excel.BoqCatalogIndex(null),
                    FillPlanningOptions.Default, true,
                    PreviewModel("上游 48/V ↔ 下游 37/V"), null);
                window.Show();
                Pump(3);
                // 默认 1440×900；不超过屏幕工作区（小屏/高缩放下会自动收）。
                double expectedWidth = Math.Min(1440.0, Math.Max(960.0,
                    SystemParameters.WorkArea.Width - 32.0));
                double expectedHeight = Math.Min(900.0, Math.Max(640.0,
                    SystemParameters.WorkArea.Height - 32.0));
                Assert.Equal(expectedWidth, window.Width);
                Assert.Equal(expectedHeight, window.Height);
                Assert.Equal(GridUnitType.Star,
                    window.PreviewColumn.Width.GridUnitType);
                Assert.Equal(GridUnitType.Star,
                    window.ReviewColumn.Width.GridUnitType);
                Assert.Equal(FillReviewWindow.PreviewMinimumPixels,
                    window.PreviewColumn.MinWidth);
                Assert.True(window.PreviewColumn.MinWidth >= 420.0);
                Assert.True(window.ReviewColumn.MinWidth >= 520.0);
                Assert.Equal(1, window.ReviewTabs.SelectedIndex);
                window.Close();
            });
        }

        [Fact]
        public void WebHost_RealWebView2LoadsAndRendersTheLocalPage()
        {
            // 真 WebView2 探针（默认跳过）：本机装 Edge WebView2 Runtime 时用
            // UNCAD_PREVIEW_WEBVIEW=1 运行，验证「控制器创建 → 本地页面加载 →
            // ready → 首帧 rendered」整条链（最怕的白屏/初始化卡死在这里看）。
            if (Environment.GetEnvironmentVariable("UNCAD_PREVIEW_WEBVIEW") != "1")
                return;
            WpfStaRunner.Run(delegate
            {
                QuickLineOrbitWebHost.DefaultAutoInitialize = true;
                try
                {
                    string root = FindRepoRoot();
                    var host = new QuickLineOrbitWebHost
                    {
                        WebRootOverride = Path.Combine(root, "src", "UNCAD",
                            "Web", "QuickLineOrbit"),
                        Width = 800,
                        Height = 600
                    };
                    var window = new Window
                    {
                        Content = host, Width = 820, Height = 640
                    };
                    window.Show();
                    Pump(2);
                    host.SetModel(PreviewModel("上游 48/V ↔ 下游 37/V"));
                    DateTime deadline = DateTime.UtcNow.AddSeconds(40);
                    while (!host.PageRendered && !host.PageFailed
                        && DateTime.UtcNow < deadline)
                        Pump(1);
                    Assert.False(host.PageFailed, host.ErrorBannerText);
                    Assert.True(host.PageReady, "页面未在 40s 内就绪");
                    Assert.True(host.PageRendered, "页面没有回报首帧");
                    Assert.Equal(2, host.RenderedSegmentCount);
                    window.Close();
                }
                finally
                {
                    QuickLineOrbitWebHost.DefaultAutoInitialize = false;
                }
            });
        }

        private static string FindRepoRoot()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "UNCAD.slnx")))
                    return directory;
                directory = Path.GetDirectoryName(directory);
            }
            throw new DirectoryNotFoundException("repo root not found");
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
                // WPF-UI TitleBar handles ContentRendered; drain it before tests
                // close a shown window, or its callback can target a dead HWND.
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }
    }
}
