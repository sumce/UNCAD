using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class FlexibleConduitCableMapTests
    {
        [Theory]
        [InlineData("3*2.5", "20")]
        [InlineData("4*2.5", "20")]
        [InlineData("5*2.5", "20")]
        [InlineData("3*4", "20")]
        [InlineData("4*4", "20")]
        [InlineData("5*4", "20")]
        [InlineData("3*10", "25")]
        [InlineData("4*10", "25")]
        [InlineData("5*10", "25")]
        [InlineData("3*16", "25")]
        [InlineData("4*16", "25")]
        [InlineData("5*16", "25")]
        [InlineData("2*25+1*16", "38")]
        [InlineData("3*25+1*16", "38")]
        [InlineData("4*25+1*16", "38")]
        [InlineData("2*35+1*16", "38")]
        [InlineData("3*35+1*16", "38")]
        [InlineData("4*35+1*16", "38")]
        [InlineData("3*50+1*25", "51")]
        [InlineData("4*50+1*25", "51")]
        [InlineData("3*70+1*35", "51")]
        [InlineData("4*70+1*35", "51")]
        [InlineData("3*(2*70)+1*70", "75")]
        [InlineData("4*(2*70)+1*70", "75")]
        [InlineData("3*95+1*50", "75")]
        [InlineData("4*95+1*50", "75")]
        [InlineData("3*(2*95)+1*50", "75")]
        [InlineData("3*120+1*70", "75")]
        [InlineData("3*(2*120)+1*120", "100")]
        public void TryGetDiameter_ContainsApprovedCableTable(string cable, string expected)
        {
            Assert.True(FlexibleConduitCableMap.TryGetDiameter(cable, out string actual));
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryGetDiameter_NormalizesFullModelWhitespaceAndMultiplicationSigns()
        {
            Assert.Equal(29, FlexibleConduitCableMap.Count);
            Assert.True(FlexibleConduitCableMap.TryGetDiameter(" ZB-YJVR - 3 × 2.5 ", out string diameter));
            Assert.Equal("20", diameter);
        }

        [Fact]
        public void ApplyTo_UsesCableAndIgnoresExistingWorkbookDiameter()
        {
            var machine = new MachineRow { Cable = "ZB-YJVR-4*16", Dia = "999" };

            Assert.True(FlexibleConduitCableMap.ApplyTo(machine));
            Assert.Equal("25", machine.Dia);
        }

        [Fact]
        public void ApplyTo_ClearsStaleDiameterWhenCableIsUnknown()
        {
            var machine = new MachineRow { Cable = "UNKNOWN-CABLE", Dia = "51" };

            Assert.False(FlexibleConduitCableMap.ApplyTo(machine));
            Assert.Equal("", machine.Dia);
        }
    }
}
