using UNCAD.Core.Geometry;
using Xunit;

namespace UNCAD.Tests
{
    public class FrameRectangleTests
    {
        [Fact]
        public void Contains_NormalizesCornersAndIncludesBoundaryTolerance()
        {
            var frame = new FrameRectangle("A", 100, 200, 0, 0);

            Assert.True(frame.Contains(0, 0));
            Assert.True(frame.Contains(100.00000005, 100));
            Assert.False(frame.Contains(100.01, 100));
        }

        [Fact]
        public void CompareReadingOrder_SortsTopToBottomThenLeftToRight()
        {
            var lower = new FrameRectangle("lower", 0, 0, 10, 10);
            var upperRight = new FrameRectangle("right", 20, 20, 30, 30);
            var upperLeft = new FrameRectangle("left", 0, 20, 10, 30);
            var frames = new[] { lower, upperRight, upperLeft };

            System.Array.Sort(frames, FrameRectangle.CompareReadingOrder);

            Assert.Equal("left", frames[0].Key);
            Assert.Equal("right", frames[1].Key);
            Assert.Equal("lower", frames[2].Key);
        }

        [Fact]
        public void Contains_ExposesOverlappingOwnershipToCaller()
        {
            var left = new FrameRectangle("left", 0, 0, 10, 10);
            var right = new FrameRectangle("right", 5, 0, 15, 10);

            // The collector treats two positive claims as a hard preflight conflict.
            Assert.True(left.Contains(7, 5));
            Assert.True(right.Contains(7, 5));
        }
    }
}
