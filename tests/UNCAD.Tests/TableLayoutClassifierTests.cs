using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class TableLayoutClassifierTests
    {
        [Theory]
        [InlineData("NO.", "项目名称", "项次编码")]
        [InlineData("序号", "设备名称", "编码")]
        public void HeaderLabels_AreRecognized(string c0, string c1, string c5)
        {
            Assert.True(TableLayoutClassifier.IsHeaderLike(c0, c1, c5));
        }

        [Theory]
        [InlineData("NO.1")]
        [InlineData("NO1")]
        [InlineData("1")]
        [InlineData("12")]
        public void NumberedRows_AreData_NotHeaders(string value)
        {
            Assert.True(TableLayoutClassifier.IsNumberedDataRow(value));
            Assert.False(TableLayoutClassifier.IsHeaderLike(value, "配电设备", "1.25"));
        }

        [Fact]
        public void DataContainingHeaderWords_IsNotMatchedBySubstring()
        {
            Assert.False(TableLayoutClassifier.IsHeaderLike("NO.2", "设备名称变更", "1.2"));
        }

        [Fact]
        public void DrawingInfoHeader_RecognizesCurrentAndSplitSequences()
        {
            Assert.True(TableLayoutClassifier.IsCurrentDrawingInfoHeader(
                "专业", "楼层", "制图", "审核", "日期", "版本"));
            Assert.True(TableLayoutClassifier.IsDrawingInfoHeader(
                "专业", "设备楼层", "上游楼层", "制图", "审核", "日期", "版本"));
            Assert.True(TableLayoutClassifier.IsDrawingInfoHeader(
                "专业", "楼层", "制图", "审核", "日期", "版本"));
            Assert.True(TableLayoutClassifier.IsDrawingInfoHeader(
                @"{\fSimSun|b0|i0|c134|p2;专业}",
                @"{\fSimSun|b0|i0|c134|p2;楼层}",
                @"{\fSimSun|b0|i0|c134|p2;制图}",
                @"{\fSimSun|b0|i0|c134|p2;审核}",
                @"{\fSimSun|b0|i0|c134|p2;日期}",
                @"{\fSimSun|b0|i0|c134|p2;版本}"));
            Assert.False(TableLayoutClassifier.IsDrawingInfoHeader(
                "专业", "楼层", "审核", "制图", "日期", "版本"));
            Assert.False(TableLayoutClassifier.IsCurrentDrawingInfoHeader(
                "专业", "设备楼层", "上游楼层", "制图", "审核", "日期", "版本"));
        }
    }
}
