using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UNCAD.Core.QuickLine;

namespace UNCAD.UI
{
    /// <summary>
    /// 3.3.0（D-054 特性③）：U1U 确认窗左侧的 3D 路由预览——WPF Canvas 自绘
    /// （**禁止第三方 3D 库**）。鼠标左键拖动=轨道旋转、滚轮=缩放、双击=复位；
    /// 线段按轴着色（X 橙 / Y 绿 / Z 蓝），底部显示轴位摘要。只绘制传入的
    /// <see cref="QuickLineOrbitPreviewModel"/>（场景/相机模型由 Features 阶段
    /// 构建后传入——分层铁律：窗体不重建场景）。
    /// </summary>
    public sealed class QuickLineOrbitPreviewControl : System.Windows.Controls.Canvas
    {
        // 静态画笔必须 Freeze：Freezable 属于创建它的线程，STA 测试线程与
        // AutoCAD UI 线程不同会抛跨线程异常；冻结后全线程可用。
        private static readonly Brush AxisXBrush = FrozenBrush(0xE0, 0x6C, 0x00); // 橙
        private static readonly Brush AxisYBrush = FrozenBrush(0x2E, 0x9E, 0x5B); // 绿
        private static readonly Brush AxisZBrush = FrozenBrush(0x3B, 0x6E, 0xD0); // 蓝
        private static readonly Brush HintBrush = FrozenBrush(0x8A, 0x8A, 0x8A);

        private static Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private readonly QuickLineOrbitCamera _camera =
            QuickLineOrbitCamera.Create();
        private QuickLineOrbitPreviewModel _model;
        private Point _dragOrigin;
        private double _dragAzimuth;
        private double _dragElevation;
        private bool _dragging;

        public QuickLineOrbitPreviewControl()
        {
            ClipToBounds = true;
            Background = Brushes.Transparent;
            Focusable = true;
            MouseLeftButtonDown += OnOrbitStart;
            MouseMove += OnOrbitMove;
            MouseLeftButtonUp += OnOrbitEnd;
            MouseWheel += OnZoomWheel;
            MouseLeave += OnOrbitEnd;
            SizeChanged += (sender, args) => Render();
        }

        /// <summary>当前预览的场景（测试/宿主可读；为 null 时显示提示）。</summary>
        public QuickLineOrbitPreviewModel Model => _model;

        /// <summary>相机（测试驱动旋转/缩放用）。</summary>
        internal QuickLineOrbitCamera Camera => _camera;

        /// <summary>当前画面上的线段条数（STA 测试断言用）。</summary>
        internal int RenderedSegmentCount { get; private set; }

        /// <summary>切换场景（含 null=隐藏为提示文字）。复位相机并重绘。</summary>
        public void SetModel(QuickLineOrbitPreviewModel model)
        {
            _model = model;
            _camera.Reset();
            Render();
        }

        /// <summary>复位相机并重绘（双击同路径）。</summary>
        internal void ResetCamera()
        {
            _camera.Reset();
            Render();
        }

        private void OnOrbitStart(object sender, MouseButtonEventArgs e)
        {
            // 双击 = 复位（Panel 没有 MouseDoubleClick 事件，用 ClickCount 判定）。
            if (e.ClickCount >= 2)
            {
                ResetCamera();
                e.Handled = true;
                return;
            }
            _dragging = true;
            _dragOrigin = e.GetPosition(this);
            _dragAzimuth = _camera.AzimuthDegrees;
            _dragElevation = _camera.ElevationDegrees;
            CaptureMouse();
            e.Handled = true;
        }

        private void OnOrbitMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point position = e.GetPosition(this);
            // 屏幕向右拖 = 方位角增大（绕场景逆时针）；向下拖 = 俯仰降低。
            // 用绝对角度驱动（拖动起点 + 位移），避免累积误差；缩放不受影响。
            _camera.SetOrientation(
                _dragAzimuth + (position.X - _dragOrigin.X) * 0.4,
                _dragElevation - (position.Y - _dragOrigin.Y) * 0.4);
            Render();
            e.Handled = true;
        }

        private void OnOrbitEnd(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
        }

        private void OnZoomWheel(object sender, MouseWheelEventArgs e)
        {
            _camera.ZoomBy(e.Delta > 0 ? 1.15 : 1.0 / 1.15);
            Render();
            e.Handled = true;
        }

        private void Render()
        {
            Children.Clear();
            RenderedSegmentCount = 0;
            if (_model == null || _model.Segments.Count == 0)
            {
                var hint = new System.Windows.Controls.TextBlock
                {
                    Text = "该图框没有可预览的带毫米标注 U1L 路由",
                    Foreground = HintBrush,
                    FontSize = 12
                };
                SetLeft(hint, 12);
                SetTop(hint, 12);
                Children.Add(hint);
                return;
            }

            // 投影（相机数学在 Core，控件只消费）。
            var projected = new List<Tuple<QuickLinePoint, QuickLinePoint, Brush>>();
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (QuickLineOrbitPreviewSegment segment in _model.Segments)
            {
                QuickLinePoint start = _camera.Project(segment.Start);
                QuickLinePoint end = _camera.Project(segment.End);
                Brush brush = BrushFor(segment.Axis);
                projected.Add(Tuple.Create(start, end, brush));
                CollectBounds(start, ref minX, ref maxX, ref minY, ref maxY);
                CollectBounds(end, ref minX, ref maxX, ref minY, ref maxY);
            }

            double width = Math.Max(1.0, ActualWidth);
            double height = Math.Max(1.0, ActualHeight);
            const double margin = 18.0;
            double spanX = Math.Max(maxX - minX, 1e-6);
            double spanY = Math.Max(maxY - minY, 1e-6);
            double fit = Math.Min((width - margin * 2.0) / spanX,
                (height - margin * 2.0 - 26.0) / spanY);
            if (double.IsNaN(fit) || double.IsInfinity(fit) || fit <= 0) fit = 1.0;
            double centreX = (minX + maxX) / 2.0;
            double centreY = (minY + maxY) / 2.0;

            foreach (Tuple<QuickLinePoint, QuickLinePoint, Brush> item in projected)
            {
                var line = new Line
                {
                    X1 = width / 2.0 + (item.Item1.X - centreX) * fit,
                    Y1 = height / 2.0 - (item.Item1.Y - centreY) * fit,
                    X2 = width / 2.0 + (item.Item2.X - centreX) * fit,
                    Y2 = height / 2.0 - (item.Item2.Y - centreY) * fit,
                    Stroke = item.Item3,
                    StrokeThickness = 2.0
                };
                Children.Add(line);
                RenderedSegmentCount++;
            }

            // 底部摘要 + 轴色图例 + 操作提示。
            var summary = new System.Windows.Controls.TextBlock
            {
                Text = (_model.AxisSummary.Length > 0 ? _model.AxisSummary + "　" : "")
                    + "拖动=旋转 · 滚轮=缩放 · 双击=复位",
                Foreground = HintBrush,
                FontSize = 11
            };
            SetLeft(summary, 10);
            SetTop(summary, Math.Max(2.0, height - 22.0));
            Children.Add(summary);
        }

        private static Brush BrushFor(QuickLineSpatialAxis axis)
        {
            switch (axis)
            {
                case QuickLineSpatialAxis.X: return AxisXBrush;
                case QuickLineSpatialAxis.Y: return AxisYBrush;
                default: return AxisZBrush;
            }
        }

        private static void CollectBounds(QuickLinePoint point,
            ref double minX, ref double maxX, ref double minY, ref double maxY)
        {
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        }
    }
}
