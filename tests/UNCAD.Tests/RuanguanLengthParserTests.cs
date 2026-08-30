using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    public class RuanguanLengthParserTests
    {
        [Theory]
        [InlineData("2000mm", "2")]
        [InlineData("软管: 2000mm", "2")]
        [InlineData("软管 2000mm", "2")]
        [InlineData("软管长度=2,500mm", "2.5")]
        public void ParseMeters_AcceptsSupportedLengthForms(string value, string expected)
        {
            Assert.Equal(expected, RuanguanLengthParser.ParseMeters(value));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("直径 32mm")]
        [InlineData("软管型号 DN32")]
        [InlineData("0mm")]
        [InlineData("Infinitymm")]
        public void ParseMeters_RejectsMissingOrNonLengthValues(string value)
        {
            Assert.Equal("", RuanguanLengthParser.ParseMeters(value));
        }
    }
}
