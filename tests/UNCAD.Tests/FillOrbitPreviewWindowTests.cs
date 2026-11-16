using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.QuickLine;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.3.0（D-054 特性③）STA 实窗测试：U1U 确认窗的左侧 3D 路由预览——
    /// 单框窗（清单确认窗）显示/隐藏预览与图层矫正报告；批量确认窗选中行
    /// 切换该图框场景；预览控件本体渲染线段、相机交互改变画面。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class FillOrbitPreviewWindowTests
    {
        [Fact]
        public void FillReviewWindow_ShowsPreviewAndLayerTextOnlyWhenProvided()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var data = FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A" },
                        new List<TableFillRow>());
                    var plain = new FillReviewWindow(data,
                        new Core.Excel.BoqCatalogIndex(null),
                        FillPlanningOptions.Default, true);
                    plain.Show();
                    Pump(2);
                    // 无预览/无报告（U1F/旧行为）：两侧附加区都隐藏。
                    Assert.Equal(Visibility.Collapsed, plain.PreviewHost.Visibility);
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
                    Assert.Equal(Visibility.Visible,
                        withPreview.LayerGovernorText.Visibility);
                    Assert.Contains("Δ11 跨", withPreview.OrbitPreview.Model.AxisSummary,
                        StringComparison.Ordinal);
                    Assert.Equal(2, withPreview.OrbitPreview.RenderedSegmentCount);
                    Assert.Contains("图层矫正", withPreview.LayerGovernorText.Text,
                        StringComparison.Ordinal);
                    withPreview.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void BatchWindow_PreviewFollowsSelectedRowAndShowsColumns()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
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

                    // 切回第一行。
                    window.BatchGrid.SelectedItem = window.BatchGrid.Items[0];
                    Pump(2);
                    Assert.Equal(previewByHandle["H1"], window.CurrentPreview);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void BatchWindow_WithoutPreviews_KeepsLegacyLayout()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
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
                    // 行为与旧版一致；连通性列默认「—」。
                    Assert.Equal(Visibility.Collapsed, window.PreviewHost.Visibility);
                    Assert.Equal(Visibility.Collapsed,
                        window.LayerGovernorText.Visibility);
                    Assert.Equal("—",
                        ((BatchFillConfirmationRow)window.BatchGrid.Items[0])
                            .ConnectivityText);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void OrbitControl_RendersAndCameraInteractionChangesScene()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
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

                    // 无模型：提示文字，0 条线段。
                    Assert.Equal(0, control.RenderedSegmentCount);

                    control.SetModel(PreviewModel("摘要"));
                    Pump(2);
                    Assert.Equal(2, control.RenderedSegmentCount);
                    Assert.NotNull(control.Model);

                    // 相机旋转后仍完整渲染（2 条），复位恢复默认朝向。
                    control.Camera.Orbit(90.0, -20.0);
                    control.ResetCamera();
                    Pump(2);
                    Assert.Equal(2, control.RenderedSegmentCount);
                    Assert.Equal(QuickLineOrbitCamera.DefaultAzimuthDegrees,
                        control.Camera.AzimuthDegrees, 6);

                    // 空场景切回提示。
                    control.SetModel(null);
                    Pump(2);
                    Assert.Equal(0, control.RenderedSegmentCount);
                    Assert.Null(control.Model);
                    host.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
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
