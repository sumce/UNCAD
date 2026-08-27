using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    public class ConduitLabelFormatterTests
    {
        [Theory]
        [InlineData("20", "⌀20线管 2000mm")]
        [InlineData("25", "⌀25线管 2000mm")]
        [InlineData("32", "⌀32线管 2000mm")]
        public void Build_AlwaysUsesDefaultTwoMeterLength(string diameter, string expected)
        {
            Assert.Equal(2000.0, ConduitLabelFormatter.DefaultLengthMm);
            Assert.Equal(expected, ConduitLabelFormatter.Build(diameter));
        }
    }
}
