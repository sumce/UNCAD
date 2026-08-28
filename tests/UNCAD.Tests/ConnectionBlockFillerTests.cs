using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class ConnectionBlockFillerTests
    {
        [Fact]
        public void UpstreamInfo_WritesFrAndDetailOnTwoMTextLines()
        {
            var row = new MachineRow
            {
                Fr = "3U-UPS28D1-PP-03(1F 化学实验室)",
                Detail = "U220 1P3W 1P20A"
            };

            Assert.Equal("3U-UPS28D1-PP-03(1F 化学实验室)\\PU220 1P3W 1P20A",
                ConnectionBlockFiller.UpstreamInfo(row));
        }

        [Fact]
        public void UpstreamInfo_DoesNotAddBlankControlLine()
        {
            Assert.Equal("FR-A", ConnectionBlockFiller.UpstreamInfo(
                new MachineRow { Fr = "FR-A" }));
            Assert.Equal("DETAIL-A", ConnectionBlockFiller.UpstreamInfo(
                new MachineRow { Detail = "DETAIL-A" }));
        }

        [Fact]
        public void AxisValues_AreTrimmedAndMappedToCorrectTags()
        {
            var row = new MachineRow
            {
                DownstreamAxis = " 2/T ",
                UpstreamAxis = " 化学实验室 "
            };

            Assert.Equal("2/T", ConnectionBlockFiller.DownstreamAxis(row));
            Assert.Equal("化学实验室", ConnectionBlockFiller.UpstreamAxis(row));
            Assert.Equal("DS", ConnectionBlockFiller.TagDownstreamAxis);
            Assert.Equal("US", ConnectionBlockFiller.TagUpstreamAxis);
        }
    }
}
