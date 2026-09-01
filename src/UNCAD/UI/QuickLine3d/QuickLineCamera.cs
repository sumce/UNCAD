using System;
using OpenTK;
using UNCAD.Core.QuickLine;

namespace UNCAD.UI.QuickLine3d
{
    /// <summary>
    /// 正交相机:AutoCAD 东南等轴测初始视角,左键轨道/滚轮缩放/中键平移。
    /// 世界坐标单位为毫米。
    /// </summary>
    public sealed class QuickLineCamera
    {
        private double _yawRadians = Math.PI * 0.75;   // 东南等轴测方位
        private double _pitchRadians = Math.PI * 0.25; // ≈35.264° 等轴测俯仰
        private double _halfWidth = 3000;
        private Vector3 _target = Vector3.Zero;

        public Matrix4 ViewMatrix
        {
            get
            {
                Vector3 eye = _target + EyeDirection * (float)ViewDistance();
                return Matrix4.LookAt(eye, _target, Vector3.UnitZ);
            }
        }

        public Matrix4 ProjectionMatrix(float aspect)
        {
            if (aspect <= 0) aspect = 1f;
            return Matrix4.CreateOrthographic(
                (float)(_halfWidth * 2 * aspect), (float)(_halfWidth * 2), 1f, 1e7f);
        }

        public float ViewHalfWidth => (float)_halfWidth;

        public Vector3 Target => _target;

        /// <summary>从目标指向眼睛的单位方向。</summary>
        public Vector3 EyeDirection => new Vector3(
            (float)(Math.Cos(_pitchRadians) * Math.Cos(_yawRadians)),
            (float)(Math.Cos(_pitchRadians) * Math.Sin(_yawRadians)),
            (float)Math.Sin(_pitchRadians));

        public void Orbit(double deltaYawDegrees, double deltaPitchDegrees)
        {
            _yawRadians += deltaYawDegrees * Math.PI / 180.0;
            _pitchRadians = Math.Min(Math.PI * 0.495,
                Math.Max(0.05, _pitchRadians + deltaPitchDegrees * Math.PI / 180.0));
        }

        public void Zoom(double factor)
        {
            _halfWidth = Math.Min(5e6, Math.Max(1.0, _halfWidth * factor));
        }

        public void Pan(Vector3 worldDelta) => _target += worldDelta;

        /// <summary>按场景包围盒自动取景。</summary>
        public void Fit(Vector3 min, Vector3 max)
        {
            Vector3 size = max - min;
            double extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            _halfWidth = Math.Max(500.0, extent * 0.75);
            _target = (min + max) * 0.5f;
        }

        /// <summary>世界坐标 → 控件客户区像素(原点左上)。</summary>
        public System.Drawing.PointF WorldToClient(Vector3 world,
            Matrix4 view, Matrix4 projection, float viewportWidth, float viewportHeight)
        {
            Vector4 clip = new Vector4(world, 1f) * view * projection;
            if (Math.Abs(clip.W) < 1e-9f) return System.Drawing.PointF.Empty;
            float nx = clip.X / clip.W;
            float ny = clip.Y / clip.W;
            return new System.Drawing.PointF(
                (nx * 0.5f + 0.5f) * viewportWidth,
                (0.5f - ny * 0.5f) * viewportHeight);
        }

        /// <summary>客户区像素 → 过目标点、平行视线方向平面的世界坐标(用于鼠标拾取)。</summary>
        public Vector3 ClientToWorldOnTargetPlane(System.Drawing.PointF client,
            Matrix4 view, Matrix4 projection, float viewportWidth, float viewportHeight)
        {
            float nx = client.X / viewportWidth * 2f - 1f;
            float ny = 1f - client.Y / viewportHeight * 2f;
            Matrix4 inverse = Matrix4.Invert(projection * view);
            Vector4 near = new Vector4(nx, ny, -1f, 1f) * inverse;
            Vector4 far = new Vector4(nx, ny, 1f, 1f) * inverse;
            Vector3 a = new Vector3(near.X, near.Y, near.Z) / near.W;
            Vector3 b = new Vector3(far.X, far.Y, far.Z) / far.W;
            Vector3 direction = b - a;
            // 与经过 target、法向=视线方向的平面求交。
            Vector3 normal = EyeDirection;
            float denominator = Vector3.Dot(direction, normal);
            if (Math.Abs(denominator) < 1e-9f) return _target;
            float t = Vector3.Dot(_target - a, normal) / denominator;
            return a + direction * t;
        }

        private double ViewDistance() => _halfWidth * 4.0;
    }
}
