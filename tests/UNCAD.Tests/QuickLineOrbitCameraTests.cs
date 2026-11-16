using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.3.0（D-054 特性③）：正交轨道相机的纯数学锁死——方位角 45°/俯仰 30°
    /// 的三轴单位向量投影期望值、缩放线性、地面归零 minZ=0。
    /// </summary>
    public sealed class QuickLineOrbitCameraTests
    {
        private const double Tolerance = 1e-6;

        [Fact]
        public void Basis_AtAzimuth45Elevation30_MatchesHandDerivedValues()
        {
            // 手算基准（相机单位方向 E=(0.6123724,0.6123724,0.5)，视线 v=−E）：
            // 右向 r=normalize(v×ẑ)=(−√2/2, √2/2, 0)；
            // 上向 u=r×v=(−0.3535534, −0.3535534, 0.8660254)。
            QuickLineOrbitCamera.CameraBasis basis = QuickLineOrbitCamera
                .ComputeBasis(45.0, 30.0);
            Assert.Equal(-Math.Sqrt(2) / 2.0, basis.RightX, 6);
            Assert.Equal(Math.Sqrt(2) / 2.0, basis.RightY, 6);
            Assert.Equal(0.0, basis.RightZ, 6);
            Assert.Equal(-0.35355339, basis.UpX, 6);
            Assert.Equal(-0.35355339, basis.UpY, 6);
            Assert.Equal(0.86602540, basis.UpZ, 6);
            // 基向量必须是单位向量且互相正交。
            Assert.Equal(1.0, Math.Sqrt(basis.RightX * basis.RightX
                + basis.RightY * basis.RightY + basis.RightZ * basis.RightZ), 6);
            Assert.Equal(1.0, Math.Sqrt(basis.UpX * basis.UpX
                + basis.UpY * basis.UpY + basis.UpZ * basis.UpZ), 6);
        }

        [Fact]
        public void Project_AtAzimuth45Elevation30_UnitAxesMatchExpected()
        {
            var camera = QuickLineOrbitCamera.Create();
            camera.SetOrientation(45.0, 30.0);

            QuickLinePoint x = camera.Project(new QuickLineSpatialPoint(1, 0, 0));
            Assert.Equal(-Math.Sqrt(2) / 2.0, x.X, Tolerance);
            Assert.Equal(-0.35355339, x.Y, Tolerance);

            QuickLinePoint y = camera.Project(new QuickLineSpatialPoint(0, 1, 0));
            Assert.Equal(Math.Sqrt(2) / 2.0, y.X, Tolerance);
            Assert.Equal(-0.35355339, y.Y, Tolerance);

            QuickLinePoint z = camera.Project(new QuickLineSpatialPoint(0, 0, 1));
            Assert.Equal(0.0, z.X, Tolerance);
            Assert.Equal(0.86602540, z.Y, Tolerance);
        }

        [Fact]
        public void Zoom_IsLinearInProjection()
        {
            var camera = QuickLineOrbitCamera.Create();
            camera.SetOrientation(0.0, 0.0);
            var point = new QuickLineSpatialPoint(3000, 0, 0);

            QuickLinePoint atOne = camera.Project(point);
            camera.ZoomBy(2.5);
            QuickLinePoint atTwoAndHalf = camera.Project(point);
            camera.ZoomBy(1.0 / 2.5);
            QuickLinePoint back = camera.Project(point);

            Assert.Equal(QuickLineOrbitCamera.DefaultZoom, camera.Zoom, 6);
            Assert.Equal(atOne.X * 2.5, atTwoAndHalf.X, Tolerance);
            Assert.Equal(atOne.Y * 2.5, atTwoAndHalf.Y, Tolerance);
            Assert.Equal(atOne.X, back.X, Tolerance);
            Assert.Equal(atOne.Y, back.Y, Tolerance);
            // 方位 0°/俯仰 0° 时相机在 +X 看 −X：屏幕右 = +Y，上 = +Z，
            // 3000mm 的 X 向线段垂直屏幕（投影在原点）——顺手锁死基向量朝向。
            Assert.Equal(0.0, atOne.X, Tolerance);
            Assert.Equal(0.0, atOne.Y, Tolerance);
            QuickLinePoint yAxis = camera.Project(new QuickLineSpatialPoint(0, 2000, 0));
            Assert.Equal(2000.0, yAxis.X, Tolerance);
            Assert.Equal(0.0, yAxis.Y, Tolerance);
            QuickLinePoint zAxis = camera.Project(new QuickLineSpatialPoint(0, 0, 1000));
            Assert.Equal(0.0, zAxis.X, Tolerance);
            Assert.Equal(1000.0, zAxis.Y, Tolerance);
        }

        [Fact]
        public void Orbit_ClampsElevationAndKeepsZoom()
        {
            var camera = QuickLineOrbitCamera.Create();
            camera.SetOrientation(0.0, 30.0);
            camera.ZoomBy(3.0);
            camera.Orbit(10.0, 500.0);
            Assert.Equal(89.9, camera.ElevationDegrees, 6);
            Assert.Equal(3.0, camera.Zoom, 6);
            camera.Orbit(0.0, -1000.0);
            Assert.Equal(-89.9, camera.ElevationDegrees, 6);
            camera.Reset();
            Assert.Equal(QuickLineOrbitCamera.DefaultAzimuthDegrees,
                camera.AzimuthDegrees, 6);
            Assert.Equal(QuickLineOrbitCamera.DefaultElevationDegrees,
                camera.ElevationDegrees, 6);
            Assert.Equal(QuickLineOrbitCamera.DefaultZoom, camera.Zoom, 6);
        }

        [Fact]
        public void FromScene_GroundNormalizesSoMinimumZIsZero()
        {
            // 连通链：水平 1000mm + 竖直向下 4000mm——重建后末端 Z=-4000，
            // 地面归零应把最低端点抬到 0（整体平移，段内高度差不变）。
            var graph = QuickLineGraph.Build(new List<QuickLineSegment>
            {
                new QuickLineSegment("h", new QuickLinePoint(0, 0),
                    new QuickLinePoint(1000, 0)),
                new QuickLineSegment("v", new QuickLinePoint(1000, 0),
                    new QuickLinePoint(1000, -4000))
            }, 1.0);
            var distances = new Dictionary<string, double>
            {
                ["h"] = 1000.0,
                ["v"] = 4000.0
            };
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, distances,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(-4000.0, scene.Nodes.Min(node => node.Position.Z), 6);

            QuickLineOrbitPreviewModel model =
                QuickLineOrbitPreviewModel.FromScene(scene, "轴位摘要");
            Assert.NotNull(model);
            Assert.Equal(2, model.Segments.Count);
            double minZ = model.Segments.Min(segment =>
                Math.Min(segment.Start.Z, segment.End.Z));
            Assert.Equal(0.0, minZ, 6);
            Assert.Equal("轴位摘要", model.AxisSummary);
            // 地面归零是整体平移：竖直段的 4000mm 高差保持不变。
            QuickLineOrbitPreviewSegment vertical = model.Segments.Single(item =>
                item.Axis == QuickLineSpatialAxis.Z);
            Assert.Equal(4000.0, Math.Abs(vertical.Start.Z - vertical.End.Z), 6);
            // 原场景不被修改。
            Assert.Equal(-4000.0, scene.Nodes.Min(node => node.Position.Z), 6);
        }

        [Fact]
        public void FromScene_EmptySceneReturnsNull()
        {
            Assert.Null(QuickLineOrbitPreviewModel.FromScene(null, ""));
            var empty = new QuickLineIsometricScene("N1",
                QuickLineProjectionMode.Isometric,
                new List<QuickLineIsometricNode>(),
                new List<QuickLineIsometricSegment>(), new List<string>());
            Assert.Null(QuickLineOrbitPreviewModel.FromScene(empty, ""));
        }

        [Fact]
        public void AxisSpan_ParsesTolerantFormsAndDescribesSpan()
        {
            Assert.True(QuickLineAxisSpan.TryParse("48/V",
                out QuickLineAxisReference full));
            Assert.Equal(48, full.Number);
            Assert.Equal("V", full.Letter);
            Assert.True(QuickLineAxisSpan.TryParse("48-V",
                out QuickLineAxisReference dashed));
            Assert.Equal(48, dashed.Number);
            Assert.Equal("V", dashed.Letter);
            Assert.True(QuickLineAxisSpan.TryParse("48", out QuickLineAxisReference numeric));
            Assert.True(numeric.HasNumber);
            Assert.False(numeric.HasLetter);
            Assert.True(QuickLineAxisSpan.TryParse(" V ", out QuickLineAxisReference letters));
            Assert.False(letters.HasNumber);
            Assert.Equal("V", letters.Letter);

            Assert.False(QuickLineAxisSpan.TryParse("", out _));
            Assert.False(QuickLineAxisSpan.TryParse("48V", out _),
                "数字+字母无分隔符不猜");
            Assert.False(QuickLineAxisSpan.TryParse("48/V/x", out _));
            Assert.False(QuickLineAxisSpan.TryParse("轴A", out _));
            Assert.True(QuickLineAxisSpan.TryParse("ABC",
                out QuickLineAxisReference multiLetter));
            Assert.Equal("ABC", multiLetter.Letter);

            // 展示文案：两端数字轴可算跨度（48−37=11 跨 × 4.8m = 52.8m）。
            string described = QuickLineAxisSpan.Describe("48/V", "37/V");
            Assert.StartsWith("上游 48/V ↔ 下游 37/V", described, StringComparison.Ordinal);
            Assert.Contains("Δ11 跨 × 4.8m ≈ 52.8m", described, StringComparison.Ordinal);
            // 解析失败：显示原文，不算跨度（不猜测）。
            string raw = QuickLineAxisSpan.Describe("轴A", "37/V");
            Assert.Equal("上游 轴A ↔ 下游 37/V", raw);
            // 字母轴也能给跨度（V=22、P=16，差 6 跨）。
            string letterOnly = QuickLineAxisSpan.Describe("V", "P");
            Assert.Contains("Δ6 跨", letterOnly, StringComparison.Ordinal);
            // 半边值（一端只有数字、另一端只有字母）不算跨度。
            Assert.DoesNotContain("跨", QuickLineAxisSpan.Describe("48", "V"),
                StringComparison.Ordinal);
        }
    }
}
