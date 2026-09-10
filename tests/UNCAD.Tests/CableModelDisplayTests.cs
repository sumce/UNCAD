using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 图框 CABLE_INFO 显示的型号命名契约：含 "+" → ZB-YJV-，不含 → ZB-YJVR-，
    /// 接地线不补前缀，且对已带前缀的输入幂等。
    /// </summary>
    public class CableModelDisplayTests
    {
        [Theory]
        [InlineData("3*35+1*16", "ZB-YJV-3*35+1*16")]     // 含 + 的多芯组合
        [InlineData("3*70+1*35", "ZB-YJV-3*70+1*35")]
        [InlineData("3*2.5", "ZB-YJVR-3*2.5")]            // 不含 +
        [InlineData("5*6", "ZB-YJVR-5*6")]
        [InlineData("3x2.5", "ZB-YJVR-3x2.5")]            // 保留原始分隔符写法
        [InlineData(" 3*2.5 ", "ZB-YJVR-3*2.5")]          // 首尾空格容忍
        public void WithTypePrefix_DerivesPrefixFromPlusSign(string model, string expected)
        {
            Assert.Equal(expected, CableModelDisplay.WithTypePrefix(model));
        }

        [Theory]
        [InlineData("ZB-YJV-3*70+1*35")]                  // 已带正确前缀
        [InlineData("ZB-YJVR-3*2.5")]
        public void WithTypePrefix_IsIdempotent(string model)
        {
            // 重复执行 U1F/U1U 不得累积前缀。
            Assert.Equal(model, CableModelDisplay.WithTypePrefix(model));
            Assert.Equal(model, CableModelDisplay.WithTypePrefix(
                CableModelDisplay.WithTypePrefix(model)));
        }

        [Fact]
        public void WithTypePrefix_PrefixFollowsModelShapeNotTheExistingPrefix()
        {
            // 已有前缀与型号形状冲突时以形状为准：含 "+" 即为电源电缆。
            Assert.Equal("ZB-YJV-3*35+1*16",
                CableModelDisplay.WithTypePrefix("ZB-YJVR-3*35+1*16"));
        }

        [Theory]
        [InlineData("1*16")]                              // 设备接地专用接地线
        [InlineData("ZB-YJVR-1*16")]
        [InlineData("1*16mm2")]
        public void WithTypePrefix_LeavesGroundingCableUnchanged(string model)
        {
            Assert.Equal(model, CableModelDisplay.WithTypePrefix(model));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void WithTypePrefix_ReturnsEmptyForNoModel(string model)
        {
            Assert.Equal("", CableModelDisplay.WithTypePrefix(model));
        }

        [Theory]
        [InlineData("3*35+1*16", "3*35+1*16")]            // 无前缀时原样返回
        [InlineData("ZB-YJV-3*35+1*16", "3*35+1*16")]
        [InlineData("ZB-YJVR-3*2.5", "3*2.5")]
        [InlineData("ZB-YJVR-1*16", "1*16")]
        public void StripTypePrefix_RemovesAtMostTwoLeadingSegments(string model, string expected)
        {
            Assert.Equal(expected, CableModelDisplay.StripTypePrefix(model));
        }
    }
}
