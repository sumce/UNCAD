using UNCAD.Core.Geometry;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class UpstreamConnectionGeometryTests
    {
        [Fact]
        public void TryCreatePath_UsesLeftEndWhenUpstreamIsLeft()
        {
            Assert.True(UpstreamConnectionGeometry.TryCreatePath(100, 200, 300, 0,
                50, 20, 0, out ConnectionPath path, horizontalGap: 10,
                horizontalLength: 0));

            Assert.True(path.UsesLeftEnd);
            Assert.Equal(100, path.Horizontal.StartX);
            Assert.Equal(300, path.Horizontal.EndX);
            Assert.Equal(190, path.Horizontal.StartY);
            Assert.Equal(path.Horizontal.StartX, path.Diagonal.StartX);
            Assert.Equal(50, path.Diagonal.EndX);
        }

        [Fact]
        public void TryCreatePath_UsesRightEndWhenUpstreamIsRight()
        {
            Assert.True(UpstreamConnectionGeometry.TryCreatePath(100, 200, 300, 0,
                450, 20, 0, out ConnectionPath path, horizontalGap: 10,
                horizontalLength: 0));

            Assert.False(path.UsesLeftEnd);
            Assert.Equal(300, path.Diagonal.StartX);
            Assert.Equal(450, path.Diagonal.EndX);
        }

        [Fact]
        public void TryCreatePath_UsesDefaultLengthForPointLikeInfoExtents()
        {
            Assert.True(UpstreamConnectionGeometry.TryCreatePath(100, 200, 100, 0,
                450, 20, 0, out ConnectionPath path, horizontalGap: 10,
                horizontalLength: UpstreamConnectionGeometry.DefaultHorizontalLength));

            Assert.Equal(UpstreamConnectionGeometry.DefaultHorizontalLength,
                path.Horizontal.EndX - path.Horizontal.StartX);
        }

        [Fact]
        public void TryCreate_UsesTheTwoBlockInsertionAnchors()
        {
            Assert.True(UpstreamConnectionGeometry.TryCreate(10, 20, 0,
                40, 5, 0, out ConnectionSegment segment));
            Assert.Equal(10, segment.StartX);
            Assert.Equal(20, segment.StartY);
            Assert.Equal(40, segment.EndX);
            Assert.Equal(5, segment.EndY);
        }

        [Fact]
        public void TryCreate_RejectsCoincidentAnchors()
        {
            Assert.False(UpstreamConnectionGeometry.TryCreate(10, 20, 0,
                10, 20, 0, out _));
        }

        [Fact]
        public void SameSegment_AcceptsReversedEndpoints()
        {
            var first = new ConnectionSegment(0, 0, 0, 10, 5, 0);
            var reversed = new ConnectionSegment(10, 5, 0, 0, 0, 0);

            Assert.True(UpstreamConnectionGeometry.SameSegment(first, reversed));
        }
    }
}
