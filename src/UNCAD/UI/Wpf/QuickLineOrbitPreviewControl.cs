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
    ///
    /// 3.3.1（D-054 Amendment）四项预览增强（用户现场反馈）：
    /// - 特性B「每条线也没有米数标记」：每段中点标毫米原文（固定屏幕字号，
    ///   投影过短自动隐藏——判定在 Core 的 QuickLineOrbitLabelPolicy）；
    /// - 特性C「3D预览内展示柱位 你没展示」：地面柱网（4800mm 柱距，可开关）+
    ///   路由两端端点轴位标签 + 数字轴插值刻度（规划在 Core 的
    ///   QuickLineColumnGridPlanner，控件只画；解析失败显示原文不猜）；
    /// - 特性D「3D预览没有展示桥架」：桥架段=电缆线两侧红色平行线（偏移=宽/2
    ///   且投影后至少 6px，随缩放自适应）+浅红半透明填充带；线管段=青色双线
    ///   细间距不加填充；角落图例注明五色语义；
    /// - 窗口对半分由宿主窗体（FillReviewWindow/BatchFillConfirmationWindow）
    ///   的 GridSplitter 承担，本控件 Fill 占满预览列。
    /// </summary>
    public sealed class QuickLineOrbitPreviewControl : System.Windows.Controls.Canvas
    {
        // 静态画笔必须 Freeze：Freezable 属于创建它的线程，STA 测试线程与
        // AutoCAD UI 线程不同会抛跨线程异常；冻结后全线程可用。
        private static readonly Brush AxisXBrush = FrozenBrush(0xE0, 0x6C, 0x00); // 橙
        private static readonly Brush AxisYBrush = FrozenBrush(0x2E, 0x9E, 0x5B); // 绿
        private static readonly Brush AxisZBrush = FrozenBrush(0x3B, 0x6E, 0xD0); // 蓝
        private static readonly Brush HintBrush = FrozenBrush(0x8A, 0x8A, 0x8A);
        // 3.3.1 特性C：柱网/刻度（浅色细线，压在线条之下）。
        private static readonly Brush ColumnGridBrush = FrozenBrush(0xC8, 0xCC, 0xD4);
        private static readonly Brush AxisTickBrush = FrozenBrush(0x70, 0x74, 0x7C);
        private static readonly Brush EndpointPinBrush = FrozenBrush(0x3A, 0x3E, 0x46);
        // 3.3.1 特性D：桥架红 / 线管青（颜色常量集中定义）。
        private static readonly Brush TrayBrush = FrozenBrush(0xE8, 0x44, 0x3C);
        private static readonly Brush TrayFillBrush = FrozenBrush(0xE8, 0x44, 0x3C, 0x2E);
        private static readonly Brush ConduitBrush = FrozenBrush(0x2E, 0xA8, 0xC0);
        // 3.3.1 特性B：段标签（固定字号+浅底保证在线条上可读）。
        private static readonly Brush LabelForegroundBrush = FrozenBrush(0x1E, 0x22, 0x28);
        private static readonly Brush LabelBackgroundBrush = FrozenBrush(0xFF, 0xFF, 0xFF, 0xB4);

        /// <summary>桥架双线投影后的最小半宽（px，随缩放自适应但不低于此值）。</summary>
        private const double MinimumTrayOffsetPixels = 6.0;
        /// <summary>线管双线的固定细间距半宽（px）。</summary>
        private const double ConduitOffsetPixels = 4.0;

        private static Brush FrozenBrush(byte r, byte g, byte b, byte a = 0xFF)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
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
        private bool _columnGridEnabled = true;

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
        /// <summary>3.3.1（特性B）：当前画出的段毫米标标签数。</summary>
        internal int RenderedLabelCount { get; private set; }
        /// <summary>3.3.1（特性C）：当前画出的柱网线数（开关关掉=0）。</summary>
        internal int RenderedColumnGridLineCount { get; private set; }
        /// <summary>3.3.1（特性C）：当前画出的数字轴刻度数。</summary>
        internal int RenderedAxisTickCount { get; private set; }
        /// <summary>3.3.1（特性D）：当前画出的桥架双线通道段数。</summary>
        internal int RenderedTrayChannelCount { get; private set; }
        /// <summary>3.3.1（特性D）：当前画出的线管双线通道段数。</summary>
        internal int RenderedConduitChannelCount { get; private set; }

        /// <summary>3.3.1（特性C）：柱网开关状态（默认开）。</summary>
        internal bool ColumnGridEnabled => _columnGridEnabled;

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

        /// <summary>3.3.1（特性C）：柱网显示开关（角落小按钮同路径）。</summary>
        internal void ToggleColumnGrid()
        {
            _columnGridEnabled = !_columnGridEnabled;
            Render();
        }

        /// <summary>重绘一次（测试直接驱动相机后校验画面用）。</summary>
        internal void RenderNow() => Render();

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
            RenderedLabelCount = 0;
            RenderedColumnGridLineCount = 0;
            RenderedAxisTickCount = 0;
            RenderedTrayChannelCount = 0;
            RenderedConduitChannelCount = 0;
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
            var projected = new List<ProjectedSegment>(_model.Segments.Count);
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (QuickLineOrbitPreviewSegment segment in _model.Segments)
            {
                QuickLinePoint start = _camera.Project(segment.Start);
                QuickLinePoint end = _camera.Project(segment.End);
                Brush brush = BrushFor(segment.Axis);
                projected.Add(new ProjectedSegment(segment, start, end, brush));
                CollectBounds(start, ref minX, ref maxX, ref minY, ref maxY);
                CollectBounds(end, ref minX, ref maxX, ref minY, ref maxY);
            }

            double width = Math.Max(1.0, ActualWidth);
            double height = Math.Max(1.0, ActualHeight);
            const double margin = 18.0;
            // 3.3.1：底部两行文字（轴位摘要 + 图例/操作提示）。
            const double bottomBand = 46.0;
            double spanX = Math.Max(maxX - minX, 1e-6);
            double spanY = Math.Max(maxY - minY, 1e-6);
            double fit = Math.Min((width - margin * 2.0) / spanX,
                (height - margin * 2.0 - bottomBand) / spanY);
            if (double.IsNaN(fit) || double.IsInfinity(fit) || fit <= 0) fit = 1.0;
            double centreX = (minX + maxX) / 2.0;
            double centreY = (minY + maxY) / 2.0;
            // 3.3.1：滚轮缩放必须真的缩放——autofit 按当前投影包络算 fit 会把
            // 相机 Zoom 整除掉（3.3.0 的滚轮在视觉上是空操作）。fit 只按
            // zoom=1 的包络当量计算，之后统一乘 Zoom：复位（Zoom=1）画面与
            // 3.3.0 完全一致，缩小时段投影变短→标签按 Core 阈值隐藏，放大时
            // 桥架双线偏移随之加宽。
            double scale = fit * _camera.Zoom;
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
                scale = fit;
            // 场景 mm → 屏幕像素的最终换算系数（投影已含 Zoom，这里再乘一次：
            // 桥架宽度偏移这类由毫米值直出的屏幕量用它，随缩放自适应）。
            double millimetresToPixels = scale * _camera.Zoom;

            QuickLineColumnGrid grid = QuickLineColumnGridPlanner.Plan(
                _model.Segments, _model.UpstreamAxisLabel,
                _model.DownstreamAxisLabel);

            // ① 特性C：地面柱网 + 数字轴刻度（画在最底层）。
            if (_columnGridEnabled && grid != null)
                RenderColumnGrid(grid, centreX, centreY, scale);

            // ② 特性C：路由两端端点轴位标签（原文，解析失败不猜）。
            if (grid != null)
                RenderEndpointPins(grid, centreX, centreY, scale);

            // ③ 特性D：桥架/线管双线通道（填充带先画，线后画，电缆线最上）。
            bool anyAssumedTrayWidth = false;
            foreach (ProjectedSegment item in projected)
            {
                QuickLineOrbitPreviewChannel channel = item.Segment.Channel;
                if (channel == null) continue;
                if (channel.TrayWidthMillimetres.HasValue)
                {
                    RenderTrayChannel(item, centreX, centreY, scale,
                        millimetresToPixels);
                    RenderedTrayChannelCount++;
                    if (channel.TrayWidthAssumedDefault) anyAssumedTrayWidth = true;
                }
                else if (channel.HasConduit)
                {
                    RenderConduitChannel(item, centreX, centreY, scale);
                    RenderedConduitChannelCount++;
                }
            }

            // ④ 电缆线本体（按轴着色）+ 特性B 段中点毫米标。
            foreach (ProjectedSegment item in projected)
            {
                Point start = ToScreen(item.Start, centreX, centreY, scale);
                Point end = ToScreen(item.End, centreX, centreY, scale);
                var line = new Line
                {
                    X1 = start.X,
                    Y1 = start.Y,
                    X2 = end.X,
                    Y2 = end.Y,
                    Stroke = item.Brush,
                    StrokeThickness = 2.0
                };
                Children.Add(line);
                RenderedSegmentCount++;

                string labelText = item.Segment.LabelText;
                if (labelText.Length == 0) continue;
                double pixelSpan = Math.Sqrt(
                    (end.X - start.X) * (end.X - start.X)
                    + (end.Y - start.Y) * (end.Y - start.Y));
                if (!QuickLineOrbitLabelPolicy.ShouldDrawLabel(pixelSpan)) continue;
                AddCentredLabel(labelText,
                    new Point((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0));
                RenderedLabelCount++;
            }

            // ⑤ 底部摘要 + 图例 + 操作提示（AxisSummary 那行动态文字保留）。
            AddBottomTexts(anyAssumedTrayWidth, height);
            // ⑥ 特性C：柱网开关（右上角小按钮）。
            AddColumnGridToggle(width);
        }

        // ---- 特性C：柱网 / 刻度 / 端点轴位 -------------------------------------

        private void RenderColumnGrid(QuickLineColumnGrid grid,
            double centreX, double centreY, double scale)
        {
            double tickBaseY = TickBaseY(grid);
            foreach (QuickLineColumnGridLine line in grid.Lines)
            {
                QuickLineSpatialPoint from = line.ConstantX
                    ? new QuickLineSpatialPoint(line.FixedValue, line.Minimum, 0.0)
                    : new QuickLineSpatialPoint(line.Minimum, line.FixedValue, 0.0);
                QuickLineSpatialPoint to = line.ConstantX
                    ? new QuickLineSpatialPoint(line.FixedValue, line.Maximum, 0.0)
                    : new QuickLineSpatialPoint(line.Maximum, line.FixedValue, 0.0);
                Point start = ToScreen(_camera.Project(from), centreX, centreY, scale);
                Point end = ToScreen(_camera.Project(to), centreX, centreY, scale);
                Children.Add(new Line
                {
                    X1 = start.X,
                    Y1 = start.Y,
                    X2 = end.X,
                    Y2 = end.Y,
                    Stroke = ColumnGridBrush,
                    StrokeThickness = 1.0
                });
                RenderedColumnGridLineCount++;
            }
            // 数字轴刻度：柱网线与包络下沿的交点处标数字（48、47、46…）。
            foreach (QuickLineColumnGridTick tick in grid.Ticks)
            {
                Point anchor = ToScreen(
                    _camera.Project(new QuickLineSpatialPoint(tick.X, tickBaseY, 0.0)),
                    centreX, centreY, scale);
                var text = new System.Windows.Controls.TextBlock
                {
                    Text = tick.Number.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    Foreground = AxisTickBrush,
                    FontSize = 11
                };
                text.Measure(new Size(double.PositiveInfinity,
                    double.PositiveInfinity));
                SetLeft(text, anchor.X - text.DesiredSize.Width / 2.0);
                SetTop(text, anchor.Y + 4.0);
                Children.Add(text);
                RenderedAxisTickCount++;
            }
        }

        /// <summary>刻度文字贴在柱网的下沿（常量 X 线的最小 Y 端）。</summary>
        private static double TickBaseY(QuickLineColumnGrid grid)
        {
            foreach (QuickLineColumnGridLine line in grid.Lines)
                if (line.ConstantX) return line.Minimum;
            return 0.0;
        }

        private void RenderEndpointPins(QuickLineColumnGrid grid,
            double centreX, double centreY, double scale)
        {
            RenderEndpointPin(grid.UpstreamEndpoint,
                TrimAxisLabel(_model.UpstreamAxisLabel), centreX, centreY, scale);
            RenderEndpointPin(grid.DownstreamEndpoint,
                TrimAxisLabel(_model.DownstreamAxisLabel), centreX, centreY, scale);
        }

        /// <summary>端点上方钉轴位标签（小圆点标记端点；原文为空不画）。</summary>
        private void RenderEndpointPin(QuickLineSpatialPoint endpoint, string label,
            double centreX, double centreY, double scale)
        {
            if (label.Length == 0) return;
            Point anchor = ToScreen(_camera.Project(endpoint), centreX, centreY, scale);
            var dot = new Ellipse
            {
                Width = 4.0,
                Height = 4.0,
                Fill = EndpointPinBrush
            };
            SetLeft(dot, anchor.X - 2.0);
            SetTop(dot, anchor.Y - 2.0);
            Children.Add(dot);
            var text = new System.Windows.Controls.TextBlock
            {
                Text = label,
                Foreground = EndpointPinBrush,
                Background = LabelBackgroundBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(text, anchor.X - text.DesiredSize.Width / 2.0);
            SetTop(text, anchor.Y - text.DesiredSize.Height - 8.0);
            Children.Add(text);
        }

        // ---- 特性D：桥架/线管双线通道 -------------------------------------------

        private void RenderTrayChannel(ProjectedSegment item, double centreX,
            double centreY, double scale, double millimetresToPixels)
        {
            Point start = ToScreen(item.Start, centreX, centreY, scale);
            Point end = ToScreen(item.End, centreX, centreY, scale);
            Vector direction = end - start;
            double length = direction.Length;
            if (length < 1e-6) return;
            Vector normal = new Vector(-direction.Y / length, direction.X / length);
            double widthMm = item.Segment.Channel.TrayWidthMillimetres.Value;
            // 偏移=桥架宽度/2，随缩放自适应，但投影后至少 6px 保可读。
            double offset = Math.Max(MinimumTrayOffsetPixels,
                widthMm / 2.0 * millimetresToPixels);
            Point a1 = start + normal * offset;
            Point a2 = end + normal * offset;
            Point b1 = start - normal * offset;
            Point b2 = end - normal * offset;
            // 浅红半透明填充带。
            var fill = new Polygon
            {
                Points = new PointCollection { a1, a2, b2, b1 },
                Fill = TrayFillBrush
            };
            Children.Add(fill);
            // 电缆线两侧两条红色平行线。
            Children.Add(new Line
            {
                X1 = a1.X, Y1 = a1.Y, X2 = a2.X, Y2 = a2.Y,
                Stroke = TrayBrush,
                StrokeThickness = 1.5
            });
            Children.Add(new Line
            {
                X1 = b1.X, Y1 = b1.Y, X2 = b2.X, Y2 = b2.Y,
                Stroke = TrayBrush,
                StrokeThickness = 1.5
            });
        }

        private void RenderConduitChannel(ProjectedSegment item, double centreX,
            double centreY, double scale)
        {
            Point start = ToScreen(item.Start, centreX, centreY, scale);
            Point end = ToScreen(item.End, centreX, centreY, scale);
            Vector direction = end - start;
            double length = direction.Length;
            if (length < 1e-6) return;
            Vector normal = new Vector(-direction.Y / length, direction.X / length);
            double offset = ConduitOffsetPixels;
            Point a1 = start + normal * offset;
            Point a2 = end + normal * offset;
            Point b1 = start - normal * offset;
            Point b2 = end - normal * offset;
            Children.Add(new Line
            {
                X1 = a1.X, Y1 = a1.Y, X2 = a2.X, Y2 = a2.Y,
                Stroke = ConduitBrush,
                StrokeThickness = 1.5
            });
            Children.Add(new Line
            {
                X1 = b1.X, Y1 = b1.Y, X2 = b2.X, Y2 = b2.Y,
                Stroke = ConduitBrush,
                StrokeThickness = 1.5
            });
        }

        // ---- 杂项 ---------------------------------------------------------------

        private void AddCentredLabel(string text, Point centre)
        {
            var label = new System.Windows.Controls.TextBlock
            {
                Text = text,
                Foreground = LabelForegroundBrush,
                Background = LabelBackgroundBrush,
                FontSize = QuickLineOrbitLabelPolicy.LabelFontSizePixels,
                Padding = new Thickness(2, 0, 2, 0)
            };
            label.Measure(new Size(double.PositiveInfinity,
                double.PositiveInfinity));
            SetLeft(label, centre.X - label.DesiredSize.Width / 2.0);
            SetTop(label, centre.Y - label.DesiredSize.Height / 2.0);
            Children.Add(label);
        }

        private void AddBottomTexts(bool anyAssumedTrayWidth, double height)
        {
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

            // 3.3.1（特性D）：角落图例——橙X/绿Y/蓝Z/红=桥架(宽)/青=线管。
            string legend = "橙X 绿Y 蓝Z　红=桥架(宽) 青=线管";
            if (anyAssumedTrayWidth)
                legend += "　桥架宽未标注按 200 显示";
            var legendText = new System.Windows.Controls.TextBlock
            {
                Text = legend,
                Foreground = HintBrush,
                FontSize = 11
            };
            SetLeft(legendText, 10);
            SetTop(legendText, Math.Max(2.0, height - 38.0));
            Children.Add(legendText);
        }

        private void AddColumnGridToggle(double width)
        {
            var toggle = new System.Windows.Controls.TextBlock
            {
                Text = _columnGridEnabled ? "柱网 开" : "柱网 关",
                Foreground = HintBrush,
                Background = Brushes.Transparent,
                FontSize = 11,
                Cursor = Cursors.Hand,
                ToolTip = "点击切换地面柱网显示"
            };
            toggle.MouseLeftButtonDown += (sender, args) =>
            {
                ToggleColumnGrid();
                args.Handled = true;
            };
            toggle.Measure(new Size(double.PositiveInfinity,
                double.PositiveInfinity));
            SetLeft(toggle, Math.Max(4.0, width - toggle.DesiredSize.Width - 10.0));
            SetTop(toggle, 8.0);
            Children.Add(toggle);
        }

        private static string TrimAxisLabel(string value)
            => (value ?? string.Empty).Trim();

        private Point ToScreen(QuickLinePoint point, double centreX,
            double centreY, double scale)
        {
            double width = Math.Max(1.0, ActualWidth);
            double height = Math.Max(1.0, ActualHeight);
            return new Point(
                width / 2.0 + (point.X - centreX) * scale,
                height / 2.0 - (point.Y - centreY) * scale);
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

        /// <summary>一段路由的投影缓存（屏幕坐标在换算时再算，避免重复）。</summary>
        private sealed class ProjectedSegment
        {
            public ProjectedSegment(QuickLineOrbitPreviewSegment segment,
                QuickLinePoint start, QuickLinePoint end, Brush brush)
            {
                Segment = segment;
                Start = start;
                End = end;
                Brush = brush;
            }

            public QuickLineOrbitPreviewSegment Segment { get; }
            public QuickLinePoint Start { get; }
            public QuickLinePoint End { get; }
            public Brush Brush { get; }
        }
    }
}
