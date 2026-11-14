using System;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// Projects the reconstructed orthogonal X/Y/Z route back onto the sketch
    /// plane.  The axis screen directions mirror
    /// QuickLineIsometricSceneBuilder's isometric candidates (X=30°, Y=150°,
    /// Z=90°) and its orthographic candidates (X=0°, Z=90°), so a re-projected
    /// scene lands exactly on the axes the scanner classifies by.
    ///
    /// Every axis projects with unit length: the sketch convention draws true
    /// millimetre distances along each axis, so a segment's projected length
    /// equals its display distance.
    /// </summary>
    public static class IsometricProjection
    {
        private const double Sqrt3Over2 = 0.86602540378443864676;

        /// <summary>
        /// Projects an isometric scene point to the sketch plane:
        /// ((x−y)·√3/2, (x+y)/2+z).  +X maps to 30°, +Y to 150°, +Z to 90°.
        /// </summary>
        public static QuickLinePoint ProjectIsometric(QuickLineSpatialPoint point)
            => new QuickLinePoint(
                (point.X - point.Y) * Sqrt3Over2,
                (point.X + point.Y) / 2.0 + point.Z);

        /// <summary>
        /// Projects an orthographic scene point to the sketch plane: +X maps
        /// to 0° and +Z to 90°.  +Y is the view axis and disappears.
        /// </summary>
        public static QuickLinePoint ProjectOrthographic(QuickLineSpatialPoint point)
            => new QuickLinePoint(point.X, point.Z);

        public static QuickLinePoint Project(QuickLineSpatialPoint point,
            QuickLineProjectionMode mode)
        {
            if (mode == QuickLineProjectionMode.Orthographic)
                return ProjectOrthographic(point);
            return ProjectIsometric(point);
        }

        /// <summary>
        /// Closeness to the camera along the projection's view axis; a larger
        /// value is nearer.  The isometric projection's null direction is
        /// (1,1,−1); with the camera above the scene the toward-viewer side is
        /// (−1,−1,1), giving z−x−y — an elevated run therefore hops over a
        /// floor run at a crossing.  Orthographic views look along +Y.
        /// </summary>
        public static double ClosenessToCamera(QuickLineSpatialPoint point,
            QuickLineProjectionMode mode)
        {
            if (mode == QuickLineProjectionMode.Orthographic)
                return point.Y;
            return point.Z - point.X - point.Y;
        }
    }
}
