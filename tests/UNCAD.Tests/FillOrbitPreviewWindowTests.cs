using System;
using System.Collections.Generic;
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
                        directionResolved: true);
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
                    // 特性C：柱网线 + 数字轴刻度 + 字母轴标签 + 两端端点轴位钉。
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

                    // 隐藏柱体不影响柱网和轴号。
                    var viewport = FindViewport(control);
                    Assert.True(viewport.HasGroup(
                        QuickLine3dSceneGroupKind.Columns));
                    Assert.True(control.ColumnsEnabled);
                    control.ToggleColumns();
                    Pump(2);
                    Assert.False(control.ColumnsEnabled);
                    Assert.False(viewport.IsGroupVisible(
                        QuickLine3dSceneGroupKind.Columns));
                    Assert.True(viewport.IsGroupVisible(
                        QuickLine3dSceneGroupKind.Grid));
                    Assert.True(control.BuiltAxisTickCount >= 2);
                    Assert.True(control.BuiltLetterLabelCount >= 1);
                    control.ToggleColumns();
                    Pump(2);

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
                control.SetModel(PreviewModel("原生内核 48/V"));
                Pump(2);
                QuickLine3dViewportControl viewport = FindViewport(control);
                Assert.NotNull(viewport);
                Assert.True(viewport.MeshCount > 0, "路由网格必须同步建好");
                Assert.True(viewport.TriangleCount > 0);
                Assert.True(viewport.LabelCount >= 4,
                    "毫米标+刻度+钉标签必须同步进叠加层");
                // 双击复位口径：复位后相机仍在（复位是纯数学，不出异常即通）。
                viewport.ResetCamera();
                Pump(1);
                host.Close();
            });
        }

        [Fact]
        public void OrbitSceneBuilder_TypedReferenceModelBuildsGroups()
        {
            // 3.3.7 三轮的建筑模型公式随内核延续：typed 参照层必须产出参照组
            //（楼板/环氧/华夫/设备/母线玻璃）+ 结构组（工字钢/葡萄架，默认藏）
            // + 结构柱；一楼地面随「本层=2F地面」一起重定位。
            var model = new QuickLineOrbitPreviewModel(new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(4800, 0, 0),
                    QuickLineSpatialAxis.X, 4800, true)
            }, "上游 48/V ↔ 下游 37/V", new List<string>(), "48/V", "37/V");
            // 分隔符是**半角分号**——DefaultList 原文如此（全角「；」不在
            // 切分集里，抄摘要时带进来的全角会让整串变一个条目）。
            string rawLevels =
                "本层=2F地面;1F工字钢顶面=2850=工字钢;2F地面=7800=环氧地坪"
                + ";3F地面=15000=华夫板;第一层母线=3660=母线=2F地面"
                + ";I-L盘顶面=2200=设备=2F地面;2F葡萄架顶面=13300=葡萄架";
            // 生产路径：TryParseBaseFloor 从「本层=2F地面」指令解析出要匹配
            // 的标高标签（2F地面），不是字面量「本层」。
            Assert.True(PreviewReferenceLevels.TryParseBaseFloor(rawLevels,
                out string baseFloorLabel));
            Assert.Equal("2F地面", baseFloorLabel);
            IReadOnlyList<PreviewReferenceLevel> levels =
                PreviewReferenceLevels.Parse(rawLevels);
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model, levels,
                    baseFloorLabel);
            Assert.NotNull(envelope);
            Assert.Equal(7800.0, envelope.BaseFloorZ);
            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(envelope);
            Assert.NotNull(scene);
            Assert.Contains(scene.Primitives,
                primitive => primitive.Group == QuickLine3dSceneGroupKind.Reference);
            Assert.Contains(scene.Primitives,
                primitive => primitive.Group == QuickLine3dSceneGroupKind.Detail);
            Assert.Contains(scene.Labels,
                label => label.Text == "1F地面 -7800" && label.Z == -7800);
            Assert.Contains(scene.Primitives, primitive =>
                primitive.Kind == QuickLine3dPrimitiveKind.Box
                && primitive.Tint == Color_FromRgb(0x39, 0x42, 0x4E)
                && Math.Abs(primitive.A1z + primitive.Sizez / 2 + 7800) < 0.001);
            Assert.All(scene.Primitives.Where(primitive =>
                primitive.Kind == QuickLine3dPrimitiveKind.Box
                && primitive.Tint == Color_FromRgb(0x5D, 0x69, 0x77)),
                column => Assert.Equal(-7800, column.A1z - column.Sizez / 2));
            // 信封保留锚定后的绝对标高；场景构建阶段再减本层基准。
            Assert.Contains(envelope.ReferenceLevels,
                level => level.Kind == "bus" && Math.Abs(level.Z - 11460.0) < 0.5);
            Assert.Contains(scene.Labels,
                label => label.Text == "第一层母线 3660");
        }

        [Fact]
        public void OrbitSceneBuilder_NoBaseFloorLaysGroundPlate()
        {
            var model = new QuickLineOrbitPreviewModel(new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(4000, 0, 0),
                    QuickLineSpatialAxis.X, 4000, true)
            }, "摘要", new List<string>());
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model, null, null);
            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(envelope);
            // 无「本层」基准（1F 绝对标高口径）→ 自动铺 1F 地面。
            Assert.Contains(scene.Labels,
                label => label.Text.StartsWith("1F地面", StringComparison.Ordinal));
        }

        [Fact]
        public void OrbitSceneBuilder_WaffleCoversOnlyOccupiedBaysWithoutSampling()
        {
            var model = new QuickLineOrbitPreviewModel(
                new List<QuickLineOrbitPreviewSegment>
                {
                    new QuickLineOrbitPreviewSegment("large",
                        new QuickLineSpatialPoint(0, 0, 0),
                        new QuickLineSpatialPoint(300000, 300000, 0),
                        QuickLineSpatialAxis.X, 424264, true)
                }, "large", new List<string>());
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model,
                    PreviewReferenceLevels.Parse("3F地面=15000=华夫板"), null);

            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(envelope);
            QuickLine3dPrimitive[] waffleCells = scene.Primitives.Where(
                primitive => primitive.Kind
                    == QuickLine3dPrimitiveKind.WaffleCell).ToArray();

            QuickLinePreviewFootprint footprint = QuickLinePreviewFootprint.Plan(envelope);
            Assert.Equal(footprint.Bays.Count * 9, waffleCells.Length);
            Assert.InRange(waffleCells.Length, 1, 5400);
            Assert.DoesNotContain(waffleCells, cell =>
                cell.A1x == 148800 && cell.A1y == 9600);
            Assert.All(waffleCells.GroupBy(primitive => new
            {
                X = Math.Floor(primitive.A1x / 4800.0),
                Y = Math.Floor(primitive.A1y / 4800.0)
            }), bay => Assert.Equal(9, bay.Count()));
        }

        [Fact]
        public void OrbitSceneBuilder_AllReferenceLayersFollowOccupiedSpace()
        {
            var model = new QuickLineOrbitPreviewModel(new[]
            {
                new QuickLineOrbitPreviewSegment("x",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(96000, 0, 0),
                    QuickLineSpatialAxis.X, 96000, true),
                new QuickLineOrbitPreviewSegment("y",
                    new QuickLineSpatialPoint(96000, 0, 0),
                    new QuickLineSpatialPoint(96000, 96000, 0),
                    QuickLineSpatialAxis.Y, 96000, true)
            }, "", new List<string>());
            var envelope = QuickLineOrbitPreviewEnvelope.Build(model,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList),
                "2F地面");
            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(envelope);
            foreach (QuickLine3dPrimitive primitive in scene.Primitives)
            {
                if (primitive.Kind == QuickLine3dPrimitiveKind.Box)
                    Assert.False(Math.Abs(primitive.A1x - 48000) < primitive.Sizex / 2
                        && Math.Abs(primitive.A1y - 48000) < primitive.Sizey / 2,
                        "No floor, bus layer, column or structural detail may fill the empty interior.");
                if (primitive.Group == QuickLine3dSceneGroupKind.Grid)
                    Assert.False(Math.Abs(primitive.A1x - 48000) < 0.001
                        && Math.Abs(primitive.B1x - 48000) < 0.001
                        && Math.Min(primitive.A1y, primitive.B1y) < 48000
                        && Math.Max(primitive.A1y, primitive.B1y) > 48000);
            }
            double footprintArea = QuickLinePreviewFootprint.Plan(envelope).Bays.Count
                * 4800.0 * 4800.0;
            foreach (Color floorColor in new[]
            {
                Color_FromRgb(0x39, 0x42, 0x4E),
                Color_FromRgb(0x5F, 0x9E, 0x6E),
                Color_FromRgb(0x8F, 0xAD, 0xC0)
            })
                Assert.Equal(footprintArea, scene.Primitives.Where(primitive =>
                    primitive.Kind == QuickLine3dPrimitiveKind.Box
                    && primitive.Tint == floorColor).Sum(p => p.Sizex * p.Sizey));
            Assert.Equal(1584, scene.Primitives.Count(p =>
                p.Kind == QuickLine3dPrimitiveKind.WaffleCell));
        }

        [Fact]
        public void OrbitControl_TooLargePreviewShowsHintAndClearsOldScene()
        {
            WpfStaRunner.Run(delegate
            {
                var control = new QuickLineOrbitPreviewControl();
                control.SetModel(PreviewModel(""));
                var model = new QuickLineOrbitPreviewModel(new[]
                {
                    new QuickLineOrbitPreviewSegment("oversized",
                        new QuickLineSpatialPoint(0, 0, 0),
                        new QuickLineSpatialPoint(1000000000, 0, 0),
                        QuickLineSpatialAxis.X, 1000000000, true)
                }, "", new List<string>());
                control.SetModel(model);
                Assert.Equal(0, FindViewport(control).TriangleCount);
                Assert.Contains(control.Children.OfType<TextBlock>(),
                    text => text.Visibility == Visibility.Visible
                        && text.Text.Contains("4096"));
                control.SetModel(PreviewModel(""));
                Assert.True(FindViewport(control).TriangleCount > 0);
            });
        }

        [Fact]
        public void OrbitSceneBuilder_PlacesNineCircularWaffleHolesPerBay()
        {
            var model = new QuickLineOrbitPreviewModel(
                new List<QuickLineOrbitPreviewSegment>
                {
                    new QuickLineOrbitPreviewSegment("diagonal-bay",
                        new QuickLineSpatialPoint(0, 0, 0),
                        new QuickLineSpatialPoint(4800, 4800, 0),
                        QuickLineSpatialAxis.X, 6788, true)
                }, "bay", new List<string>());
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model,
                    PreviewReferenceLevels.Parse("3F地面=15000=华夫板"),
                    null);

            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(envelope);
            QuickLine3dPrimitive[] bayCells = scene.Primitives.Where(primitive =>
                primitive.Kind == QuickLine3dPrimitiveKind.WaffleCell
                && primitive.A1x >= 0 && primitive.B1x <= 4800
                && primitive.A1y >= 0 && primitive.B1y <= 4800).ToArray();

            Assert.Equal(9, bayCells.Length);
            Assert.All(bayCells, cell =>
            {
                Assert.True(cell.Radius > 0);
                Assert.True(cell.Radius < (cell.B1x - cell.A1x) / 2.0);
                Assert.Equal(15000.0, cell.A1z);
            });

            WpfStaRunner.Run(delegate
            {
                var geometry = new QuickLine3dViewportControl.MeshBuilder();
                geometry.Append(bayCells[0]);
                Assert.Equal(16, geometry.LastAppendTriangles);
                var built = geometry.ToBuilt(
                    new QuickLine3dViewportControl.MaterialKey(
                        bayCells[0].Tint, bayCells[0].Opacity),
                    QuickLine3dSceneGroupKind.Reference);
                Assert.Equal(16, built.Mesh.Positions.Count);
                Assert.Equal(48, built.Mesh.TriangleIndices.Count);
                Point3D center = new Point3D(
                    (bayCells[0].A1x + bayCells[0].B1x) / 2.0,
                    (bayCells[0].A1y + bayCells[0].B1y) / 2.0,
                    bayCells[0].A1z);
                for (int index = 0; index < 12; index++)
                {
                    Point3D inner = built.Mesh.Positions[index + 4];
                    Assert.Equal(bayCells[0].Radius,
                        (inner - center).Length, 6);
                }
                double area = 0;
                for (int index = 0; index < built.Mesh.TriangleIndices.Count; index += 3)
                {
                    Point3D a = built.Mesh.Positions[built.Mesh.TriangleIndices[index]];
                    Point3D b = built.Mesh.Positions[built.Mesh.TriangleIndices[index + 1]];
                    Point3D c = built.Mesh.Positions[built.Mesh.TriangleIndices[index + 2]];
                    double cross = Vector3D.CrossProduct(b - a, c - a).Z;
                    Assert.True(cross > 0, "Every face must face up and have nonzero area.");
                    area += cross / 2;
                }
                // Full square minus the regular 12-sided opening: no corner gaps.
                double squareArea = (bayCells[0].B1x - bayCells[0].A1x)
                    * (bayCells[0].B1y - bayCells[0].A1y);
                Assert.Equal(squareArea - 3 * bayCells[0].Radius * bayCells[0].Radius,
                    area, 5);
            });
        }

        [Fact]
        public void OrbitSceneBuilder_RemovesLegacyDemoCabinetsAndKeepsCustomDevices()
        {
            var model = new QuickLineOrbitPreviewModel(
                new List<QuickLineOrbitPreviewSegment>
                {
                    new QuickLineOrbitPreviewSegment("route",
                        new QuickLineSpatialPoint(4800, 2400, 0),
                        new QuickLineSpatialPoint(9600, 2400, 0),
                        QuickLineSpatialAxis.X, 4800, true)
                }, "摘要", new List<string>(), "48/V", "48/W",
                new QuickLineSpatialPoint(4800, 2400, 0),
                new QuickLineSpatialPoint(9600, 2400, 0),
                directionResolved: true);
            const string levels =
                "本层=2F地面;2F地面=7800=环氧地坪"
                + ";I-L盘高度=2200=设备=2F地面"
                + ";插座盘顶面=1000=设备=2F地面"
                + ";插座盘安装=1000=设备=2F地面"
                + ";自定义设备=800=设备=2F地面";
            PreviewReferenceLevels.TryParseBaseFloor(levels,
                out string baseFloor);
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model,
                    PreviewReferenceLevels.Parse(levels), baseFloor);

            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(envelope);
            QuickLine3dPrimitive customDevice = Assert.Single(scene.Primitives,
                primitive => primitive.Kind == QuickLine3dPrimitiveKind.Box
                    && primitive.Group == QuickLine3dSceneGroupKind.Reference
                    && primitive.Tint == Color_FromRgb(0xD9, 0xDF, 0xE6));

            Assert.Equal(800.0, customDevice.Sizez);
            Assert.Contains(scene.Labels, label =>
                label.Text == "自定义设备 800");
            Assert.DoesNotContain(scene.Labels, label =>
                label.Text.Contains("I-L盘")
                    || label.Text.Contains("I-Line盘")
                    || label.Text.Contains("插座盘"));
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
        public void OrbitControl_ReusesSameModelAndRetainsHiddenColumnsBetweenFrames()
        {
            WpfStaRunner.Run(() =>
            {
                var control = new QuickLineOrbitPreviewControl();
                var model = PreviewModel("first");
                control.SetModel(model);
                control.ToggleColumns();
                var viewport = FindViewport(control);
                var surface = viewport.Children.OfType<Viewport3D>().Single();
                var visuals = surface.Children.Cast<Visual3D>().ToArray();
                control.SetModel(model);
                Assert.Equal(visuals, surface.Children.Cast<Visual3D>());
                control.SetModel(PreviewModel("second"));
                Assert.False(control.ColumnsEnabled);
                Assert.False(viewport.IsGroupVisible(QuickLine3dSceneGroupKind.Columns));
                Assert.True(viewport.IsGroupVisible(QuickLine3dSceneGroupKind.Grid));
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
                TextBlock mm = labels.Children.OfType<TextBlock>()
                    .Single(element => element.Text == "4000mm");
                TranslateTransform mmPosition =
                    (TranslateTransform)mm.RenderTransform;
                Assert.Equal(viewport.ActualWidth / 2.0,
                    mmPosition.X + mm.DesiredSize.Width / 2.0, 1);
                Assert.Equal(viewport.ActualHeight / 2.0,
                    mmPosition.Y + mm.DesiredSize.Height / 2.0, 1);

                double[] routeCenters = labels.Children.OfType<TextBlock>()
                    .Where(element => element.Text.StartsWith("route ",
                        StringComparison.Ordinal))
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

        private static System.Windows.Media.Color Color_FromRgb(byte r, byte g,
            byte b)
        {
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }

        /// <summary>从宿主控件里取出视口（模板树直达）。</summary>
        private static QuickLine3dViewportControl FindViewport(
            QuickLineOrbitPreviewControl control)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(control))
            {
                if (child is QuickLine3dViewportControl viewport) return viewport;
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
