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
        [InlineData("MQBAN03 墙插", true)]   // OR：任一词命中即保留
        [InlineData("不存在", false)]
        public void MatchesCircuit_UsesOrSemanticsOnMachineAndCircuit(
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
            Assert.True(XLayoutSelectionFilter.MatchesAny("anything", null));
        }

        [Fact]
        public void PastedMachineIdList_KeepsAnyListedMachine()
        {
            // 现场用法：把一串机台ID 粘进筛选框（空格或逗号分隔），只留下这些机台。
            string pasted = "MCWCE01 MDHCV02,MYDFI15，MYOMI02";
            var terms = XLayoutSelectionFilter.SplitTerms(pasted);
            Assert.Equal(4, terms.Count);
            Assert.True(XLayoutSelectionFilter.MatchesCircuit(
                "MYDFI15", "CPU 插座1", terms));
            Assert.True(XLayoutSelectionFilter.MatchesCircuit(
                "MYOMI02", "PM", terms));
            Assert.False(XLayoutSelectionFilter.MatchesCircuit(
                "MQBAN03", "电脑插座1", terms));
            // 任一命中即可：列表里有一个不存在的词也不影响其它词的匹配。
            string withNoise = pasted + " I12 NOT-EXIST";
            Assert.True(XLayoutSelectionFilter.MatchesCircuit("MYDFI15", "PM",
                XLayoutSelectionFilter.SplitTerms(withNoise)));
        }
    }
}
