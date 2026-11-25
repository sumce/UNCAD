using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UNCAD.Core.QuickLine;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1U 3D 预览窗格截图器（3.8.0）：设置环境变量 UNCAD_PREVIEW_SHOTS 后按
    /// --filter PreviewLookScreenshot 运行，把一份贴近现场的样例场景渲染成 PNG
    /// （未设置时立即通过，不影响常规测试）。给轴位/米数/楼层平面的视觉调优用——
    /// 3D 视图的观感只有真渲染出来才看得出来。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class PreviewLookScreenshotTests
    {
        [Trait("Category", "Manual")]
        [Fact]
        public void GeneratePreviewLookScreenshots()
        {
            string target = Environment.GetEnvironmentVariable("UNCAD_PREVIEW_SHOTS");
            if (string.IsNullOrWhiteSpace(target)) return;
            if (target == "1")
                target = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "tmp", "ui-shots"));
            Directory.CreateDirectory(target);
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { Generate(target); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        /// <summary>
        /// 样例：2F 上游 I-Line 盘 → 桥架 → 立管上 3F → 线管 → 设备。
        /// 上游 48/V、设备 37/V。插座盘时整条线路按真实链路抬高 1 m
        /// （上游末端落在 2F 地面 + 1 m）。
        /// </summary>
        internal static QuickLineOrbitPreviewModel SampleModel(
            PreviewCabinetKind cabinet = PreviewCabinetKind.ILinePanel)
        {
            double lift = cabinet == PreviewCabinetKind.SocketPanel
                ? PreviewCabinet.SocketPanelMountingMillimetres : 0.0;
            return new QuickLineOrbitPreviewModel(new[]
            {
                Segment("s1", 0, 0, lift, 14400, 0, lift, QuickLineSpatialAxis.X, 14400, "14400mm",
                    new QuickLineOrbitPreviewChannel(400.0, false, false)),
                Segment("s2", 14400, 0, lift, 14400, 4800, lift, QuickLineSpatialAxis.Y, 4800, "4800mm",
                    new QuickLineOrbitPreviewChannel(400.0, false, false)),
                Segment("s3", 14400, 4800, lift, 14400, 4800, 7800 + lift, QuickLineSpatialAxis.Z, 7800, "7800mm"),
                Segment("s4", 14400, 4800, 7800 + lift, 14400, 14400, 7800 + lift, QuickLineSpatialAxis.Y, 9600, "9600mm",
                    new QuickLineOrbitPreviewChannel(null, false, true)),
                Segment("s5", 14400, 14400, 7800 + lift, 4800, 14400, 7800 + lift, QuickLineSpatialAxis.X, 9600, "9600mm")
            }, "上游 48/V ↔ 下游 37/V · Δ11 跨 × 4.8m ≈ 52.8m", new List<string>(),
                "48/V", "37/V",
                new QuickLineSpatialPoint(0, 0, lift),
                new QuickLineSpatialPoint(4800, 14400, 7800 + lift),
                directionResolved: true,
                hose: new QuickLineOrbitPreviewHose(2500.0, 100.0, "Ruanguan 块", false),
                recognitionSummary: "识别 5 段带毫米标线段 · 桥架 1 趟 19.2m · 线管 1 趟 9.6m · 软管 2.5m",
                routeIssues: new[] { "1 段线段没有毫米标：2A3B" },
                floors: PreviewFloorPlan.Plan("2F", "3F", "本层=2F地面",
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

        private static QuickLineOrbitPreviewSegment Segment(string id, double x1, double y1,
            double z1, double x2, double y2, double z2, QuickLineSpatialAxis axis,
            double millimetres, string label, QuickLineOrbitPreviewChannel channel = null)
            => new QuickLineOrbitPreviewSegment(id,
                new QuickLineSpatialPoint(x1, y1, z1), new QuickLineSpatialPoint(x2, y2, z2),
                axis, millimetres, true, label, channel);

        private static void Generate(string dir)
        {
            var control = new QuickLineOrbitPreviewControl();
            var host = new Window
            {
                Content = control,
                Width = 780,
                Height = 720,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            host.Show();
            Pump(4);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            control.SetModel(SampleModel());
            clock.Stop();
            long firstMs = clock.ElapsedMilliseconds;
            Pump(8);
            Thread.Sleep(900);
            Pump(4);
            Capture(control, Path.Combine(dir, "preview-sample.png"));
            // 插座盘样例：离地 1 m 的支架和柜顶标签（3.8.0.1 观感核对）。
            control.SetModel(SampleModel(PreviewCabinetKind.SocketPanel));
            Pump(6);
            Thread.Sleep(500);
            Pump(3);
            Capture(control, Path.Combine(dir, "preview-socket.png"));

            // 度量：建场景耗时 + 网格/三角形/标签数（卡不卡看这几个数）。
            QuickLine3dViewportControl viewport = FindViewport(control);
            // 第二次换模型：WPF 首次建视口的固定开销已付过，这才是「建场景」本身的耗时。
            clock.Restart();
            control.SetModel(SampleModel());
            clock.Stop();
            long warmMs = clock.ElapsedMilliseconds;
            File.WriteAllLines(Path.Combine(dir, "preview-metrics.txt"), new[]
            {
                "setModelFirstMs=" + firstMs,
                "setModelWarmMs=" + warmMs,
                "meshes=" + (viewport?.MeshCount ?? -1),
                "triangles=" + (viewport?.TriangleCount ?? -1),
                "labels=" + (viewport?.LabelCount ?? -1)
            });
            // 牌子清单（文字/显隐/屏幕偏移）：哪块牌子被藏了一眼能看出来。
            control.SetModel(SampleModel(PreviewCabinetKind.SocketPanel));
            Pump(4);
            File.WriteAllLines(Path.Combine(dir, "preview-labels.txt"),
                DescribeLabels(control));
            host.Close();
        }

        private static IEnumerable<string> DescribeLabels(
            QuickLineOrbitPreviewControl control)
        {
            QuickLine3dViewportControl viewport = FindViewport(control);
            Canvas layer = viewport?.Children.OfType<Canvas>().FirstOrDefault();
            if (layer == null)
            {
                yield return "no label layer";
                yield break;
            }
            foreach (object child in layer.Children)
            {
                if (!(child is FrameworkElement element)) continue;
                var text = element as TextBlock
                    ?? (element as Border)?.Child as TextBlock;
                var transform = element.RenderTransform as TranslateTransform;
                yield return (element.Visibility == Visibility.Visible ? "shown  "
                        : "HIDDEN ") + "[" + text?.Text + "] w="
                    + element.DesiredSize.Width.ToString("0")
                    + " x=" + (transform?.X ?? double.NaN).ToString("0")
                    + " y=" + (transform?.Y ?? double.NaN).ToString("0");
            }
        }

        private static QuickLine3dViewportControl FindViewport(DependencyObject node)
        {
            if (node is QuickLine3dViewportControl viewport) return viewport;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            {
                QuickLine3dViewportControl found = FindViewport(
                    VisualTreeHelper.GetChild(node, index));
                if (found != null) return found;
            }
            return null;
        }

        private static void Capture(FrameworkElement element, string path)
        {
            int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
                encoder.Save(stream);
        }

        private static void Pump(int rounds)
        {
            for (int round = 0; round < rounds; round++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(40);
            }
        }
    }
}
