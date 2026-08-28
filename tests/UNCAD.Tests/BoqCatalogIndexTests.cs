using System.Collections.Generic;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    public class BoqCatalogIndexTests
    {
        [Fact]
        public void ExactConduitIndexes_SeparateRigidAndFlexibleWithoutSubstitution()
        {
            var index = new BoqCatalogIndex(new List<ListItem>
            {
                Item("3.3", "镀锌穿线管", "38mm"),
                Item("3.7", "包塑金属软管", "38mm"),
                Item("3.32", "镀锌穿线管", "32mm")
            });

            Assert.Equal("3.32", index.FindRigidConduit("Φ32")?.Code);
            Assert.Null(index.FindFlexibleConduit("32mm"));
            Assert.Equal("3.3", index.FindRigidConduit("DN38")?.Code);
            Assert.Equal("3.7", index.FindFlexibleConduit("38")?.Code);
        }

        [Fact]
        public void SpecIndex_NormalizesWhitespaceAndKeepsFirstDuplicateDeterministically()
        {
            var index = new BoqCatalogIndex(new List<ListItem>
            {
                Item("2.1", "桥架", "200 x 100"),
                Item("2.9", "重复桥架", "200*100"),
                Item("1.1", "电缆", "3*2.5")
            });

            Assert.Equal("2.1", index.FindBridge("200*100")?.Code);
            Assert.Equal("1.1", index.FindCable("ZB-YJVR-3x2.5")?.Code);
        }

        private static ListItem Item(string code, string name, string spec)
            => new ListItem { Code = code, Name = name, Spec = spec };
    }
}
