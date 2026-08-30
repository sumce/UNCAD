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
