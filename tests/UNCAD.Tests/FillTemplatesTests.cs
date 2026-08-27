using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>清单表填充模板测试。</summary>
    public class FillTemplatesTests
    {
        [Theory]
        [InlineData("N208 3P4W 3P50A", "3P50A配电")]
        [InlineData(" 3P4W 3P200A", "3P200A配电")]
        [InlineData("U220 1P3W 1P20A", "1P20A配电")]
        [InlineData("U480 3P4W 3P630A", "3P630A配电")]
        [InlineData("-", "THC")] // 无匹配 → 回退回路名称
        [InlineData(null, "THC")]
        public void BreakerName_ExtractsSpec(string detail, string expected)
        {
            Assert.Equal(expected, FillTemplates.BreakerName(detail, "THC"));
        }

        [Fact]
        public void CableTemplate_ContainsBackslashP_LineBreaks()
        {
            string s = string.Format(FillTemplates.CableDesc, "ZB-YJV-3*70+1*35");
            Assert.StartsWith("1.名称:ZB-YJV-3*70+1*35mm²单芯电缆", s);
            Assert.Contains("\\P2.配线形式", s);
            Assert.Contains("\\P3.说明", s);
        }

        [Fact]
        public void CableQuantity_UsesAggregatedMeters_WithoutUnit()
        {
            var stat = StatCalculator.Calculate(new[]
            {
                "2000mm", "3000mm", "4400mm"
            }, 250.0);

            Assert.Equal("9.4", TableFillFormatter.CableQuantity(stat));
        }

        [Fact]
        public void CableQuantity_IsEmpty_WhenNoLengthSelected()
        {
            Assert.Equal("", TableFillFormatter.CableQuantity(new CableStatResult()));
        }

        [Fact]
        public void TableDimensions_UseRequiredTextAndGeneratedRowHeights()
        {
            Assert.Equal(500.0, TableFillFormatter.DefaultTextHeight);
            Assert.Equal(5847.8848, TableFillFormatter.GeneratedRowHeight, 4);
        }

        [Fact]
        public void FlexibleConduitQuantity_DefaultsToTwoMeters()
        {
            Assert.Equal(2000.0, TableFillFormatter.DefaultFlexibleConduitMm);
            Assert.Equal("2", TableFillFormatter.FlexibleConduitQuantity());
        }

        [Fact]
        public void ConduitTemplate_UsesDiameter()
        {
            string s = string.Format(FillTemplates.ConduitDesc, "51");
            Assert.StartsWith("1.名称:51mm(1-1/2\")包塑金属软管", s);
            Assert.Contains("\\P2.材质", s);
        }
    }
}
