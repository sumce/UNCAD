using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Controls;
using System.Linq;

namespace UNCAD.UI
{
    /// <summary>
    /// 3.4.0：WPF 原生 Viewport3D 视口——3D 预览的**哑渲染器**。消费
    /// QuickLine3dScene（构建器算好的纯几何），职责只有四件：合并网格
    /// （按材质合并=少量 DrawCall）、两盏灯（环境+平行光，对应 three.js
    /// hemisphere+sun）、轨道相机（左键旋转/滚轮缩放/右键平移/双击复位，
    /// Z 轴朝上与 CAD 口径一致）、标签叠加层（手工视图投影矩阵，组开关
    /// +屏幕范围+防重叠三重过滤）。没有浏览器进程、没有用户数据目录锁、
    /// 没有 airspace 白窗——「正在启动三维渲染器」这一整类故障不复存在。
    /// </summary>
    internal sealed class QuickLine3dViewportControl : Grid
    {
        private static readonly Color SceneBackground = FromRgb(0x0D, 0x13, 0x1A);

        private static readonly Dictionary<string, Color> LabelPalette =
            new Dictionary<string, Color>(StringComparer.Ordinal)
            {
                // 对齐 Web/QuickLineOrbit styles.css 的标签配色（页面已退役，
                // 视觉口径随控件延续）。
                ["mm"] = FromRgb(0xD8, 0xE2, 0xEC),
                ["axis"] = FromRgb(0x8F, 0xA0, 0xB3),
                ["ref"] = FromRgb(0xE0, 0xA9, 0x4A),
                ["pin"] = FromRgb(0x99, 0xA7, 0xB8),
                ["route"] = FromRgb(0xCF, 0xE0, 0xEF),
                ["directionDown"] = FromRgb(0x63, 0xC9, 0xA5),
                ["directionUp"] = FromRgb(0xE0, 0xB0, 0x64),
                // D-058：U1U 窗格的软管标签。
                ["hose"] = FromRgb(0x5F, 0xC9, 0x8A)
            };
        private static readonly double[] LabelCollisionOffsets =
            { 0, -20, 20, -40, 40 };
        private static readonly double[] FixedLabelOffset = { 0 };

        private readonly Viewport3D _viewport = new Viewport3D();
        private readonly Canvas _labelLayer = new Canvas
        {
            IsHitTestVisible = false,
            ClipToBounds = true
        };
        private readonly PerspectiveCamera _perspective = new PerspectiveCamera
        {
            UpDirection = new Vector3D(0, 0, 1),
            FieldOfView = 60.0
        };
        private readonly OrthographicCamera _orthographic =
            new OrthographicCamera { UpDirection = new Vector3D(0, 0, 1) };

        private readonly Dictionary<QuickLine3dSceneGroupKind, ModelVisual3D>
            _groupVisuals = new Dictionary<QuickLine3dSceneGroupKind,
                ModelVisual3D>();
        private readonly List<LabelEntry> _labels = new List<LabelEntry>();
        private readonly List<LabelEntry> _activeLabels = new List<LabelEntry>();
        private readonly List<Rect> _occupied = new List<Rect>();
        private readonly HashSet<string> _hiddenLabelKinds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<QuickLine3dSceneGroupKind> _hiddenGroups =
            new HashSet<QuickLine3dSceneGroupKind>();
        private ILookup<QuickLine3dSceneGroupKind, QuickLine3dPrimitive> _groupPrimitives;
        private bool _cameraUpdatePending;

        private QuickLine3dScene _scene;
        private Matrix3D _worldToScreen;
        private double _azimuth;
        private double _elevation;
        private double _distance;
        private double _orthoHeight;
        private double _centerx;
        private double _centery;
        private double _centerz;
        private double _defaultDistance;
        private double _defaultOrthoHeight;
        private bool _orthographicMode;
        private bool _rotating;
        private bool _panning;
        private Point _lastMouse;

        public QuickLine3dViewportControl()
        {
            Background = new SolidColorBrush(SceneBackground);
            ClipToBounds = true;
            _viewport.ClipToBounds = true;
            _viewport.IsHitTestVisible = false;
            Children.Add(_viewport);
            Children.Add(_labelLayer);

            MouseLeftButtonDown += OnRotateDown;
            MouseRightButtonDown += OnPanDown;
            MouseDown += (sender, args) =>
            {
                if (args.ChangedButton == MouseButton.Middle) OnPanDown(sender, args);
            };
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseWheel += OnMouseWheel;
            LostMouseCapture += OnLostCapture;
            SizeChanged += (sender, args) => UpdateCamera();
            Unloaded += (sender, args) =>
            {
                CompositionTarget.Rendering -= OnCameraFrame;
                _cameraUpdatePending = false;
            };
        }

        private static Color FromRgb(byte r, byte g, byte b)
        {
            return Color.FromRgb(r, g, b);
        }

        // ---- 无头/验收观测口径 -------------------------------------------------

        /// <summary>当前场景标签数（SetScene 后同步可得）。</summary>
        internal int LabelCount => _labels.Count;

        /// <summary>当前已实例化的文本控件数（性能回归观测）。</summary>
        internal int MaterializedLabelCount
        {
            get
            {
                int count = 0;
                foreach (LabelEntry entry in _labels)
                    if (entry.Element != null) count++;
                return count;
            }
        }

        internal bool UsesNonHitTestedViewport => !_viewport.IsHitTestVisible;

        /// <summary>本场景合并后的网格数（材质合并结果，测试观测）。</summary>
        internal int MeshCount { get; private set; }

        /// <summary>本场景三角形总数（测试观测）。</summary>
        internal int TriangleCount { get; private set; }

        // ---- 场景装载 -----------------------------------------------------------

        /// <summary>装载场景（重建灯光/网格/相机/标签；同步完成，无任何异步）。</summary>
        public void SetScene(QuickLine3dScene scene,
            IEnumerable<string> initiallyHiddenLabelKinds = null,
            IEnumerable<QuickLine3dSceneGroupKind> initiallyHiddenGroups = null)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _viewport.Children.Clear();
            _labelLayer.Children.Clear();
            _groupVisuals.Clear();
            _labels.Clear();
            _activeLabels.Clear();
            _hiddenLabelKinds.Clear();
            _hiddenGroups.Clear();
            foreach (var kind in initiallyHiddenGroups
                ?? new[] { QuickLine3dSceneGroupKind.Detail })
                _hiddenGroups.Add(kind);
            _groupPrimitives = _scene.Primitives.ToLookup(primitive => primitive.Group);
            if (initiallyHiddenLabelKinds != null)
                foreach (string kind in initiallyHiddenLabelKinds)
                    if (!string.IsNullOrEmpty(kind))
                        _hiddenLabelKinds.Add(kind);
            MeshCount = 0;
            TriangleCount = 0;

            // 两盏灯：环境 0.95（路由近原色）+ 平行光 0.55（参照模型出立体感）。
            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(242, 242, 242)));
            Vector3D sunDirection = new Vector3D(0.55, 0.75, -1.0);
            sunDirection.Normalize();
            lights.Children.Add(new DirectionalLight(Color.FromRgb(140, 140, 140),
                sunDirection));
            lights.Freeze();
            _viewport.Children.Add(new ModelVisual3D { Content = lights });

            foreach (var group in _groupPrimitives)
                if (!_hiddenGroups.Contains(group.Key)) BuildGroupMeshes(group.Key);

            // 相机初值：取景中心 + 方位方向（构建器给的世界系方向）。
            _centerx = _scene.Centerx;
            _centery = _scene.Centery;
            _centerz = _scene.Centerz;
            Vector3D orbit = new Vector3D(_scene.Orbitx, _scene.Orbitsy,
                _scene.Orbitz);
            if (orbit.LengthSquared < 1e-12) orbit = new Vector3D(-1, -1, 0.577);
            orbit.Normalize();
            _azimuth = Math.Atan2(orbit.Y, orbit.X);
            _elevation = Math.Asin(Clamp(orbit.Z, -1.0, 1.0));
            _orthographicMode = _scene.Orthographic;
            _distance = Math.Max(_scene.CameraDistance, 1.0);
            _orthoHeight = Math.Max(_scene.OrthoViewHeight, 1.0);
            _defaultDistance = _distance;
            _defaultOrthoHeight = _orthoHeight;

            BuildLabelElements();

            double diagonal = Math.Max(_scene.Diagonal, 1.0);
            _perspective.NearPlaneDistance = _scene.Near;
            _perspective.FarPlaneDistance = _scene.Far;
            _orthographic.NearPlaneDistance = _scene.Near;
            _orthographic.FarPlaneDistance = _scene.Far;
            // 场景对角线滚轮缩放边界（防止缩到穿模/缩到无限远）。
            MinZoomDistance = diagonal * 0.01;
            MaxZoomDistance = Math.Max(diagonal * 50.0, _defaultDistance * 10.0);

            if (_viewport.Camera == null
                || (_orthographicMode && !ReferenceEquals(_viewport.Camera,
                        _orthographic))
                || (!_orthographicMode && !ReferenceEquals(_viewport.Camera,
                        _perspective)))
            {
                _viewport.Camera = _orthographicMode
                    ? (ProjectionCamera)_orthographic
                    : _perspective;
            }
            UpdateCamera();
        }

        private double MinZoomDistance { get; set; } = 1.0;
        private double MaxZoomDistance { get; set; } = 1e9;

        /// <summary>分组显隐（组标签随组显隐——updateLabels 每次重判）。</summary>
        public void SetGroupVisible(QuickLine3dSceneGroupKind kind, bool visible)
        {
            bool changed = visible ? _hiddenGroups.Remove(kind) : _hiddenGroups.Add(kind);
            if (!changed) return;
            if (visible && !_groupVisuals.ContainsKey(kind) && HasGroup(kind))
                BuildGroupMeshes(kind);
            // Visual3D 没有 Visibility——显隐用挂上/摘下实现（标签随
            // 对应组一起激活或休眠）。
            if (_groupVisuals.TryGetValue(kind, out ModelVisual3D visual))
            {
                bool attached = _viewport.Children.Contains(visual);
                if (visible && !attached) _viewport.Children.Add(visual);
                else if (!visible && attached) _viewport.Children.Remove(visual);
            }
            RefreshActiveLabels();
            UpdateLabels();
        }

        public bool IsGroupVisible(QuickLine3dSceneGroupKind kind)
        {
            return !_hiddenGroups.Contains(kind);
        }

        /// <summary>场景里是否存在该组（决定开关按钮要不要显示）。</summary>
        internal bool HasGroup(QuickLine3dSceneGroupKind kind)
        {
            return _groupPrimitives?.Contains(kind) == true;
        }

        /// <summary>
        /// 按标签种类显隐（组开关之上的第二层——U13D 的「路线标签/端点标签」
        /// 独立按钮用；kind 传 null 清空全部过滤）。
        /// </summary>
        public void SetLabelKindVisible(string kind, bool visible)
        {
            if (kind == null) _hiddenLabelKinds.Clear();
            else if (visible) _hiddenLabelKinds.Remove(kind);
            else _hiddenLabelKinds.Add(kind);
            RefreshActiveLabels();
            UpdateLabels();
        }

        /// <summary>复位到场景默认视角。</summary>
        public void ResetCamera()
        {
            if (_scene == null) return;
            _distance = _defaultDistance;
            _orthoHeight = _defaultOrthoHeight;
            _centerx = _scene.Centerx;
            _centery = _scene.Centery;
            _centerz = _scene.Centerz;
            Vector3D orbit = new Vector3D(_scene.Orbitx, _scene.Orbitsy, _scene.Orbitz);
            if (orbit.LengthSquared < 1e-12) orbit = new Vector3D(-1, -1, 0.577);
            orbit.Normalize();
            _azimuth = Math.Atan2(orbit.Y, orbit.X);
            _elevation = Math.Asin(Clamp(orbit.Z, -1, 1));
            UpdateCamera();
        }

        // ---- 网格构建（按组×材质合并） -------------------------------------------

        private void BuildGroupMeshes(QuickLine3dSceneGroupKind kind)
        {
            // 每组分两批：不透明先画、半透明后画（WPF 半透明排序是粗粒度的，
            // 树序即绘制序——半透明建筑板压在路由上会糊，先实后透能把错序
            // 减到最轻）。
            var builders = new Dictionary<KeyValuePair<QuickLine3dSceneGroupKind,
                MaterialKey>, MeshBuilder>();
            foreach (QuickLine3dPrimitive primitive in _groupPrimitives[kind])
            {
                var key = new KeyValuePair<QuickLine3dSceneGroupKind, MaterialKey>(
                    primitive.Group,
                    new MaterialKey(primitive.Tint, primitive.Opacity));
                if (!builders.TryGetValue(key, out MeshBuilder builder))
                {
                    builder = new MeshBuilder();
                    builders.Add(key, builder);
                }
                builder.Append(primitive);
                TriangleCount += builder.LastAppendTriangles;
            }

            foreach (KeyValuePair<QuickLine3dSceneGroupKind, List<MeshBuilder
                .Built>> pair in CollectBuilt(builders))
            {
                QuickLine3dSceneGroupKind groupKind = pair.Key;
                var groupModel = new Model3DGroup();
                foreach (MeshBuilder.Built built in pair.Value)
                {
                    if (built.Mesh == null
                        || built.Mesh.TriangleIndices.Count == 0) continue;
                    var material = new DiffuseMaterial
                    {
                        Brush = MakeBrush(built.Key.Tint, built.Key.Opacity)
                    };
                    material.Freeze();
                    var geometryModel = new GeometryModel3D
                    {
                        Geometry = built.Mesh,
                        Material = material
                    };
                    // 半透明板双面（从下往上看也见——桥架填充带/华夫板口径）。
                    if (built.Key.Opacity < 1.0) geometryModel.BackMaterial = material;
                    geometryModel.Freeze();
                    groupModel.Children.Add(geometryModel);
                    MeshCount++;
                }
                groupModel.Freeze();
                var visual = new ModelVisual3D { Content = groupModel };
                _groupVisuals[pair.Key] = visual;
                if (!_hiddenGroups.Contains(groupKind))
                    _viewport.Children.Add(visual);
            }
        }

        /// <summary>按组归并已构建网格：每组不透明在前、半透明在后（稳定序）。</summary>
        private static List<KeyValuePair<QuickLine3dSceneGroupKind,
            List<MeshBuilder.Built>>> CollectBuilt(
            Dictionary<KeyValuePair<QuickLine3dSceneGroupKind, MaterialKey>,
                MeshBuilder> builders)
        {
            var byGroup = new Dictionary<QuickLine3dSceneGroupKind,
                List<MeshBuilder.Built>>();
            var opaque = new List<MeshBuilder.Built>();
            var transparent = new List<MeshBuilder.Built>();
            foreach (KeyValuePair<KeyValuePair<QuickLine3dSceneGroupKind,
                MaterialKey>, MeshBuilder> entry in builders)
            {
                QuickLine3dSceneGroupKind group = entry.Key.Key;
                MeshBuilder.Built built = entry.Value.ToBuilt(entry.Key.Value,
                    group);
                if (built == null || built.Mesh == null
                    || built.Mesh.TriangleIndices.Count == 0) continue;
                if (built.Key.Opacity < 1.0) transparent.Add(built);
                else opaque.Add(built);
            }
            foreach (MeshBuilder.Built built in opaque)
                AddBuilt(byGroup, built);
            foreach (MeshBuilder.Built built in transparent)
                AddBuilt(byGroup, built);
            return new List<KeyValuePair<QuickLine3dSceneGroupKind,
                List<MeshBuilder.Built>>>(byGroup);
        }

        private static void AddBuilt(Dictionary<QuickLine3dSceneGroupKind,
            List<MeshBuilder.Built>> byGroup, MeshBuilder.Built built)
        {
            if (!byGroup.TryGetValue(built.Group, out List<MeshBuilder.Built> list))
            {
                list = new List<MeshBuilder.Built>();
                byGroup.Add(built.Group, list);
            }
            list.Add(built);
        }

        private static Brush MakeBrush(Color tint, double opacity)
        {
            var brush = opacity < 1.0
                ? new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Round(tint.A * Clamp(opacity, 0.05, 1.0)),
                    tint.R, tint.G, tint.B))
                : new SolidColorBrush(tint);
            brush.Freeze();
            return brush;
        }

        // ---- 相机 / 交互 ---------------------------------------------------------

        private void OnRotateDown(object sender, MouseButtonEventArgs args)
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0)
            {
                OnPanDown(sender, args);
                return;
            }
            if (args.ClickCount == 2)
            {
                ResetCamera();
                return;
            }
            Focus();
            _rotating = true;
            _panning = false;
            _lastMouse = args.GetPosition(this);
            CaptureMouse();
            args.Handled = true;
        }

        private void OnPanDown(object sender, MouseButtonEventArgs args)
        {
            _panning = true;
            _rotating = false;
            _lastMouse = args.GetPosition(this);
            CaptureMouse();
            args.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs args)
        {
            if (!_rotating && !_panning) return;
            Point position = args.GetPosition(this);
            double deltax = position.X - _lastMouse.X;
            double deltay = position.Y - _lastMouse.Y;
            _lastMouse = position;
            if (_rotating)
            {
                OrbitBy(deltax, deltay);
            }
            else
            {
                double worldPerPixel = _orthographicMode
                    ? OrthoWidth() / Math.Max(ActualWidth, 1.0)
                    : 2.0 * _distance * Math.Tan(
                        _perspective.FieldOfView * Math.PI / 360.0)
                        / Math.Max(ActualWidth, 1.0);
                Vector3D right, up;
                CameraBasis(out right, out up);
                double shiftx = -deltax * worldPerPixel;
                double shifty = deltay * worldPerPixel;
                _centerx += right.X * shiftx + up.X * shifty;
                _centery += right.Y * shiftx + up.Y * shifty;
                _centerz += right.Z * shiftx + up.Z * shifty;
            }
            RequestCameraUpdate();
            args.Handled = true;
        }

        internal void OrbitBy(double deltaX, double deltaY)
        {
            double scale = 2 * Math.PI / Math.Max(ActualHeight, 1);
            _azimuth -= deltaX * scale;
            _elevation = Clamp(_elevation + deltaY * scale, -1.48, 1.48);
            RequestCameraUpdate();
        }

        private void RequestCameraUpdate()
        {
            if (_cameraUpdatePending) return;
            _cameraUpdatePending = true;
            CompositionTarget.Rendering += OnCameraFrame;
        }

        private void OnCameraFrame(object sender, EventArgs args)
        {
            CompositionTarget.Rendering -= OnCameraFrame;
            _cameraUpdatePending = false;
            UpdateCamera();
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs args)
        {
            if (_rotating || _panning)
            {
                _rotating = false;
                _panning = false;
                ReleaseMouseCapture();
                args.Handled = true;
            }
        }

        private void OnLostCapture(object sender, MouseEventArgs args)
        {
            _rotating = false;
            _panning = false;
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs args)
        {
            ZoomAt(args.GetPosition(this), args.Delta);
            args.Handled = true;
        }

        internal void ZoomAt(Point pointer, double delta)
        {
            double factor = Math.Pow(1.15, -delta / 120.0);
            double oldHeight = _orthographicMode ? _orthoHeight
                : 2 * _distance * Math.Tan(Math.PI / 6);
            if (_orthographicMode)
                _orthoHeight = Clamp(_orthoHeight * factor,
                    MinZoomDistance * 0.5, MaxZoomDistance * 4.0);
            else
                _distance = Clamp(_distance * factor,
                    MinZoomDistance, MaxZoomDistance);
            double newHeight = _orthographicMode ? _orthoHeight
                : 2 * _distance * Math.Tan(Math.PI / 6);
            double shift = (oldHeight - newHeight) / Math.Max(ActualHeight, 1);
            CameraBasis(out Vector3D right, out Vector3D up);
            Vector3D offset = right * ((pointer.X - ActualWidth / 2) * shift)
                + up * ((ActualHeight / 2 - pointer.Y) * shift);
            _centerx += offset.X;
            _centery += offset.Y;
            _centerz += offset.Z;
            UpdateCamera();
        }

        private double OrthoWidth()
        {
            double aspect = ActualHeight < 1.0 ? 1.0 : ActualWidth / ActualHeight;
            return _orthoHeight * aspect;
        }

        private void CameraBasis(out Vector3D right, out Vector3D up)
        {
            Vector3D forward = new Vector3D(
                _centerx - CameraPosition().X,
                _centery - CameraPosition().Y,
                _centerz - CameraPosition().Z);
            forward.Normalize();
            Vector3D worldUp = new Vector3D(0, 0, 1);
            right = Vector3D.CrossProduct(forward, worldUp);
            if (right.LengthSquared < 1e-12) right = new Vector3D(1, 0, 0);
            right.Normalize();
            up = Vector3D.CrossProduct(right, forward);
        }

        private Point3D CameraPosition()
        {
            double cosine = Math.Cos(_elevation);
            return new Point3D(
                _centerx + _distance * cosine * Math.Cos(_azimuth),
                _centery + _distance * cosine * Math.Sin(_azimuth),
                _centerz + _distance * Math.Sin(_elevation));
        }

        /// <summary>按相机状态刷新 Viewport3D 相机、投影矩阵与标签位置。</summary>
        private void UpdateCamera()
        {
            if (_scene == null) return;
            double aspect = ActualHeight < 1.0
                ? 1.0
                : ActualWidth / ActualHeight;
            Point3D center = new Point3D(_centerx, _centery, _centerz);
            Point3D eye;
            double near = _scene.Near;
            double far = _scene.Far;
            if (_orthographicMode)
            {
                // 正交：位置只决定方向感（距离取对角量级即可）。
                double stand = Math.Max(_scene.Diagonal * 4.0, 10000.0);
                double cosine = Math.Cos(_elevation);
                eye = new Point3D(
                    _centerx + stand * cosine * Math.Cos(_azimuth),
                    _centery + stand * cosine * Math.Sin(_azimuth),
                    _centerz + stand * Math.Sin(_elevation));
                near = Math.Max(near, 0.1);
                far = Math.Max(far, stand * 4.0);
                _orthographic.Position = eye;
                _orthographic.LookDirection = new Vector3D(
                    center.X - eye.X, center.Y - eye.Y, center.Z - eye.Z);
                _orthographic.Width = _orthoHeight * aspect;
            }
            else
            {
                eye = CameraPosition();
                // three.js 60° 是**垂直**视场；WPF FieldOfView 是**水平**视场
                // ——按纵横比换算，保证同样的取景感。
                double verticalTan = Math.Tan(30.0 * Math.PI / 180.0);
                double horizontalFov = 2.0 * Math.Atan(verticalTan
                    * Math.Max(aspect, 0.1)) * 180.0 / Math.PI;
                _perspective.FieldOfView = Clamp(horizontalFov, 10.0, 170.0);
                _perspective.Position = eye;
                _perspective.LookDirection = new Vector3D(
                    center.X - eye.X, center.Y - eye.Y, center.Z - eye.Z);
            }
            _worldToScreen = Matrix3D.Multiply(
                CreateViewMatrix(eye, center),
                CreateProjectionMatrix(aspect, near, far));
            UpdateLabels();
        }

        // ---- 标签叠加层 -----------------------------------------------------------

        private void BuildLabelElements()
        {
            foreach (QuickLine3dSceneLabel label in _scene.Labels)
                _labels.Add(new LabelEntry(label));
            RefreshActiveLabels();
        }

        private void RefreshActiveLabels()
        {
            _activeLabels.Clear();
            foreach (LabelEntry entry in _labels)
            {
                bool groupVisible = !_hiddenGroups.Contains(entry.Scene.Group);
                bool visible = groupVisible
                    && !_hiddenLabelKinds.Contains(entry.Scene.Kind);
                if (visible)
                {
                    if (entry.Element == null)
                    {
                        var text = new TextBlock
                        {
                            Text = entry.Scene.Text,
                            FontSize = string.Equals(entry.Scene.Kind, "axis",
                                StringComparison.Ordinal) ? 11 : 12,
                            Foreground = new SolidColorBrush(
                                LabelColor(entry.Scene.Kind)),
                            IsHitTestVisible = false
                        };
                        text.Measure(new Size(double.PositiveInfinity,
                            double.PositiveInfinity));
                        Canvas.SetLeft(text, 0.0);
                        Canvas.SetTop(text, 0.0);
                        text.RenderTransform = new TranslateTransform(0, 0);
                        entry.Element = text;
                        _labelLayer.Children.Add(text);
                    }
                    _activeLabels.Add(entry);
                }
                else if (entry.Element != null)
                {
                    entry.Element.Visibility = Visibility.Hidden;
                }
            }
            _activeLabels.Sort(CompareLabelPriority);
        }

        private static Color LabelColor(string kind)
        {
            Color color;
            return LabelPalette.TryGetValue(kind ?? "", out color)
                ? color
                : FromRgb(0xD8, 0xE2, 0xEC);
        }

        private static bool DeOverlaps(string kind)
        {
            // 柱网/罗盘/路线/方向标签走防重叠；毫米标/参照标/端点钉直显
            //（对齐两版网页的行为分工）。
            switch (kind ?? "")
            {
                case "axis":
                case "compass":
                case "route":
                case "directionDown":
                case "directionUp":
                    return true;
                default:
                    return false;
            }
        }

        private static int SortPriority(LabelEntry entry)
        {
            switch (entry.Scene.Kind ?? "")
            {
                case "directionDown":
                case "directionUp":
                    return 0;
                case "axis":
                case "compass":
                    return 1;
                case "route":
                    return 2;
                default:
                    return 3;
            }
        }

        private void UpdateLabels()
        {
            if (_scene == null || ActualWidth < 1.0 || ActualHeight < 1.0) return;
            double width = ActualWidth;
            double height = ActualHeight;
            _occupied.Clear();

            // Priority order is cached until group or label visibility changes.
            foreach (LabelEntry entry in _activeLabels)
            {
                Point4D clip = _worldToScreen.Transform(new Point4D(
                    entry.Scene.X, entry.Scene.Y, entry.Scene.Z, 1.0));
                bool shown = clip.W > 1e-9;
                double ndcx = 0, ndcy = 0;
                if (shown)
                {
                    ndcx = clip.X / clip.W;
                    ndcy = clip.Y / clip.W;
                    shown = ndcx > -1.05 && ndcx < 1.05
                        && ndcy > -1.05 && ndcy < 1.05
                        && clip.Z / clip.W > -1.0 && clip.Z / clip.W < 1.0;
                }
                if (shown)
                {
                    double x = (ndcx * 0.5 + 0.5) * width;
                    double y = (0.5 - ndcy * 0.5) * height;
                    double verticalOffset = 0.0;
                    if (DeOverlaps(entry.Scene.Kind))
                        shown = PlaceWithoutOverlap(entry, x, y,
                            out verticalOffset);
                    if (shown)
                    {
                        var transform = (TranslateTransform)entry.Element
                            .RenderTransform;
                        transform.X = x - entry.Element.DesiredSize.Width / 2.0;
                        transform.Y = y + verticalOffset
                            - entry.Element.DesiredSize.Height / 2.0;
                    }
                }
                entry.Element.Visibility = shown
                    ? Visibility.Visible
                    : Visibility.Hidden;
            }
        }

        private static int CompareLabelPriority(LabelEntry left, LabelEntry right)
        {
            return SortPriority(left).CompareTo(SortPriority(right));
        }

        /// <summary>防重叠落位（同网页：五个竖向偏移候选，全撞则隐藏）。</summary>
        private bool PlaceWithoutOverlap(LabelEntry entry, double x, double y,
            out double verticalOffset)
        {
            verticalOffset = 0.0;
            double labelWidth = entry.Element.DesiredSize.Width;
            double labelHeight = entry.Element.DesiredSize.Height;
            foreach (double offset in entry.Scene.Kind == "axis"
                ? FixedLabelOffset : LabelCollisionOffsets)
            {
                var candidate = new Rect(
                    x - labelWidth / 2.0 - 3,
                    y + offset - labelHeight / 2.0 - 3,
                    labelWidth + 6, labelHeight + 6);
                bool overlaps = false;
                foreach (Rect other in _occupied)
                {
                    if (candidate.Left < other.Right && candidate.Right > other.Left
                        && candidate.Top < other.Bottom
                        && candidate.Bottom > other.Top)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (overlaps) continue;
                _occupied.Add(candidate);
                verticalOffset = offset;
                return true;
            }
            return false;
        }

        // ---- 投影矩阵（与 WPF 相机同口径，专供标签落位） -------------------------

        /// <summary>视图矩阵（行向量约定：p' = p·M；第三列取 -forward）。</summary>
        internal static Matrix3D CreateViewMatrix(Point3D eye, Point3D target)
        {
            Vector3D forward = new Vector3D(
                target.X - eye.X, target.Y - eye.Y, target.Z - eye.Z);
            forward.Normalize();
            Vector3D worldUp = new Vector3D(0, 0, 1);
            Vector3D right = Vector3D.CrossProduct(forward, worldUp);
            if (right.LengthSquared < 1e-12) right = new Vector3D(1, 0, 0);
            right.Normalize();
            Vector3D up = Vector3D.CrossProduct(right, forward);
            return new Matrix3D(
                right.X, up.X, -forward.X, 0,
                right.Y, up.Y, -forward.Y, 0,
                right.Z, up.Z, -forward.Z, 0,
                0, 0, 0, 1)
            {
                OffsetX = -(right.X * eye.X + right.Y * eye.Y + right.Z * eye.Z),
                OffsetY = -(up.X * eye.X + up.Y * eye.Y + up.Z * eye.Z),
                OffsetZ = forward.X * eye.X + forward.Y * eye.Y + forward.Z * eye.Z
            };
        }

        /// <summary>投影矩阵：正交=宽高两轴归一；透视=WPF 水平视场口径。</summary>
        private Matrix3D CreateProjectionMatrix(double aspect, double near,
            double far)
        {
            if (_orthographicMode)
            {
                double width = Math.Max(_orthoHeight * aspect, 1e-6);
                double depth = far - near;
                if (Math.Abs(depth) < 1e-9) depth = 1e-9;
                return new Matrix3D(
                    2.0 / width, 0, 0, 0,
                    0, 2.0 / Math.Max(_orthoHeight, 1e-6), 0, 0,
                    0, 0, -2.0 / depth, 0,
                    0, 0, -(far + near) / depth, 1);
            }
            double fovRadians = _perspective.FieldOfView * Math.PI / 180.0;
            double xScale = 1.0 / Math.Tan(Math.Max(fovRadians, 0.01) / 2.0);
            double yScale = xScale * Math.Max(aspect, 0.1);
            double depthScale = far - near;
            if (Math.Abs(depthScale) < 1e-9) depthScale = 1e-9;
            return new Matrix3D(
                xScale, 0, 0, 0,
                0, yScale, 0, 0,
                0, 0, far / (near - far), -1,
                0, 0, far * near / (near - far), 0);
        }

        private static double Clamp(double value, double low, double high)
        {
            return value < low ? low : (value > high ? high : value);
        }

        /// <summary>材质合并键（颜色+透明度）。</summary>
        internal readonly struct MaterialKey : IEquatable<MaterialKey>
        {
            internal MaterialKey(Color tint, double opacity)
            {
                Tint = tint;
                Opacity = Math.Round(opacity, 2);
            }

            internal Color Tint { get; }
            internal double Opacity { get; }

            public bool Equals(MaterialKey other)
            {
                return Tint.Equals(other.Tint)
                    && Opacity.Equals(other.Opacity);
            }

            public override bool Equals(object obj)
            {
                return obj is MaterialKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Tint.GetHashCode() * 397)
                        ^ Opacity.GetHashCode();
                }
            }
        }

        /// <summary>几何累积器：同类图元并进一张 MeshGeometry3D。</summary>
        internal sealed class MeshBuilder
        {
            private static readonly Vector3D[] WaffleDirections =
                CreateWaffleDirections();
            private readonly Point3DCollection _positions = new Point3DCollection();
            private readonly Vector3DCollection _normals = new Vector3DCollection();
            private readonly Int32Collection _indices = new Int32Collection();

            internal int LastAppendTriangles { get; private set; }

            internal void Append(QuickLine3dPrimitive primitive)
            {
                int before = _indices.Count;
                switch (primitive.Kind)
                {
                    case QuickLine3dPrimitiveKind.Tube:
                        AddTube(primitive);
                        break;
                    case QuickLine3dPrimitiveKind.Box:
                        AddBox(primitive);
                        break;
                    case QuickLine3dPrimitiveKind.Quad:
                        AddQuad(primitive);
                        break;
                    case QuickLine3dPrimitiveKind.WaffleCell:
                        AddWaffleCell(primitive);
                        break;
                    default:
                        AddSphere(primitive);
                        break;
                }
                LastAppendTriangles = (_indices.Count - before) / 3;
            }

            internal Built ToBuilt(MaterialKey key,
                QuickLine3dSceneGroupKind group)
            {
                if (_indices.Count == 0) return null;
                var mesh = new MeshGeometry3D
                {
                    Positions = _positions,
                    Normals = _normals,
                    TriangleIndices = _indices
                };
                mesh.Freeze();
                return new Built(key, group, mesh);
            }

            /// <summary>按材质合并完成的网格（组归属随构建器传入）。</summary>
            internal sealed class Built
            {
                internal Built(MaterialKey key, QuickLine3dSceneGroupKind group,
                    MeshGeometry3D mesh)
                {
                    Key = key;
                    Group = group;
                    Mesh = mesh;
                }

                internal MaterialKey Key { get; }
                internal QuickLine3dSceneGroupKind Group { get; }
                internal MeshGeometry3D Mesh { get; }
            }

            private int AddVertex(Point3D position, Vector3D normal)
            {
                _positions.Add(position);
                _normals.Add(normal);
                return _positions.Count - 1;
            }

            private void AddTriangle(int a, int b, int c)
            {
                _indices.Add(a);
                _indices.Add(b);
                _indices.Add(c);
            }

            private void AddTube(QuickLine3dPrimitive p)
            {
                var a = new Point3D(p.A1x, p.A1y, p.A1z);
                var b = new Point3D(p.B1x, p.B1y, p.B1z);
                Vector3D direction = new Vector3D(
                    b.X - a.X, b.Y - a.Y, b.Z - a.Z);
                double length = direction.Length;
                if (length < 1e-9 || p.Radius <= 0) return;
                direction /= length;
                Vector3D helper = Math.Abs(direction.Z) > 0.9
                    ? new Vector3D(1, 0, 0)
                    : new Vector3D(0, 0, 1);
                Vector3D side1 = Vector3D.CrossProduct(direction, helper);
                side1.Normalize();
                Vector3D side2 = Vector3D.CrossProduct(direction, side1);
                const int sides = 6;
                int[] ringA = new int[sides];
                int[] ringB = new int[sides];
                for (int index = 0; index < sides; index++)
                {
                    double angle = index * Math.PI * 2.0 / sides;
                    Vector3D radial = side1 * Math.Cos(angle)
                        + side2 * Math.Sin(angle);
                    ringA[index] = AddVertex(
                        a + radial * p.Radius, radial);
                }
                for (int index = 0; index < sides; index++)
                {
                    double angle = index * Math.PI * 2.0 / sides;
                    Vector3D radial = side1 * Math.Cos(angle)
                        + side2 * Math.Sin(angle);
                    ringB[index] = AddVertex(
                        b + radial * p.Radius, radial);
                }
                for (int index = 0; index < sides; index++)
                {
                    int next = (index + 1) % sides;
                    AddTriangle(ringA[index], ringB[index], ringB[next]);
                    AddTriangle(ringA[index], ringB[next], ringA[next]);
                }
            }

            private void AddBox(QuickLine3dPrimitive p)
            {
                if (p.Sizex <= 0 || p.Sizey <= 0 || p.Sizez <= 0) return;
                double minx = p.A1x - p.Sizex / 2, maxx = p.A1x + p.Sizex / 2;
                double miny = p.A1y - p.Sizey / 2, maxy = p.A1y + p.Sizey / 2;
                double minz = p.A1z - p.Sizez / 2, maxz = p.A1z + p.Sizez / 2;
                AddFace(
                    new Point3D(minx, miny, minz), new Point3D(maxx, miny, minz),
                    new Point3D(maxx, maxy, minz), new Point3D(minx, maxy, minz),
                    new Vector3D(0, 0, -1));
                AddFace(
                    new Point3D(minx, miny, maxz), new Point3D(minx, maxy, maxz),
                    new Point3D(maxx, maxy, maxz), new Point3D(maxx, miny, maxz),
                    new Vector3D(0, 0, 1));
                AddFace(
                    new Point3D(minx, miny, minz), new Point3D(minx, maxy, minz),
                    new Point3D(minx, maxy, maxz), new Point3D(minx, miny, maxz),
                    new Vector3D(-1, 0, 0));
                AddFace(
                    new Point3D(maxx, miny, minz), new Point3D(maxx, miny, maxz),
                    new Point3D(maxx, maxy, maxz), new Point3D(maxx, maxy, minz),
                    new Vector3D(1, 0, 0));
                AddFace(
                    new Point3D(minx, miny, minz), new Point3D(minx, miny, maxz),
                    new Point3D(maxx, miny, maxz), new Point3D(maxx, miny, minz),
                    new Vector3D(0, -1, 0));
                AddFace(
                    new Point3D(minx, maxy, minz), new Point3D(maxx, maxy, minz),
                    new Point3D(maxx, maxy, maxz), new Point3D(minx, maxy, maxz),
                    new Vector3D(0, 1, 0));
            }

            private void AddFace(Point3D a, Point3D b, Point3D c, Point3D d,
                Vector3D normal)
            {
                int ia = AddVertex(a, normal);
                int ib = AddVertex(b, normal);
                int ic = AddVertex(c, normal);
                int id = AddVertex(d, normal);
                AddTriangle(ia, ib, ic);
                AddTriangle(ia, ic, id);
            }

            private void AddQuad(QuickLine3dPrimitive p)
            {
                var a = new Point3D(p.A1x, p.A1y, p.A1z);
                var b = new Point3D(p.B1x, p.B1y, p.B1z);
                var c = new Point3D(p.A2x, p.A2y, p.A2z);
                var d = new Point3D(p.B2x, p.B2y, p.B2z);
                Vector3D normal = Vector3D.CrossProduct(b - a, d - a);
                if (normal.LengthSquared < 1e-18) return;
                normal.Normalize();
                int ia = AddVertex(a, normal);
                int ib = AddVertex(b, normal);
                int ic = AddVertex(c, normal);
                int id = AddVertex(d, normal);
                AddTriangle(ia, ib, ic);
                AddTriangle(ia, ic, id);
            }

            private void AddWaffleCell(QuickLine3dPrimitive p)
            {
                const int segments = 12;
                double centerx = (p.A1x + p.B1x) / 2.0;
                double centery = (p.A1y + p.B1y) / 2.0;
                double halfx = (p.B1x - p.A1x) / 2.0;
                double halfy = (p.B1y - p.A1y) / 2.0;
                var normal = new Vector3D(0, 0, 1);
                int first = _positions.Count;
                AddVertex(new Point3D(centerx + halfx, centery + halfy, p.A1z), normal);
                AddVertex(new Point3D(centerx - halfx, centery + halfy, p.A1z), normal);
                AddVertex(new Point3D(centerx - halfx, centery - halfy, p.A1z), normal);
                AddVertex(new Point3D(centerx + halfx, centery - halfy, p.A1z), normal);
                for (int index = 0; index < segments; index++)
                {
                    Vector3D direction = WaffleDirections[index];
                    AddVertex(new Point3D(centerx + direction.X * p.Radius,
                        centery + direction.Y * p.Radius, p.A1z), normal);
                }
                // Split each side between its two corners to stay outside the hole.
                for (int corner = 0; corner < 4; corner++)
                {
                    int start = 1 + corner * 3;
                    AddTriangle(first + corner, first + (corner + 1) % 4,
                        first + 4 + (start + 2) % segments);
                    for (int edge = 2; edge > 0; edge--)
                        AddTriangle(first + corner,
                            first + 4 + (start + edge) % segments,
                            first + 4 + (start + edge - 1) % segments);
                    AddTriangle(first + (corner + 1) % 4,
                        first + 4 + (start + 3) % segments,
                        first + 4 + (start + 2) % segments);
                }
            }

            private static Vector3D[] CreateWaffleDirections()
            {
                const int segments = 12;
                var directions = new Vector3D[segments];
                for (int index = 0; index < segments; index++)
                {
                    // Align every third hole vertex with a square corner.
                    double angle = (index + 0.5) * Math.PI * 2.0 / segments;
                    directions[index] = new Vector3D(
                        Math.Cos(angle), Math.Sin(angle), 0);
                }
                return directions;
            }

            private void AddSphere(QuickLine3dPrimitive p)
            {
                if (p.Radius <= 0) return;
                var center = new Point3D(p.A1x, p.A1y, p.A1z);
                int firstVertex = _positions.Count;
                foreach (Vector3D normal in SphereNormals)
                {
                    AddVertex(center + normal * p.Radius, normal);
                }
                for (int index = 0; index < SphereTriangles.Length; index += 3)
                {
                    AddTriangle(firstVertex + SphereTriangles[index],
                        firstVertex + SphereTriangles[index + 1],
                        firstVertex + SphereTriangles[index + 2]);
                }
            }

            private static readonly Vector3D[] SphereNormals =
            {
                new Vector3D(1, 0, 0), new Vector3D(-1, 0, 0),
                new Vector3D(0, 1, 0), new Vector3D(0, -1, 0),
                new Vector3D(0, 0, 1), new Vector3D(0, 0, -1)
            };

            private static readonly int[] SphereTriangles =
            {
                4, 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0,
                5, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3
            };
        }

        /// <summary>标签条目（场景锚点 + 叠加层元素）。</summary>
        private sealed class LabelEntry
        {
            internal LabelEntry(QuickLine3dSceneLabel scene)
            {
                Scene = scene;
            }

            internal QuickLine3dSceneLabel Scene { get; }
            internal TextBlock Element { get; set; }
        }

    }
}
