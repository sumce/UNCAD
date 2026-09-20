using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using Xunit;

namespace UNCAD.Tests
{
    public class XLayoutLayoutTests
    {
        [Fact]
        public void Arrange_IdentifiesFirstPlacedFrameByFinalLeftEdge()
        {
            // The second source frame starts much farther right. Its translation is therefore
            // numerically smaller, even though its final placed column is the second one.
            var firstSource = new XLayoutFrameItem("M1", "A",
                new FrameRectangle("first", 100000, 0, 101000, 500), "first");
            var secondSource = new XLayoutFrameItem("M1", "B",
                new FrameRectangle("second", 500000, 0, 501000, 500), "second");

            IReadOnlyList<XLayoutPlacement> result = XLayoutLayout.Arrange(
                new[] { firstSource, secondSource });
            XLayoutPlacement firstPlaced = result.OrderBy(placement =>
                placement.TranslationX + placement.Item.Boundary.MinX).First();

            Assert.Same(firstSource, firstPlaced.Item);
            Assert.NotSame(firstPlaced.Item, result.OrderBy(
                placement => placement.TranslationX).First().Item);
        }

        [Fact]
        public void Arrange_GroupsSameMachineIntoOneRow()
        {
            var items = new[]
            {
                new XLayoutFrameItem("M1", "C2", new FrameRectangle("2", 500, 100, 1500, 900), "2"),
                new XLayoutFrameItem("M2", "C3", new FrameRectangle("3", 0, 0, 800, 400), "3"),
                new XLayoutFrameItem("M1", "C1", new FrameRectangle("1", 0, 0, 2000, 1000), "1")
            };

            IReadOnlyList<XLayoutPlacement> result = XLayoutLayout.Arrange(items);

            Assert.Equal(3, result.Count);
            Assert.Equal(2, result.Count(item => item.RowIndex == 0));
            Assert.Single(result, item => item.RowIndex == 1);
            Assert.All(result.Where(item => item.RowIndex == 0), item =>
                Assert.Equal(0d, item.TranslationY + item.Item.Boundary.MaxY));
            XLayoutPlacement secondRow = Assert.Single(result, item => item.RowIndex == 1);
            Assert.Equal(-11000d, secondRow.TranslationY + secondRow.Item.Boundary.MaxY);
        }

        [Fact]
        public void Arrange_GroupsMachineIdsIgnoringInternalWhitespace()
        {
            IReadOnlyList<XLayoutPlacement> result = XLayoutLayout.Arrange(new[]
            {
                new XLayoutFrameItem("M Q-01", "A", new FrameRectangle("a", 0, 0, 100, 100), "a"),
                new XLayoutFrameItem("MQ-01", "B", new FrameRectangle("b", 0, 0, 100, 100), "b")
            });

            Assert.Equal(2, result.Count);
            Assert.All(result, item => Assert.Equal(0, item.RowIndex));
        }

        [Fact]
        public void Arrange_Uses10000HorizontalGapAndExactVerticalGapWithDifferentHeights()
        {
            var items = new[]
            {
                new XLayoutFrameItem("M1", "A", new FrameRectangle("a", 0, 0, 1000, 2000), "a"),
                new XLayoutFrameItem("M1", "B", new FrameRectangle("b", 0, 0, 500, 500), "b"),
                new XLayoutFrameItem("M2", "C", new FrameRectangle("c", 0, 0, 500, 500), "c")
            };

            IReadOnlyList<XLayoutPlacement> result = XLayoutLayout.Arrange(items);
            XLayoutPlacement first = result[0];
            XLayoutPlacement second = result[1];
            XLayoutPlacement third = result[2];

            Assert.Equal(0d, first.TranslationX + first.Item.Boundary.MinX);
            Assert.Equal(1000d + 10000d, second.TranslationX + second.Item.Boundary.MinX);
            double firstBottom = first.TranslationY + first.Item.Boundary.MinY;
            double secondRowTop = third.TranslationY + third.Item.Boundary.MaxY;
            Assert.Equal(10000d, firstBottom - secondRowTop);
        }
    }
}
