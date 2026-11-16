using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// 3.3.0（D-054）：正交轨道相机——方位角/俯仰角绕场景旋转 + 缩放的纯数学
    /// 投影器，把 <see cref="QuickLineSpatialPoint"/> 投到 2D 屏幕坐标。WPF
    /// Canvas 自绘预览用它，**禁止引入第三方 3D 库**（HelixToolkit 等）。
    ///
    /// 约定：相机从「目标点 + 单位方向 E(方位角, 俯仰角)」处看向目标点，
    /// 视线 v = −E；屏幕右向 r = normalize(v × ẑ)，屏幕上向 u = r × v。
    /// 投影 = ((p − target)·r × zoom, (p − target)·u × zoom)，Y 轴向上为正
    /// （控件层再做一次 Y 翻转，画布向下为正）。俯仰角钳制在 ±89.9°，
    /// 保证视线永不与世界上向 ẑ 平行（r 退化）。
    /// </summary>
    public sealed class QuickLineOrbitCamera
    {
        /// <summary>默认初始视角：从西南上方看（方位 −135°、俯仰 30°），与 U1L3D 预览习惯一致。</summary>
        public const double DefaultAzimuthDegrees = -135.0;
        public const double DefaultElevationDegrees = 30.0;
        public const double DefaultZoom = 1.0;
        private const double ElevationClampDegrees = 89.9;

        private QuickLineOrbitCamera(double azimuthDegrees, double elevationDegrees,
            double zoom)
        {
            AzimuthDegrees = azimuthDegrees;
            ElevationDegrees = elevationDegrees;
            Zoom = zoom;
        }

        public static QuickLineOrbitCamera Create()
            => new QuickLineOrbitCamera(DefaultAzimuthDegrees,
                DefaultElevationDegrees, DefaultZoom);

        /// <summary>绕 Z 轴的方位角（度，逆时针为正，+X 方向为 0°）。</summary>
        public double AzimuthDegrees { get; private set; }

        /// <summary>俯仰角（度，水平面以上为正，钳制 ±89.9°）。</summary>
        public double ElevationDegrees { get; private set; }

        /// <summary>缩放系数（正数；屏幕坐标线性乘以它）。</summary>
        public double Zoom { get; private set; }

        /// <summary>轨道旋转：方位角加差值、俯仰角加差值后钳制。</summary>
        public void Orbit(double deltaAzimuthDegrees, double deltaElevationDegrees)
        {
            SetOrientation(AzimuthDegrees + deltaAzimuthDegrees,
                ElevationDegrees + deltaElevationDegrees);
        }

        /// <summary>直接设置朝向（度）——拖动交互用绝对角度驱动；缩放不变。</summary>
        public void SetOrientation(double azimuthDegrees, double elevationDegrees)
        {
            if (double.IsNaN(azimuthDegrees) || double.IsInfinity(azimuthDegrees)
                || double.IsNaN(elevationDegrees)
                || double.IsInfinity(elevationDegrees)) return;
            AzimuthDegrees = NormalizeDegrees(azimuthDegrees);
            ElevationDegrees = Math.Max(-ElevationClampDegrees,
                Math.Min(ElevationClampDegrees, elevationDegrees));
        }

        /// <summary>缩放：乘一个正系数（小于 1 缩小，大于 1 放大）。</summary>
        public void ZoomBy(double factor)
        {
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
                return;
            Zoom = Zoom * factor;
        }

        /// <summary>复位到默认视角。</summary>
        public void Reset()
        {
            AzimuthDegrees = DefaultAzimuthDegrees;
            ElevationDegrees = DefaultElevationDegrees;
            Zoom = DefaultZoom;
        }

        /// <summary>把空间点投到屏幕坐标（Y 向上为正）。</summary>
        public QuickLinePoint Project(QuickLineSpatialPoint point)
        {
            CameraBasis basis = ComputeBasis(AzimuthDegrees, ElevationDegrees);
            double zoom = Zoom;
            return new QuickLinePoint(
                (point.X * basis.RightX + point.Y * basis.RightY
                    + point.Z * basis.RightZ) * zoom,
                (point.X * basis.UpX + point.Y * basis.UpY
                    + point.Z * basis.UpZ) * zoom);
        }

        /// <summary>一次算出屏幕基向量（亦供单测直接锁死数值）。</summary>
        public static CameraBasis ComputeBasis(double azimuthDegrees,
            double elevationDegrees)
        {
            double azimuth = azimuthDegrees * Math.PI / 180.0;
            double elevation = elevationDegrees * Math.PI / 180.0;
            double cosElevation = Math.Cos(elevation);
            double sinElevation = Math.Sin(elevation);
            // 视线 v = −E，E 是相机相对场景的单位方向。
            double viewX = -cosElevation * Math.Cos(azimuth);
            double viewY = -cosElevation * Math.Sin(azimuth);
            double viewZ = -sinElevation;
            // 右向 r = normalize(v × ẑ) = normalize(viewY, −viewX, 0)。
            double rightLength = Math.Sqrt(viewX * viewX + viewY * viewY);
            if (rightLength < 1e-12)
            {
                // 俯仰角钳制后不可达；防御性兜底防止除零。
                return new CameraBasis(1.0, 0.0, 0.0, 0.0, 0.0, 1.0);
            }
            double rightX = viewY / rightLength;
            double rightY = -viewX / rightLength;
            // 上向 u = r × v。
            double upX = rightY * viewZ - 0.0 * viewY;
            double upY = 0.0 * viewX - rightX * viewZ;
            double upZ = rightX * viewY - rightY * viewX;
            return new CameraBasis(rightX, rightY, 0.0, upX, upY, upZ);
        }

        private static double NormalizeDegrees(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            return value;
        }

        /// <summary>正交相机的屏幕基向量（均为单位向量）。</summary>
        public readonly struct CameraBasis
        {
            public CameraBasis(double rightX, double rightY, double rightZ,
                double upX, double upY, double upZ)
            {
                RightX = rightX;
                RightY = rightY;
                RightZ = rightZ;
                UpX = upX;
                UpY = upY;
                UpZ = upZ;
            }

            public double RightX { get; }
            public double RightY { get; }
            public double RightZ { get; }
            public double UpX { get; }
            public double UpY { get; }
            public double UpZ { get; }
        }
    }

    /// <summary>3.3.0（D-054）：Canvas 3D 预览的一条线段（已地面归零）。</summary>
    public sealed class QuickLineOrbitPreviewSegment
    {
        public QuickLineOrbitPreviewSegment(string id, QuickLineSpatialPoint start,
            QuickLineSpatialPoint end, QuickLineSpatialAxis axis,
            double distanceMillimetres, bool completed)
        {
            Id = id ?? string.Empty;
            Start = start;
            End = end;
            Axis = axis;
            DistanceMillimetres = distanceMillimetres;
            Completed = completed;
        }

        public string Id { get; }
        public QuickLineSpatialPoint Start { get; }
        public QuickLineSpatialPoint End { get; }
        public QuickLineSpatialAxis Axis { get; }
        public double DistanceMillimetres { get; }
        public bool Completed { get; }
    }

    /// <summary>
    /// 3.3.0（D-054）：U1U 确认窗左侧 3D 预览的场景模型——由
    /// QuickLineIsometricSceneBuilder.BuildAll（带毫米标）重建后整体**地面归零**
    /// （所有节点 Z 减去最小 Z，「最低的那条线段的端点高度为 0」），附上游/
    /// 下游轴位摘要文字。纯数据：窗体只绘制，不重建场景（分层铁律）。
    /// </summary>
    public sealed class QuickLineOrbitPreviewModel
    {
        public QuickLineOrbitPreviewModel(
            IReadOnlyList<QuickLineOrbitPreviewSegment> segments,
            string axisSummary, IReadOnlyList<string> diagnostics)
        {
            Segments = segments ?? Array.Empty<QuickLineOrbitPreviewSegment>();
            AxisSummary = axisSummary ?? string.Empty;
            Diagnostics = diagnostics ?? Array.Empty<string>();
        }

        public IReadOnlyList<QuickLineOrbitPreviewSegment> Segments { get; }
        /// <summary>上游/下游轴位摘要（如「上游 48/V ↔ 下游 37/V · Δ11 跨 × 4.8m ≈ 52.8m」）。</summary>
        public string AxisSummary { get; }
        public IReadOnlyList<string> Diagnostics { get; }

        /// <summary>
        /// 由等测场景构建预览模型：整体地面归零（minZ→0），段端点取场景节点
        /// 坐标，毫米值取真实距离（DistanceMillimetres）。scene 为 null 或没有
        /// 线段时返回 null（调用方隐藏预览区）。
        /// </summary>
        public static QuickLineOrbitPreviewModel FromScene(
            QuickLineIsometricScene scene, string axisSummary)
        {
            if (scene == null || scene.Segments.Count == 0) return null;
            double minZ = scene.Nodes.Count == 0
                ? 0.0
                : scene.Nodes.Min(node => node.Position.Z);
            if (double.IsNaN(minZ) || double.IsInfinity(minZ)) minZ = 0.0;
            var positions = scene.Nodes.ToDictionary(node => node.Id,
                node => new QuickLineSpatialPoint(node.Position.X,
                    node.Position.Y, node.Position.Z - minZ),
                StringComparer.OrdinalIgnoreCase);
            var segments = new List<QuickLineOrbitPreviewSegment>(
                scene.Segments.Count);
            foreach (QuickLineIsometricSegment segment in scene.Segments)
            {
                if (!positions.TryGetValue(segment.StartNodeId,
                        out QuickLineSpatialPoint start)
                    || !positions.TryGetValue(segment.EndNodeId,
                        out QuickLineSpatialPoint end)) continue;
                segments.Add(new QuickLineOrbitPreviewSegment(segment.Id,
                    start, end, segment.Axis, segment.DistanceMillimetres,
                    segment.Completed));
            }
            if (segments.Count == 0) return null;
            return new QuickLineOrbitPreviewModel(segments, axisSummary,
                scene.Diagnostics);
        }
    }
}
