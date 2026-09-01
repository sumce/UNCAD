using System;
using UNCAD.Core.Geometry;
using Xunit;

namespace UNCAD.Tests
{
    public class SemicircleGeometryTests
    {
        [Theory]
        [InlineData(-5, 0, 5, 0)]
        [InlineData(5, 0, -5, 0)]
        [InlineData(0, -5, 0, 5)]
        [InlineData(-3.5355339, -3.5355339, 3.5355339, 3.5355339)]
        public void Angles_AlwaysProducesNormalizedHalfCircle(
            double x1, double y1, double x2, double y2)
        {
            SemicircleGeometry.Angles(0, 0, x1, y1, x2, y2, 5,
                out double start, out double end);

            Assert.InRange(start, 0, Math.PI * 2.0);
            Assert.Equal(Math.PI, end - start, 8);
        }

        [Theory]
        [InlineData(1000, 300, 500, true)]
        [InlineData(300, 300, 150, false)]
        [InlineData(1000, 300, 150, false)]
        [InlineData(1000, 300, 850, false)]
        public void CanSplit_RejectsZeroLengthResidualSegments(
            double total, double diameter, double centerDistance, bool expected)
        {
            Assert.Equal(expected,
                SemicircleGeometry.CanSplit(total, diameter, centerDistance));
        }

        [Fact]
        public void Angles_HorizontalLineBulgesUpwardInEitherDirection()
        {
            AssertBulgesUp(-5, 5);
            AssertBulgesUp(5, -5);
        }

        [Theory]
        [InlineData(-5, 5, -1)]
        [InlineData(5, -5, 1)]
        public void Bulge_PreservesUpwardArcInPolylineTravelDirection(
            double firstX, double secondX, double expected)
        {
            Assert.Equal(expected, SemicircleGeometry.Bulge(
                0, 0, firstX, 0, secondX, 0, 5));
        }

        private static void AssertBulgesUp(double firstX, double secondX)
        {
            SemicircleGeometry.Angles(0, 0, firstX, 0, secondX, 0, 5,
                out double start, out double end);
            Assert.True(Math.Sin((start + end) / 2.0) > 0.999999);
        }
    }
}
