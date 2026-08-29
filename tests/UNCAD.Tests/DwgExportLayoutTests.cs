using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using Xunit;

namespace UNCAD.Tests
{
    public class DwgExportLayoutTests
    {
        [Fact]
        public void Arrange_PlacesFramesInOneRowWith10000Gap()
        {
            var first = new DwgFrameLayoutItem("M1", "设备1",
                new FrameRectangle("first", 0, 100, 2000, 1100));
            var second = new DwgFrameLayoutItem("M1", "设备2",
                new FrameRectangle("second", 500, 300, 1500, 900));

            IReadOnlyList<DwgFramePlacement> result = DwgExportLayout.Arrange(
                new[] { second, first });

            Assert.Equal(2, result.Count);
            Assert.Same(first, result[0].Item);
            Assert.Same(second, result[1].Item);
            Assert.Equal(-100d, result[0].TranslationY);
            Assert.Equal(11500d, result[1].TranslationX);
        }

        [Fact]
        public void Arrange_AllowsTwoFramesWithSameDeviceName()
        {
            var items = new[]
            {
                new DwgFrameLayoutItem("M1", "同名设备",
                    new FrameRectangle("first", 0, 0, 1000, 500)),
                new DwgFrameLayoutItem("M1", "同名设备",
                    new FrameRectangle("second", 2000, 0, 3000, 500))
            };

            IReadOnlyList<DwgFramePlacement> result = DwgExportLayout.Arrange(items);

            Assert.Equal(2, result.Count);
            Assert.Equal("同名设备", result[0].Item.DeviceName);
            Assert.Equal("同名设备", result[1].Item.DeviceName);
            Assert.Equal(11000d, result[1].TranslationX + items[1].Boundary.MinX);
        }

        [Fact]
        public void Arrange_Supports31FramesThroughApColumnEquivalentWidth()
        {
            var items = Enumerable.Range(1, 31).Select(index =>
                new DwgFrameLayoutItem("M1", "回路" + index,
                    new FrameRectangle(index.ToString(), index * 1000, 0,
                        index * 1000 + 500, 500))).ToList();

            IReadOnlyList<DwgFramePlacement> result = DwgExportLayout.Arrange(items);

            Assert.Equal(31, result.Count);
            Assert.Equal(30 * 10500d, result[30].TranslationX + items[30].Boundary.MinX);
            Assert.Equal("回路31", result[30].Item.DeviceName);
        }
    }
}
