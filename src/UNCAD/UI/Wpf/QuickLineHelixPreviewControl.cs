using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using UNCAD.Core.QuickLine;

namespace UNCAD.UI
{
    /// <summary>
    /// 3.3.2（D-054 Amendment）：U1U 确认窗左侧的 3D 路由预览——渲染内核换为
    /// HelixToolkit.Wpf（用户看了 3.3.1 Canvas 正交投影后现场反馈「我旋转的
    /// 时候明显感觉这不对 这不像是3D的 像是一个伪3D的」，主会话选型表定案
    /// HelixToolkit.Wpf，WebView2 方案作废）。透视相机 + 内置轨道交互（左键
    /// 拖动=旋转、滚轮=缩放、右键拖动=平移、双击=复位），路由线用
    /// LinesVisual3D（等宽屏空线），米数标/轴位标签用 BillboardTextVisual3D。
    /// 数据模型 <see cref="QuickLineOrbitPreviewModel"/> 与 3.3.1 完全一致
    /// （场景语义仍由 Features 阶段构建后传入——分层铁律：窗体不重建场景；
    /// 本控件只把传入模型映射成 3D 可视元素）。
    ///
    /// 沿用 3.3.1 的四特性：A 对半分布局由宿主窗体 GridSplitter 承担；B 每段
    /// 中点标毫米原文；C 柱位进场景——3.3.2 修正为数字轴×字母轴**两向正交
    /// 柱网**（QuickLineColumnGridPlanner 扩展：数字轴向由端点平面位移决定，
    /// 不再沿场景 X 单向插值——用户反馈「轴位不应该这样显示」的根因，场景 X
    /// 是重建抽象轴与现实柱网方向无固定对应）；D 桥架/线管双线通道（桥架=
    /// 两侧红色平行线+浅红半透明填充带，线管=青色细双线），角落图例保留
    /// （WPF 元素叠加在视口上）。
    ///
    /// 工程细节：HelixViewport3D **惰性创建**——首次 SetModel(非 null) 才建
    /// （U1F/无预览路径与 3D 完全无关）。Helix 主题字典里有跨线程共享的未冻结
    /// Freezable，Billboard/ScreenSpace 视觉还订阅进程级弱渲染事件——窗体
    /// Unloaded 时把场景子元素摘下让逐帧回调失效（多 STA 线程的测试宿主才
    /// 不会被异线程 Transform3D 炸掉；生产 AutoCAD 单 UI 线程本就不受影响）。
    /// </summary>
    public sealed class QuickLineHelixPreviewControl : Grid
    {
        // 静态画笔必须 Freeze：Freezable 属于创建它的线程，STA 测试线程与
        // AutoCAD UI 线程不同会抛跨线程异常；冻结后全线程可用。
        private static readonly Brush HintBrush = FrozenBrush(0x8A, 0x8A, 0x8A);
        private static readonly Brush AxisTickBrush = FrozenBrush(0x70, 0x74, 0x7C);
        private static readonly Brush EndpointPinBrush = FrozenBrush(0x3A, 0x3E, 0x46);
        // 特性B：段标签（浅底保证在线条上可读）。
        private static readonly Brush LabelForegroundBrush = FrozenBrush(0x1E, 0x22, 0x28);
        private static readonly Brush LabelBackgroundBrush = FrozenBrush(0xFF, 0xFF, 0xFF, 0xB4);

        // 轴向配色沿用 3.3.1（X 橙 / Y 绿 / Z 蓝）与桥架红/线管青。
        private static readonly Color AxisXColor = Color.FromRgb(0xE0, 0x6C, 0x00);
        private static readonly Color AxisYColor = Color.FromRgb(0x2E, 0x9E, 0x5B);
        private static readonly Color AxisZColor = Color.FromRgb(0x3B, 0x6E, 0xD0);
        private static readonly Color ColumnGridColor = Color.FromRgb(0xC8, 0xCC, 0xD4);
        private static readonly Color TrayColor = Color.FromRgb(0xE8, 0x44, 0x3C);
        private static readonly Color TrayFillColor = Color.FromArgb(0x33, 0xE8, 0x44, 0x3C);
        private static readonly Color ConduitColor = Color.FromRgb(0x2E, 0xA8, 0xC0);
        private static readonly Color EndpointPinColor = Color.FromRgb(0x3A, 0x3E, 0x46);

        /// <summary>桥架双线的世界空间最小半宽（mm，占场景对角线比例——细桥架拉远后仍可见）。</summary>
        private const double MinimumTrayOffsetDiagonalRatio = 0.0012;
        /// <summary>线管双线的世界空间半宽（mm，占场景对角线比例）。</summary>
        private const double ConduitOffsetDiagonalRatio = 0.0006;
        /// <summary>米数标的世界高度（占场景对角线比例——Billboard 字号口径）。</summary>
        private const double LabelHeightDiagonalRatio = 0.016;
        /// <summary>轴位刻度/字母标签的世界高度（略小于米数标）。</summary>
        private const double TickHeightDiagonalRatio = 0.013;
        /// <summary>端点轴位钉的竖直引线长度（占对角线比例）。</summary>
        private const double EndpointPinLengthDiagonalRatio = 0.030;
        /// <summary>Billboard 文字的源像素字号（世界尺寸经 HeightFactor 换算）。</summary>
        private const double BillboardFontSize = 12.0;

        private HelixViewport3D _viewport;
        private readonly TextBlock _hintText;
        private readonly TextBlock _summaryText;
        private readonly TextBlock _legendText;
        private readonly TextBlock _toggleText;
        private readonly List<Visual3D> _modelChildren =
            new List<Visual3D>();
        private readonly List<Visual3D> _columnGridChildren =
            new List<Visual3D>();
        private QuickLineOrbitPreviewModel _model;
        private bool _columnGridEnabled = true;
        private bool _pendingFit;
        private bool _sceneDetached;

        public QuickLineHelixPreviewControl()
        {
            ClipToBounds = true;

            _hintText = new TextBlock
            {
                Text = "该图框没有可预览的带毫米标注 U1L 路由",
                Foreground = HintBrush,
                FontSize = 12
            };
            _hintText.SetValue(MarginProperty, new Thickness(12));
            _hintText.SetValue(HorizontalAlignmentProperty,
                HorizontalAlignment.Left);
            _hintText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
            Children.Add(_hintText);

            _summaryText = new TextBlock
            {
                Foreground = HintBrush,
                FontSize = 11,
                Text = "左键拖动=旋转 · 滚轮=缩放 · 右键拖动=平移 · 双击=复位"
            };
            _summaryText.SetValue(HorizontalAlignmentProperty,
                HorizontalAlignment.Left);
            _summaryText.SetValue(VerticalAlignmentProperty,
                VerticalAlignment.Bottom);
            _summaryText.SetValue(MarginProperty, new Thickness(10, 0, 0, 2));
            Children.Add(_summaryText);

            _legendText = new TextBlock
            {
                Foreground = HintBrush,
                FontSize = 11,
                Text = "橙X 绿Y 蓝Z　红=桥架(宽) 青=线管"
            };
            _legendText.SetValue(HorizontalAlignmentProperty,
                HorizontalAlignment.Left);
            _legendText.SetValue(VerticalAlignmentProperty,
                VerticalAlignment.Bottom);
            _legendText.SetValue(MarginProperty, new Thickness(10, 0, 0, 18));
            Children.Add(_legendText);

            _toggleText = new TextBlock
            {
                Foreground = HintBrush,
                FontSize = 11,
                Cursor = Cursors.Hand,
                ToolTip = "点击切换地面柱网显示"
            };
            _toggleText.SetValue(HorizontalAlignmentProperty,
                HorizontalAlignment.Right);
            _toggleText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
            _toggleText.SetValue(MarginProperty, new Thickness(0, 8, 10, 0));
            _toggleText.MouseLeftButtonDown += (sender, args) =>
            {
                ToggleColumnGrid();
                args.Handled = true;
            };
            Children.Add(_toggleText);

            // Helix 的 Billboard/ScreenSpace 视觉订阅进程级弱渲染事件（每帧把
            // 文字面片转向相机）。窗体关闭后视觉仍挂在树上时，另一个 STA 线程
            // 的渲染节拍会跨线程调用它（WPF WeakEventManager 在引发线程派发），
            // 写入异线程 Transform3D 直接炸宿主——Unloaded 时把 3D 场景子元素
            // 全部摘下（Billboard 的 OnVisualParentChanged 会置 isRendering=false，
            // 残留订阅变空操作）；Loaded 再挂回并重新适配。
            Unloaded += (sender, args) => DetachSceneChildren();
            Loaded += (sender, args) =>
            {
                ReattachSceneChildren();
                FitIfPending();
            };
            SizeChanged += (sender, args) => FitIfPending();
        }

        /// <summary>当前预览的场景（测试/宿主可读；为 null 时显示提示）。</summary>
        public QuickLineOrbitPreviewModel Model => _model;

        /// <summary>
        /// Helix 视口（宿主/测试可达；交互与相机由其内置）。惰性创建：首次
        /// SetModel(非 null) 之前为 null（U1F/无预览路径不建 3D）。
        /// </summary>
        internal HelixViewport3D Viewport => _viewport;

        /// <summary>当前已构建的路由线段条数（数据级计数，不依赖 GPU 渲染）。</summary>
        internal int BuiltSegmentLineCount { get; private set; }
        /// <summary>特性B：当前已构建的段毫米标标签数。</summary>
        internal int BuiltLabelCount { get; private set; }
        /// <summary>特性C：当前可见的柱网线数（开关关掉=0；显示语义计数）。</summary>
        internal int BuiltColumnGridLineCount =>
            _columnGridEnabled ? _plannedColumnGridLineCount : 0;
        /// <summary>特性C：当前可见的数字轴刻度数（开关关掉=0）。</summary>
        internal int BuiltAxisTickCount =>
            _columnGridEnabled ? _plannedAxisTickCount : 0;
        /// <summary>特性C（3.3.2）：当前可见的字母轴标签数（开关关掉=0）。</summary>
        internal int BuiltLetterLabelCount =>
            _columnGridEnabled ? _plannedLetterLabelCount : 0;
        /// <summary>特性C：当前已构建的端点轴位钉数。</summary>
        internal int BuiltEndpointPinCount { get; private set; }
        /// <summary>特性D：当前已构建的桥架双线通道段数。</summary>
        internal int BuiltTrayChannelCount { get; private set; }
        /// <summary>特性D：当前已构建的线管双线通道段数。</summary>
        internal int BuiltConduitChannelCount { get; private set; }

        /// <summary>特性C：柱网开关状态（默认开）。</summary>
        internal bool ColumnGridEnabled => _columnGridEnabled;

        private int _plannedColumnGridLineCount;
        private int _plannedAxisTickCount;
        private int _plannedLetterLabelCount;

        /// <summary>切换场景（含 null=隐藏为提示文字）。非 null 首次调用时创建视口。</summary>
        public void SetModel(QuickLineOrbitPreviewModel model)
        {
            if (model != null && model.Segments.Count > 0) EnsureViewport();
            _model = model;
            RebuildScene();
        }

        /// <summary>复位视角：回到默认透视相机并适配场景（双击/按钮同路径）。</summary>
        internal void ResetCameraView()
        {
            if (_viewport == null) return;
            _viewport.ResetCamera();
            FitIfPending(force: true);
        }

        /// <summary>特性C：柱网显示开关（角落小按钮同路径）。</summary>
        internal void ToggleColumnGrid()
        {
            _columnGridEnabled = !_columnGridEnabled;
            ApplyColumnGridVisibility();
            UpdateToggleText();
        }

        private static Brush FrozenBrush(byte r, byte g, byte b, byte a = 0xFF)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 惰性创建 Helix 视口（透视相机——真正的 3D，不再是 3.3.1 Canvas 的
        /// 正交伪 3D）。U1F/无预览路径从不进入这里。
        /// </summary>
        private void EnsureViewport()
        {
            if (_viewport != null) return;
            _viewport = new HelixViewport3D
            {
                ShowViewCube = false,
                ShowCameraInfo = false,
                ShowCoordinateSystem = false,
                ShowCameraTarget = false,
                ShowFrameRate = false,
                IsHeadLightEnabled = true,
                Background = Brushes.White
            };
            Children.Insert(0, _viewport);
        }

        // ---- 场景构建 -----------------------------------------------------------

        private void RebuildScene()
        {
            ClearScene();
            if (_model == null || _model.Segments.Count == 0)
            {
                _hintText.Visibility = Visibility.Visible;
                _summaryText.Visibility = Visibility.Collapsed;
                _legendText.Visibility = Visibility.Collapsed;
                _toggleText.Visibility = Visibility.Collapsed;
                return;
            }
            _hintText.Visibility = Visibility.Collapsed;
            _summaryText.Visibility = Visibility.Visible;
            _legendText.Visibility = Visibility.Visible;
            _toggleText.Visibility = Visibility.Visible;
            _summaryText.Text = (_model.AxisSummary.Length > 0
                ? _model.AxisSummary + "　" : "")
                + "左键拖动=旋转 · 滚轮=缩放 · 右键拖动=平移 · 双击=复位";

            Envelope envelope = Envelope.Of(_model.Segments);
            double diagonal = Math.Max(envelope.Diagonal, 1.0);

            // 特性C：两向柱网 + 两向标签（数字轴×字母轴；方向由端点位移决定）。
            QuickLineColumnGrid grid = QuickLineColumnGridPlanner.Plan(
                _model.Segments, _model.UpstreamAxisLabel,
                _model.DownstreamAxisLabel);
            if (grid != null)
            {
                BuildColumnGrid(grid, diagonal);
                BuildEndpointPins(grid, diagonal);
            }

            // 特性D：桥架/线管双线通道（先画，电缆线叠在上面）。
            bool anyAssumedTrayWidth = false;
            foreach (QuickLineOrbitPreviewSegment segment in _model.Segments)
            {
                QuickLineOrbitPreviewChannel channel = segment.Channel;
                if (channel == null) continue;
                if (channel.TrayWidthMillimetres.HasValue)
                {
                    BuildTrayChannel(segment, diagonal);
                    BuiltTrayChannelCount++;
                    if (channel.TrayWidthAssumedDefault) anyAssumedTrayWidth = true;
                }
                else if (channel.HasConduit)
                {
                    BuildConduitChannel(segment, diagonal);
                    BuiltConduitChannelCount++;
                }
            }

            // 电缆线本体（按轴分组，三个等宽屏空线可视元素）+ 特性B 段中点毫米标。
            BuildRouteLines(diagonal);
            UpdateLegend(anyAssumedTrayWidth);
            UpdateToggleText();
            ApplyColumnGridVisibility();

            // 透视相机从场景斜上方看：默认方向与 3.3.1 的西南上方视角等价
            //（方位 -135°/俯仰 30° 的方向向量），场距随场景包络自适应。
            SetDefaultCamera(envelope, diagonal);
            _sceneDetached = false;
            _pendingFit = true;
            FitIfPending();
        }

        private void ClearScene()
        {
            if (_viewport != null)
            {
                foreach (Visual3D child in _modelChildren)
                    _viewport.Children.Remove(child);
                foreach (Visual3D child in _columnGridChildren)
                    _viewport.Children.Remove(child);
            }
            _modelChildren.Clear();
            _columnGridChildren.Clear();
            BuiltSegmentLineCount = 0;
            BuiltLabelCount = 0;
            _plannedColumnGridLineCount = 0;
            _plannedAxisTickCount = 0;
            _plannedLetterLabelCount = 0;
            BuiltEndpointPinCount = 0;
            BuiltTrayChannelCount = 0;
            BuiltConduitChannelCount = 0;
        }

        /// <summary>摘下全部 3D 场景子元素（窗体 Unloaded；渲染回调随之失效）。</summary>
        private void DetachSceneChildren()
        {
            if (_viewport == null) return;
            foreach (Visual3D child in _modelChildren)
                _viewport.Children.Remove(child);
            foreach (Visual3D child in _columnGridChildren)
                _viewport.Children.Remove(child);
            _sceneDetached = true;
        }

        /// <summary>重新挂回（Unloaded 后再 Loaded 的场景，如窗体复用）。</summary>
        private void ReattachSceneChildren()
        {
            if (!_sceneDetached || _viewport == null) return;
            _sceneDetached = false;
            if (_model == null || _model.Segments.Count == 0) return;
            foreach (Visual3D child in _modelChildren)
                _viewport.Children.Add(child);
            ApplyColumnGridVisibility();
            _pendingFit = true;
        }

        private void BuildRouteLines(double diagonal)
        {
            double labelHeight = diagonal * LabelHeightDiagonalRatio;
            var byAxis = new Dictionary<QuickLineSpatialAxis,
                List<Point3D>>();
            foreach (QuickLineOrbitPreviewSegment segment in _model.Segments)
            {
                Point3D start = ToPoint3D(segment.Start);
                Point3D end = ToPoint3D(segment.End);
                if (!byAxis.TryGetValue(segment.Axis, out List<Point3D> points))
                {
                    points = new List<Point3D>();
                    byAxis[segment.Axis] = points;
                }
                points.Add(start);
                points.Add(end);
                BuiltSegmentLineCount++;

                string labelText = segment.LabelText;
                if (labelText.Length == 0) continue;
                var middle = new Point3D(
                    (start.X + end.X) / 2.0,
                    (start.Y + end.Y) / 2.0,
                    (start.Z + end.Z) / 2.0 + labelHeight * 0.6);
                _modelChildren.Add(CreateBillboard(labelText, middle,
                    labelHeight, LabelForegroundBrush, LabelBackgroundBrush,
                    FontWeights.Normal));
                BuiltLabelCount++;
            }
            foreach (KeyValuePair<QuickLineSpatialAxis, List<Point3D>> pair in byAxis)
            {
                var lines = new LinesVisual3D
                {
                    Color = AxisColor(pair.Key),
                    Thickness = 2.0,
                    Points = new Point3DCollection(pair.Value)
                };
                _modelChildren.Add(lines);
            }
        }

        private void BuildColumnGrid(QuickLineColumnGrid grid, double diagonal)
        {
            double tickHeight = diagonal * TickHeightDiagonalRatio;
            var gridPoints = new List<Point3D>(grid.Lines.Count * 2);
            foreach (QuickLineColumnGridLine line in grid.Lines)
            {
                Point3D from = line.ConstantX
                    ? new Point3D(line.FixedValue, line.Minimum, 0.0)
                    : new Point3D(line.Minimum, line.FixedValue, 0.0);
                Point3D to = line.ConstantX
                    ? new Point3D(line.FixedValue, line.Maximum, 0.0)
                    : new Point3D(line.Maximum, line.FixedValue, 0.0);
                gridPoints.Add(from);
                gridPoints.Add(to);
                _plannedColumnGridLineCount++;
            }
            if (_plannedColumnGridLineCount > 0)
            {
                var lines = new LinesVisual3D
                {
                    Color = ColumnGridColor,
                    Thickness = 1.0,
                    Points = new Point3DCollection(gridPoints)
                };
                _columnGridChildren.Add(lines);
            }

            // 数字轴刻度：贴柱网与包络边沿的交点；字母轴标签贴对边（两向分开）。
            foreach (QuickLineColumnGridTick tick in grid.NumericTicks)
            {
                Point3D anchor = grid.NumericAxisIsX
                    ? new Point3D(tick.Position, grid.MinY, 0.0)
                    : new Point3D(grid.MinX, tick.Position, 0.0);
                _columnGridChildren.Add(CreateBillboard(
                    tick.Number.ToString(System.Globalization.CultureInfo
                        .InvariantCulture),
                    Raised(anchor, tickHeight * 0.5), tickHeight,
                    AxisTickBrush, null, FontWeights.Normal));
                _plannedAxisTickCount++;
            }
            foreach (QuickLineColumnGridLetterLabel letter in grid.LetterLabels)
            {
                Point3D anchor = grid.NumericAxisIsX
                    ? new Point3D(grid.MaxX, letter.Position, 0.0)
                    : new Point3D(letter.Position, grid.MaxY, 0.0);
                _columnGridChildren.Add(CreateBillboard(letter.Letter,
                    Raised(anchor, tickHeight * 0.5), tickHeight,
                    AxisTickBrush, null, FontWeights.Normal));
                _plannedLetterLabelCount++;
            }
        }

        private void BuildEndpointPins(QuickLineColumnGrid grid, double diagonal)
        {
            double labelHeight = diagonal * TickHeightDiagonalRatio;
            double pinLength = diagonal * EndpointPinLengthDiagonalRatio;
            BuildEndpointPin(grid.UpstreamEndpoint,
                TrimAxisLabel(_model.UpstreamAxisLabel), pinLength, labelHeight);
            BuildEndpointPin(grid.DownstreamEndpoint,
                TrimAxisLabel(_model.DownstreamAxisLabel), pinLength, labelHeight);
        }

        /// <summary>端点上方竖直引线 + 轴位原文标签（原文为空不画）。</summary>
        private void BuildEndpointPin(QuickLineSpatialPoint endpoint,
            string label, double pinLength, double labelHeight)
        {
            if (label.Length == 0) return;
            Point3D basePoint = ToPoint3D(endpoint);
            var tip = new Point3D(basePoint.X, basePoint.Y,
                basePoint.Z + pinLength);
            var pin = new LinesVisual3D
            {
                Color = EndpointPinColor,
                Thickness = 1.5,
                Points = new Point3DCollection(new[] { basePoint, tip })
            };
            _modelChildren.Add(pin);
            _modelChildren.Add(CreateBillboard(label, Raised(tip,
                labelHeight * 0.4), labelHeight, EndpointPinBrush,
                LabelBackgroundBrush, FontWeights.SemiBold));
            BuiltEndpointPinCount++;
        }

        // ---- 特性D：桥架/线管双线通道 -------------------------------------------

        /// <summary>
        /// 段方向的水平法向（世界空间 mm）：dir × ẑ 归一化。竖直段（dir∥ẑ）
        /// 回退 ŷ——通道在水平面上向两侧偏移，竖直爬升段同样成立。
        /// </summary>
        private static Vector3D HorizontalNormal(Vector3D direction)
        {
            var normal = Vector3D.CrossProduct(direction,
                new Vector3D(0, 0, 1));
            if (normal.Length < 1e-9) return new Vector3D(0, 1, 0);
            normal.Normalize();
            return normal;
        }

        private void BuildTrayChannel(QuickLineOrbitPreviewSegment segment,
            double diagonal)
        {
            double widthMm = segment.Channel.TrayWidthMillimetres.Value;
            // 偏移=桥架宽度/2（世界空间真实宽度），但不低于对角线的千分之一
            // ——细桥架拉远后仍可见（3.3.1 屏幕 6px 下限的 3D 等价口径）。
            double offset = Math.Max(widthMm / 2.0,
                diagonal * MinimumTrayOffsetDiagonalRatio);
            Point3D start = ToPoint3D(segment.Start);
            Point3D end = ToPoint3D(segment.End);
            var direction = end - start;
            if (direction.Length < 1e-6) return;
            Vector3D normal = HorizontalNormal(direction);
            var a1 = start + normal * offset;
            var a2 = end + normal * offset;
            var b1 = start - normal * offset;
            var b2 = end - normal * offset;

            // 浅红半透明填充带（双面材质：从下往上看也有）。
            var mesh = new MeshGeometry3D();
            mesh.Positions.Add(a1);
            mesh.Positions.Add(a2);
            mesh.Positions.Add(b2);
            mesh.Positions.Add(b1);
            mesh.TextureCoordinates.Add(new Point(0, 0));
            mesh.TextureCoordinates.Add(new Point(0, 0));
            mesh.TextureCoordinates.Add(new Point(0, 0));
            mesh.TextureCoordinates.Add(new Point(0, 0));
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(1);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(3);
            var fillBrush = new SolidColorBrush(TrayFillColor);
            fillBrush.Freeze();
            var fillMaterial = new DiffuseMaterial { Brush = fillBrush };
            var fill = new GeometryModel3D
            {
                Geometry = mesh,
                Material = fillMaterial,
                BackMaterial = fillMaterial
            };
            _modelChildren.Add(new ModelVisual3D { Content = fill });

            // 电缆线两侧两条红色平行线（等宽屏空线）。
            var rails = new LinesVisual3D
            {
                Color = TrayColor,
                Thickness = 1.5,
                Points = new Point3DCollection(new[] { a1, a2, b1, b2 })
            };
            _modelChildren.Add(rails);
        }

        private void BuildConduitChannel(QuickLineOrbitPreviewSegment segment,
            double diagonal)
        {
            double offset = diagonal * ConduitOffsetDiagonalRatio;
            Point3D start = ToPoint3D(segment.Start);
            Point3D end = ToPoint3D(segment.End);
            var direction = end - start;
            if (direction.Length < 1e-6) return;
            Vector3D normal = HorizontalNormal(direction);
            var a1 = start + normal * offset;
            var a2 = end + normal * offset;
            var b1 = start - normal * offset;
            var b2 = end - normal * offset;
            var rails = new LinesVisual3D
            {
                Color = ConduitColor,
                Thickness = 1.5,
                Points = new Point3DCollection(new[] { a1, a2, b1, b2 })
            };
            _modelChildren.Add(rails);
        }

        // ---- 相机 ---------------------------------------------------------------

        private void SetDefaultCamera(Envelope envelope, double diagonal)
        {
            var centre = new Point3D(envelope.CentreX, envelope.CentreY,
                envelope.CentreZ);
            // 方向向量等价于方位 -135°/俯仰 30°（与 3.3.1 Canvas 相机同视角习惯）。
            var direction = new Vector3D(-1, -1, 0.57735026919);
            direction.Normalize();
            var camera = new PerspectiveCamera
            {
                Position = centre + direction * (diagonal * 1.6),
                LookDirection = direction * -diagonal * 1.6,
                UpDirection = new Vector3D(0, 0, 1),
                FieldOfView = 60
            };
            // DefaultCamera 必须是独立实例：交互会原位修改当前相机，若与
            // DefaultCamera 同实例，双击复位会变成空操作（复位永远回不去）。
            _viewport.DefaultCamera = camera;
            _viewport.Camera = camera.Clone();
        }

        private void FitIfPending(bool force = false)
        {
            if (_viewport == null) return;
            if (!_pendingFit && !force) return;
            if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return;
            _pendingFit = false;
            try
            {
                _viewport.ZoomExtents();
            }
            catch (Exception)
            {
                // 视口未就绪（无显卡/零尺寸）时保持显式相机位，不炸宿主窗。
            }
        }

        // ---- 杂项 ---------------------------------------------------------------

        private BillboardTextVisual3D CreateBillboard(string text,
            Point3D position, double worldHeight, Brush foreground,
            Brush background, FontWeight weight)
        {
            return new BillboardTextVisual3D
            {
                Text = text,
                Position = position,
                FontSize = BillboardFontSize,
                FontWeight = weight,
                Foreground = foreground,
                Background = background,
                // 世界四边形高度 = 文本像素高度 × HeightFactor；按场景对角线
                // 比例换算世界高度（毫米场景下文本约对角线的 1.6%/1.3%）。
                HeightFactor = worldHeight / BillboardFontSize
            };
        }

        private void ApplyColumnGridVisibility()
        {
            if (_viewport == null) return;
            foreach (Visual3D child in _columnGridChildren)
            {
                bool contains = _viewport.Children.Contains(child);
                if (_columnGridEnabled && !contains) _viewport.Children.Add(child);
                if (!_columnGridEnabled && contains) _viewport.Children.Remove(child);
            }
        }

        private void UpdateToggleText()
        {
            if (_toggleText.Visibility != Visibility.Visible) return;
            _toggleText.Text = _columnGridEnabled ? "柱网 开" : "柱网 关";
        }

        private void UpdateLegend(bool anyAssumedTrayWidth)
        {
            string legend = "橙X 绿Y 蓝Z　红=桥架(宽) 青=线管";
            if (anyAssumedTrayWidth)
                legend += "　桥架宽未标注按 200 显示";
            _legendText.Text = legend;
        }

        private static string TrimAxisLabel(string value)
            => (value ?? string.Empty).Trim();

        private static Point3D ToPoint3D(QuickLineSpatialPoint point)
            => new Point3D(point.X, point.Y, point.Z);

        private static Point3D Raised(Point3D point, double delta)
            => new Point3D(point.X, point.Y, point.Z + delta);

        private static Color AxisColor(QuickLineSpatialAxis axis)
        {
            switch (axis)
            {
                case QuickLineSpatialAxis.X: return AxisXColor;
                case QuickLineSpatialAxis.Y: return AxisYColor;
                default: return AxisZColor;
            }
        }

        /// <summary>场景包络（含对角线长度与中心——相机与字号换算用）。</summary>
        private sealed class Envelope
        {
            private Envelope(double minX, double minY, double minZ,
                double maxX, double maxY, double maxZ)
            {
                MinX = minX;
                MinY = minY;
                MinZ = minZ;
                MaxX = maxX;
                MaxY = maxY;
                MaxZ = maxZ;
            }

            public double MinX { get; }
            public double MinY { get; }
            public double MinZ { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public double MaxZ { get; }

            public double CentreX => (MinX + MaxX) / 2.0;
            public double CentreY => (MinY + MaxY) / 2.0;
            public double CentreZ => (MinZ + MaxZ) / 2.0;
            public double Diagonal => Math.Sqrt(
                (MaxX - MinX) * (MaxX - MinX)
                + (MaxY - MinY) * (MaxY - MinY)
                + (MaxZ - MinZ) * (MaxZ - MinZ));

            public static Envelope Of(
                IReadOnlyList<QuickLineOrbitPreviewSegment> segments)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                double maxZ = double.MinValue;
                foreach (QuickLineOrbitPreviewSegment segment in segments)
                {
                    foreach (QuickLineSpatialPoint point in new[]
                        { segment.Start, segment.End })
                    {
                        minX = Math.Min(minX, point.X);
                        minY = Math.Min(minY, point.Y);
                        minZ = Math.Min(minZ, point.Z);
                        maxX = Math.Max(maxX, point.X);
                        maxY = Math.Max(maxY, point.Y);
                        maxZ = Math.Max(maxZ, point.Z);
                    }
                }
                return new Envelope(minX, minY, minZ, maxX, maxY, maxZ);
            }
        }
    }
}
