using UNCAD.Core.Geometry;
using UNCAD.Core.Dwg;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XLayoutDiagnosticTests
    {
        [Fact]
        public void DescribeFrameIdentityFailureIncludesLocationAndHandle()
        {
            var boundary = new FrameRectangle("208AE", 100, 200, 500, 800);
            string message = XLayoutDiagnostics.DescribeFrameIdentityFailure(
                "208AE", boundary, 3, 12, 7, "框选内容中未读取到设备名称");

            Assert.Contains("第 3/12 个图框", message);
            Assert.Contains("句柄 208AE", message);
            Assert.Contains("中心(300,500)", message);
            Assert.Contains("范围X[100..500] Y[200..800]", message);
            Assert.Contains("包含实体 7 个", message);
            Assert.Contains("未读取到设备名称", message);
        }
    }
}
