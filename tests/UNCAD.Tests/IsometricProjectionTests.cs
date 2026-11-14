using System;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// Xauto 重排/拱桥用的等测投影约定：与 SceneBuilder 的分类轴（X=30°、
    /// Z=90°、Y=150°）互为逆映射，深度沿视线零向量 (1,1,−1) 取 z−x−y。
    /// </summary>
    public class IsometricProjectionTests
    {
        private const double Sqrt3Over2 = 0.86602540378443864676;

        [Fact]
        public void IsometricAxes_ProjectToSketchScreenAnglesAtUnitLength()
        {
            Assert.Equal(30.0, AngleOf(IsometricProjection.ProjectIsometric(
                new QuickLineSpatialPoint(1000, 0, 0))), 6);
            Assert.Equal(150.0, AngleOf(IsometricProjection.ProjectIsometric(
                new QuickLineSpatialPoint(0, 1000, 0))), 6);
            Assert.Equal(90.0, AngleOf(IsometricProjection.ProjectIsometric(
                new QuickLineSpatialPoint(0, 0, 1000))), 6);

            Assert.Equal(1000.0, IsometricProjection.ProjectIsometric(
                new QuickLineSpatialPoint(1000, 0, 0)).DistanceTo(
                new QuickLinePoint(0, 0)), 6);
        }

        [Fact]
        public void Projection_InvertsSceneBuilderClassification()
        {
            // 一条按 30° 画的 3000mm 线会被分类成 +X；把 3D X 点投影回去
            // 必须落回同一条线上（重排的 round-trip 基础）。
            var drawn = new QuickLineSegment("s1",
                new QuickLinePoint(0, 0),
                new QuickLinePoint(3000 * Sqrt3Over2, 1500));
            Assert.True(QuickLineIsometricSceneBuilder.TryClassify(drawn, 3.0,
                out QuickLineSpatialAxis axis, out int directionSign,
                out _));
            Assert.Equal(QuickLineSpatialAxis.X, axis);
            Assert.Equal(1, directionSign);

            QuickLinePoint projected = IsometricProjection.ProjectIsometric(
                new QuickLineSpatialPoint(3000, 0, 0));
            Assert.Equal(3000 * Sqrt3Over2, projected.X, 6);
            Assert.Equal(1500.0, projected.Y, 6);
        }

        [Fact]
        public void Closeness_ElevatedRunIsNearerThanFloorRun()
        {
            // 规范交叉：沿 X 的地面线 A 与抬高的沿 Y 线 B 在纸面交叉，
            // 重建出的交点前像 (a−b) 沿 (1,1,−1)；俯视相机下 B 更近。
            var floorMid = new QuickLineSpatialPoint(5, 0, 0);
            var raisedMid = new QuickLineSpatialPoint(3, -2, 2);
            Assert.True(IsometricProjection.ClosenessToCamera(raisedMid,
                    QuickLineProjectionMode.Isometric)
                > IsometricProjection.ClosenessToCamera(floorMid,
                    QuickLineProjectionMode.Isometric));
            // 同一点沿视线方向平移不改变屏幕位置，只改变深度。
            var point = new QuickLineSpatialPoint(4, 4, 0);
            var towardCamera = new QuickLineSpatialPoint(3, 3, 1);
            QuickLinePoint screenPoint = IsometricProjection
                .ProjectIsometric(point);
            QuickLinePoint screenShifted = IsometricProjection
                .ProjectIsometric(towardCamera);
            Assert.Equal(screenPoint.X, screenShifted.X, 6);
            Assert.Equal(screenPoint.Y, screenShifted.Y, 6);
            Assert.True(IsometricProjection.ClosenessToCamera(towardCamera,
                    QuickLineProjectionMode.Isometric)
                > IsometricProjection.ClosenessToCamera(point,
                    QuickLineProjectionMode.Isometric));
        }

        [Fact]
        public void Orthographic_XZToScreenAndYIsDepth()
        {
            QuickLinePoint projected = IsometricProjection.ProjectOrthographic(
                new QuickLineSpatialPoint(3, 7, 5));
            Assert.Equal(3.0, projected.X, 6);
            Assert.Equal(5.0, projected.Y, 6);
            Assert.Equal(7.0, IsometricProjection.ClosenessToCamera(
                new QuickLineSpatialPoint(3, 7, 5),
                QuickLineProjectionMode.Orthographic), 6);
        }

        private static double AngleOf(QuickLinePoint point)
        {
            double degrees = Math.Atan2(point.Y, point.X) * 180.0 / Math.PI;
            if (degrees < 0) degrees += 360.0;
            return degrees;
        }
    }
}
