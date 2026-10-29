using UNCAD.Core.Dwg;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>XLAYOUT 筛选表达式：空格/逗号等分隔、AND 语义、大小写不敏感。</summary>
    public sealed class XLayoutSelectionFilterTests
    {
        [Theory]
        [InlineData("MQBAN03", 1)]
        [InlineData("MQBAN03 电脑", 2)]
        [InlineData("a,b，c;d、e", 5)]
        [InlineData("  ", 0)]
        [InlineData(null, 0)]
        public void SplitTerms_SplitsOnCommonSeparators(string text, int expected)
            => Assert.Equal(expected, XLayoutSelectionFilter.SplitTerms(text).Count);

        [Theory]
        [InlineData("", true)]
        [InlineData("MQBAN03", true)]
        [InlineData("mqban03", true)]
        [InlineData("电脑", true)]
        [InlineData("MQBAN03 电脑", true)]
        [InlineData("MQBAN03 墙插", false)]
        [InlineData("不存在", false)]
        public void MatchesCircuit_UsesAndSemanticsOnMachineAndCircuit(
            string filter, bool expected)
        {
            Assert.Equal(expected, XLayoutSelectionFilter.MatchesCircuit(
                "MQBAN03", "电脑插座1", filter));
        }

        [Fact]
        public void MatchesMachine_IgnoresCircuitNames()
        {
            Assert.True(XLayoutSelectionFilter.MatchesMachine("MQBAN03",
                XLayoutSelectionFilter.SplitTerms("MQBAN03")));
            Assert.False(XLayoutSelectionFilter.MatchesMachine("MQBAN03",
                XLayoutSelectionFilter.SplitTerms("电脑")));
        }

        [Fact]
        public void MatchesCircuit_EmptyTermsMatchEverything()
        {
            Assert.True(XLayoutSelectionFilter.MatchesCircuit("M1", "A",
                XLayoutSelectionFilter.SplitTerms("")));
            Assert.True(XLayoutSelectionFilter.MatchesAll("anything", null));
        }
    }
}
