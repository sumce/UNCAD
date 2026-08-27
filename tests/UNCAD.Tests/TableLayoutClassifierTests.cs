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
    }
}
