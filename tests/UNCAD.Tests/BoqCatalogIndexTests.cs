using System.Collections.Generic;
using System.IO;
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
                Item("3.3", "线管", "38mm", "线管"),
                Item("3.7", "软管", "38mm", "软管"),
                Item("3.32", "线管", "32mm", "线管")
            });

            Assert.Equal("3.32", index.FindRigidConduit("Φ32")?.Code);
            Assert.Null(index.FindFlexibleConduit("32mm"));
            Assert.Equal("3.3", index.FindRigidConduit("DN38")?.Code);
            Assert.Equal("3.7", index.FindFlexibleConduit("38")?.Code);
        }

        [Fact]
        public void Alias1_Migrates32mmInputToTheDatabase38mmMaterial()
        {
            ListItem target = Item("3.3", "镀锌穿线管", "38mm", "线管");
            target.Alias1 = "32mm";
            var index = new BoqCatalogIndex(new[]
            {
                target,
                Item("3.7", "包塑金属软管", "38mm", "软管")
            });

            ListItem migrated = index.FindFlexibleConduit("32mm");

            Assert.Same(target, migrated);
            Assert.Equal("3.3", migrated.Code);
            Assert.Equal("38mm", migrated.Alias);
            Assert.Equal("32mm", migrated.Alias1);
        }

        [Fact]
        public void Alias1_DuplicateMigrationTargetsAreRejected()
        {
            ListItem first = Item("3.3", "镀锌穿线管", "38mm", "线管");
            first.Alias1 = "32mm";
            ListItem second = Item("3.7", "包塑金属软管", "38mm", "软管");
            second.Alias1 = "DN32";

            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                new BoqCatalogIndex(new[] { first, second }));

            Assert.Contains("别名1重复", error.Message);
            Assert.Contains("3.3", error.Message);
            Assert.Contains("3.7", error.Message);
        }

        [Fact]
        public void AliasIndex_NormalizesValuesAndRejectsAmbiguousCategoryDuplicates()
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                new BoqCatalogIndex(new List<ListItem>
                {
                    Item("2.1", "桥架", "200 x 100", "桥架"),
                    Item("2.9", "重复桥架", "200*100", "桥架")
                }));

            Assert.Contains("桥架", error.Message);
            Assert.Contains("2.1", error.Message);
            Assert.Contains("2.9", error.Message);
        }

        [Fact]
        public void AliasMatching_UsesExplicitCategoryAndNeverInfersBlankClass()
        {
            var index = new BoqCatalogIndex(new[]
            {
                Item("1.1", "复杂电缆名称", "3*2.5", "电缆"),
                Item("1.2", "电缆", "4*2.5", "")
            });

            Assert.Equal("1.1", index.FindCable("ZB-YJVR-3x2.5")?.Code);
            Assert.Null(index.FindCable("ZB-YJVR-4*2.5"));
            Assert.Single(index.Cables);
        }

        [Fact]
        public void OutletPanelMatching_UsesApprovedCodeMappingOnly()
        {
            var index = new BoqCatalogIndex(new[]
            {
                Item("4.11", "插座盘", "100A+20A*40", "插座盘"),
                Item("4.12", "插座盘", "80A+16A*30", "插座盘"),
                Item("4.14", "插座盘", "50A+16A*10", "插座盘")
            });

            Assert.Equal("4.11", index.FindOutletPanel(20)?.Code);
            Assert.Equal("4.12", index.FindOutletPanel(16)?.Code);
            Assert.Null(index.FindOutletPanel(10));
            Assert.Equal("4.11", BoqCatalogIndex.OutletPanelCode(20));
            Assert.Equal("", BoqCatalogIndex.OutletPanelCode(10));
        }

        [Fact]
        public void CableFeatureMatching_UsesProjectFeatureAndReturnsCanonicalAlias()
        {
            ListItem item = new ListItem
            {
                Category = "电缆",
                Code = "1.1",
                Name = "多芯电缆 XLPE",
                Feature = "1.名称:0.6/1kV-YJVR-2.5mm2*3C 多芯电缆\n2.配线形式:穿管或桥架敷设",
                Alias = "3*2.5"
            };
            var index = new BoqCatalogIndex(new[] { item });

            ListItem matched = index.FindCableByFeature(
                "1.名称:o.6/1kV-YJVR-2.5mm²*3C 多芯电缆\\P2.配线形式:穿管或桥架敷设");

            Assert.Same(item, matched);
            Assert.Equal("3*2.5", matched.Alias);
        }

        private static ListItem Item(string code, string name, string alias, string category)
            => new ListItem { Category = category, Code = code, Name = name, Alias = alias };
    }
}
