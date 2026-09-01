using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using UNCAD.Core.QuickLine;
using UNCAD.Core.Text;

namespace UNCAD.UI.QuickLine3d
{
    /// <summary>
    /// OpenGL 线框画布:轨道/缩放/平移、鼠标方向拾取、橡皮筋预览与
    /// GDI 叠加标注(中文渲染走 Graphics,避免 GL 字体依赖)。
    /// </summary>
    internal sealed class QuickLineGlCanvas : GLControl
    {
        private readonly QuickLineSceneState _state;
        private readonly QuickLineCamera _camera = new QuickLineCamera();
        private bool _orbiting;
        private bool _panning;
        private Point _lastMouse;
        private readonly Font _labelFont;
        private readonly Font _hintFont;

        // 鼠标绘制:CAD 极轴追踪 — 移动即预览,单击落点。
        private static readonly (QuickLineSpatialAxis axis, int sign)[] Handles =
        {
            (QuickLineSpatialAxis.X, 1), (QuickLineSpatialAxis.X, -1),
            (QuickLineSpatialAxis.Y, 1), (QuickLineSpatialAxis.Y, -1),
            (QuickLineSpatialAxis.Z, 1), (QuickLineSpatialAxis.Z, -1)
        };
        private const double SnapMillimetres = 100.0;
        /// <summary>方向粘滞余量:新轴点积领先当前轴这么多才切换,防止辅助线乱跳。</summary>
        private const float StickyMargin = 0.12f;
        private int _stickyAxis = -1;
        private PointF _cursorClient;

        public QuickLineGlCanvas(QuickLineSceneState state)
            : base(new GraphicsMode(new ColorFormat(8, 8, 8, 8), 24, 0, 0),
                3, 0, GraphicsContextFlags.Default)
        {
            _state = state;
            _labelFont = UiTheme.FontBody;
            _hintFont = UiTheme.FontCaption;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.WindowBg;
            VSync = false;
            state.Changed += Invalidate;
            MouseDown += OnCanvasMouseDown;
            MouseMove += OnCanvasMouseMove;
            MouseUp += OnCanvasMouseUp;
            MouseWheel += OnCanvasMouseWheel;
            Resize += (sender, args) => Invalidate();
            Load += (sender, args) =>
            {
                GL.ClearColor(UiTheme.WindowBg);
                GL.Enable(EnableCap.LineSmooth);
            };
            Disposed += (sender, args) => _state.Changed -= Invalidate;
        }

        private Matrix4 View => _camera.ViewMatrix;
        private Matrix4 Projection => _camera.ProjectionMatrix(
            Math.Max(Width, 1) / (float)Math.Max(Height, 1));

        // ---------- 交互 ----------

        private void OnCanvasMouseDown(object sender, MouseEventArgs e)
        {
            _lastMouse = e.Location;
            if (e.Button == MouseButtons.Middle)
            {
                _panning = true;
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (_state.Drawing)
            {
                // 极轴追踪:移动即预览,单击直接落点。
                if (_state.PreviewLength > 0.0)
                {
                    CommitArmed();
                    return;
                }
            }
            else if (TryPickSegment(e.Location)) return; // 编辑模式点选线段
            _orbiting = true;
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            var delta = new Size(e.X - _lastMouse.X, e.Y - _lastMouse.Y);
            if (_orbiting)
            {
                _camera.Orbit(delta.Width * 0.4, delta.Height * 0.4);
                Invalidate();
            }
            else if (_panning)
            {
                float worldPerPixel = _camera.ViewHalfWidth * 2f / Math.Max(Height, 1);
                Matrix4 view = View;
                Vector3 right = Vector3.TransformVector(Vector3.UnitX, view);
                Vector3 up = Vector3.TransformVector(Vector3.UnitY, view);
                right.Normalize();
                up.Normalize();
                _camera.Pan(-right * delta.Width * worldPerPixel
                    + up * delta.Height * worldPerPixel);
                Invalidate();
            }
            else if (_state.Drawing)
            {
                TrackPolarAxis(e.Location, e);
            }
            _lastMouse = e.Location;
        }

        /// <summary>
        /// CAD 极轴追踪:光标世界坐标相对活动节点的矢量,与 6 个单位轴取点积,
        /// 最大者为吸附方向;长度 = 该方向的投影长度(连续)。带方向粘滞,
        /// 越过 45° 边界一定余量才切换,辅助线不乱跳。
        /// </summary>
        private void TrackPolarAxis(Point location, MouseEventArgs e)
        {
            _cursorClient = location;
            Vector3 node = ToVector(_state.NodePosition(_state.ActiveNodeId));
            Vector3 cursor = _camera.ClientToWorldOnTargetPlane(location,
                View, Projection, Width, Height);
            Vector3 delta = cursor - node;
            if (delta.LengthSquared < 1e-6f) return;

            int best = 0;
            float bestDot = float.NegativeInfinity;
            for (int i = 0; i < Handles.Length; i++)
            {
                float d = Vector3.Dot(delta, AxisWorldVector(Handles[i].axis,
                    Handles[i].sign));
                if (d > bestDot) { bestDot = d; best = i; }
            }
            if (_stickyAxis >= 0 && best != _stickyAxis)
            {
                float stickyDot = Vector3.Dot(delta, AxisWorldVector(
                    Handles[_stickyAxis].axis, Handles[_stickyAxis].sign));
                // 粘滞:新方向必须明显更优才切换;否则维持原方向。
                if (bestDot > stickyDot * (1f + StickyMargin))
                    _stickyAxis = best;
                best = _stickyAxis;
            }
            else
            {
                _stickyAxis = best;
            }

            double rawLength = Math.Max(0f, bestDot);
            double length = (Control.ModifierKeys & Keys.Shift) != 0 ? rawLength
                : Math.Round(rawLength / SnapMillimetres) * SnapMillimetres;
            _state.ArmPreview(Handles[best].axis, Handles[best].sign, length);
        }

        private void OnCanvasMouseUp(object sender, MouseEventArgs e)
        {
            _orbiting = false;
            _panning = false;
        }

        private void CommitArmed()
        {
            _state.CommitArmedPreview();
            _stickyAxis = -1; // 新一段重新开始方向追踪
            _cursorClient = PointF.Empty;
            EnsureRouteVisible();
        }

        /// <summary>路线超出当前视野时才重新取景,避免每次落点重置缩放。</summary>
        public void EnsureRouteVisible()
        {
            if (_state.Segments.Count == 0) return;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (QuickLineMutableSegment segment in _state.Segments)
            {
                Expand(ref min, ref max, ToVector(
                    _state.NodePosition(segment.StartNodeId)));
                Expand(ref min, ref max, ToVector(
                    _state.NodePosition(segment.EndNodeId)));
            }
            Vector3 size = max - min;
            float extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (extent * 0.75f > _camera.ViewHalfWidth * 1.15f) FitRoute();
        }

        private static Vector3 AxisWorldVector(QuickLineSpatialAxis axis, int sign)
        {
            int s = sign < 0 ? -1 : 1;
            switch (axis)
            {
                case QuickLineSpatialAxis.X: return Vector3.UnitX * s;
                case QuickLineSpatialAxis.Y: return Vector3.UnitY * s;
                default: return Vector3.UnitZ * s;
            }
        }

        private void OnCanvasMouseWheel(object sender, MouseEventArgs e)
        {
            _camera.Zoom(e.Delta > 0 ? 0.85 : 1.0 / 0.85);
            Invalidate();
        }

        private bool TryPickSegment(Point client)
        {
            Matrix4 view = View;
            Matrix4 projection = Projection;
            float best = 12f;
            string picked = null;
            foreach (QuickLineMutableSegment segment in _state.Segments)
            {
                PointF a = _camera.WorldToClient(ToVector(
                    _state.NodePosition(segment.StartNodeId)),
                    view, projection, Width, Height);
                PointF b = _camera.WorldToClient(ToVector(
                    _state.NodePosition(segment.EndNodeId)),
                    view, projection, Width, Height);
                float distance = DistancePointToSegment(client, a, b);
                if (!(distance < best)) continue;
                best = distance;
                picked = segment.Id;
            }
            return picked != null && _state.SelectSegment(picked);
        }

        private static float DistancePointToSegment(Point p, PointF a, PointF b)
        {
            float abx = b.X - a.X, aby = b.Y - a.Y;
            float apx = p.X - a.X, apy = p.Y - a.Y;
            float lengthSquared = abx * abx + aby * aby;
            float t = lengthSquared <= 1e-6f ? 0f
                : Math.Max(0f, Math.Min(1f, (apx * abx + apy * aby) / lengthSquared));
            float dx = apx - abx * t, dy = apy - aby * t;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>按当前内容自动取景。</summary>
        public void FitRoute()
        {
            if (_state.Segments.Count == 0)
            {
                Vector3 origin = ToVector(_state.NodePosition(_state.ActiveNodeId));
                _camera.Fit(origin - new Vector3(2000), origin + new Vector3(2000));
            }
            else
            {
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                foreach (QuickLineMutableSegment segment in _state.Segments)
                {
                    Expand(ref min, ref max, ToVector(
                        _state.NodePosition(segment.StartNodeId)));
                    Expand(ref min, ref max, ToVector(
                        _state.NodePosition(segment.EndNodeId)));
                }
                _camera.Fit(min, max);
            }
            Invalidate();
        }

        private static void Expand(ref Vector3 min, ref Vector3 max, Vector3 point)
        {
            min = Vector3.ComponentMin(min, point);
            max = Vector3.ComponentMax(max, point);
        }

        private static Vector3 ToVector(QuickLineSpatialPoint point)
            => new Vector3((float)point.X, (float)point.Y, (float)point.Z);

        // ---------- 渲染 ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!IsHandleCreated || DesignMode) { base.OnPaint(e); return; }
            try { MakeCurrent(); }
            catch { base.OnPaint(e); return; }
            GL.Viewport(0, 0, Width, Height);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            Matrix4 view = View;
            Matrix4 projection = Projection;
            DrawGrid(view, projection);
            DrawAxes(view, projection);
            foreach (QuickLineMutableSegment segment in _state.Segments)
            {
                Color color = SelectedColor(segment);
                bool dashed = false;
                if (!segment.Completed) { color = UiTheme.BrandOrange; }
                else if (string.Equals(segment.Id, _state.SelectedSegmentId,
                    StringComparison.Ordinal)) { color = UiTheme.Accent; }
                Vector3 a = ToVector(_state.NodePosition(segment.StartNodeId));
                Vector3 b = ToVector(_state.NodePosition(segment.EndNodeId));
                if (!segment.Completed)
                {
                    // 未确认段的渲染长度走 DisplayDistanceMillimetres。
                    Vector3 delta = b - a;
                    double display = segment.DisplayDistanceMillimetres;
                    double actual = segment.DistanceMillimetres;
                    if (actual > 1e-9)
                    {
                        delta *= (float)(display / actual);
                        b = a + delta;
                    }
                }
                DrawLine(a, b, color, view, projection, dashed);
            }
            QuickLineMutableSegment preview = _state.PreviewSegment;
            if (preview != null && _state.PreviewLength > 0.0)
            {
                Vector3 a = ToVector(_state.NodePosition(_state.ActiveNodeId));
                Vector3 b = a + AxisWorldVector(preview.Axis, preview.DirectionSign)
                    * (float)_state.PreviewLength;
                DrawLine(a, b, UiTheme.Accent, view, projection, true);
            }
            SwapBuffers();
            DrawOverlay(e.Graphics, view, projection);
        }

        private static Color SelectedColor(QuickLineMutableSegment segment)
            => segment.Completed ? UiTheme.TextPrimary : UiTheme.BrandOrange;

        private void DrawGrid(Matrix4 view, Matrix4 projection)
        {
            // XZ 地面网格,覆盖当前视野量级。
            float half = Math.Max(2000f, _camera.ViewHalfWidth * 1.5f);
            float step = half > 20000 ? 10000f : half > 4000 ? 2000f : 500f;
            var model = Matrix4.Identity;
            GL.UseProgram(0);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadMatrix(ref view);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref projection);
            GL.Begin(PrimitiveType.Lines);
            GL.Color3(UiTheme.Border);
            for (float x = -half; x <= half; x += step)
            {
                GL.Vertex3(x, -half, 0); GL.Vertex3(x, half, 0);
                GL.Vertex3(-half, x, 0); GL.Vertex3(half, x, 0);
            }
            GL.End();
        }

        private void DrawAxes(Matrix4 view, Matrix4 projection)
        {
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadMatrix(ref view);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref projection);
            GL.Begin(PrimitiveType.Lines);
            GL.Color3(Color.FromArgb(180, UiTheme.Accent));
            GL.Vertex3(0, 0, 0); GL.Vertex3(1000, 0, 0);
            GL.Color3(Color.FromArgb(180, UiTheme.SuccessFg));
            GL.Vertex3(0, 0, 0); GL.Vertex3(0, 1000, 0);
            GL.Color3(Color.FromArgb(180, UiTheme.WarningFg));
            GL.Vertex3(0, 0, 0); GL.Vertex3(0, 0, 1000);
            GL.End();
        }

        private void DrawLine(Vector3 a, Vector3 b, Color color,
            Matrix4 view, Matrix4 projection, bool dashed)
        {
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadMatrix(ref view);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref projection);
            if (dashed)
            {
                GL.Enable(EnableCap.LineStipple);
                GL.LineStipple(1, 0x0F0F);
            }
            GL.LineWidth(1.6f);
            GL.Begin(PrimitiveType.Lines);
            GL.Color3(color);
            GL.Vertex3(a);
            GL.Vertex3(b);
            GL.End();
            if (dashed) GL.Disable(EnableCap.LineStipple);
        }

        private void DrawOverlay(Graphics g, Matrix4 view, Matrix4 projection)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            foreach (QuickLineMutableSegment segment in _state.Segments)
            {
                Vector3 mid = (ToVector(_state.NodePosition(segment.StartNodeId))
                    + ToVector(_state.NodePosition(segment.EndNodeId))) * 0.5f;
                PointF point = _camera.WorldToClient(mid, view, projection, Width, Height);
                string text = segment.Completed
                    ? TextFormatter.FormatNum(segment.DistanceMillimetres) + "mm"
                    : TextFormatter.FormatNum(segment.DisplayDistanceMillimetres) + "mm(未确认)";
                DrawShadowText(g, text, point, _labelFont, UiTheme.TextSecondary);
            }
            if (_state.Drawing && _state.PreviewLength > 0.0)
            {
                // 长度提示跟随光标。
                DrawShadowText(g, TextFormatter.FormatNum(_state.PreviewLength) + "mm",
                    _cursorClient, _labelFont, UiTheme.Accent);
            }
        }

        private static void DrawShadowText(Graphics g, string text, PointF point,
            Font font, Color color)
        {
            var layout = new PointF(point.X + 6, point.Y - font.Height - 2);
            using var brush = new SolidBrush(Color.FromArgb(160, UiTheme.Surface));
            g.DrawString(text, font, brush, layout.X + 1, layout.Y + 1);
            using var fore = new SolidBrush(color);
            g.DrawString(text, font, fore, layout);
        }
    }
}
