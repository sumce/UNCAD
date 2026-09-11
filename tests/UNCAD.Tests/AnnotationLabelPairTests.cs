using System.Collections.Generic;
using System.IO;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1Q/U1C 两行标注（第一行固定清单 1.名称，第二行长度）的编解码契约。
    /// 关键不变量：只在同一个 MTEXT 内、且第一行确实是清单型号、第二行确实是
    /// 纯长度时才配对；孤立的 2000mm 必须仍然是电缆。
    /// </summary>
    public class AnnotationLabelPairTests
    {
        private static ListItem Item(string code, string category, string feature,
            string alias)
        {
            return new ListItem
            {
                Code = code,
                Category = category,
                Name = "项目" + code,
                Feature = feature,
                Unit = "m",
                Alias = alias
            };
        }

        private static AnnotationModelIndex Index() => AnnotationModelIndex.Build(new[]
        {
            Item("2.6", "桥架", "1.名称:梯形桥架200Wx100H\n2.材质:铝合金",
                "200*100"),
            Item("2.4", "桥架", "1.名称:梯形桥架400Wx100H\n2.材质:铝合金",
                "400*100"),
            Item("3.1", "线管", "1.名称:镀锌穿线管EMT PIPE 20mm(3/4\")\n2.说明:含防",
                "20mm"),
            Item("3.3", "线管", "1.名称:镀锌穿线管EMT PIPE 38mm(1-1/2\")\n2.说明:含防",
                "38mm"),
            Item("3.5", "软管", "1.名称:20mm(3/4\") 包塑金属软管(波纹管)", "20mm")
        });

        [Fact]
        public void Build_WritesModelThenLengthOnSeparateLines()
        {
            string label = AnnotationLabelPair.Build("梯形桥架200Wx100H", "2500mm");

            Assert.Equal("梯形桥架200Wx100H\\P2500mm", label);
            Assert.Equal(new List<string> { "梯形桥架200Wx100H", "2500mm" },
                TextParser.SplitMTextLines(label));
        }

        [Theory]
        [InlineData("桥架200*100 2500mm")]
        [InlineData("桥架200*100 10格")]
        public void UpgradeBridgeLabel_UsesEmbeddedBoqModel(string legacy)
        {
            Assert.True(AnnotationLabelPair.TryUpgradeBridgeLabel(
                legacy, 250.0, out string upgraded));
            Assert.Equal("梯形桥架200Wx100H\\P2500mm", upgraded);
        }

        [Fact]
        public void UpgradeBridgeLabel_LeavesUnreadableOrAlreadyUpgradedTextAlone()
        {
            Assert.False(AnnotationLabelPair.TryUpgradeBridgeLabel(
                "桥架200*100 12.5格", 250.0, out _));
            Assert.False(AnnotationLabelPair.TryUpgradeBridgeLabel(
                "梯形桥架200Wx100H\\P2500mm", 250.0, out _));
        }

        [Fact]
        public void TwoLineLabelsUseCompactLineSpacing()
        {
            Assert.Equal(0.65, EntityFactory.LabelLineSpacingFactor, 2);
        }

        [Fact]
        public void CollapseLines_RestoresBridgeSingleLineForm()
        {
            List<string> collapsed = AnnotationLabelPair.CollapseLines(
                new List<string> { "梯形桥架200Wx100H", "2500mm" }, Index());

            Assert.Equal(new List<string> { "桥架200*100 2500mm" }, collapsed);
        }

        [Fact]
        public void CollapseLines_RestoresConduitSingleLineForm()
        {
            List<string> collapsed = AnnotationLabelPair.CollapseLines(
                new List<string> { "镀锌穿线管EMT PIPE 20mm(3/4\")", "2000mm" }, Index());

            Assert.Equal(new List<string> { "⌀20线管 2000mm" }, collapsed);
        }

        [Fact]
        public void CollapseLines_LeavesUnknownModelAlone_SoBareLengthStaysACable()
        {
            // 第一行不是清单型号，就不是 U1Q/U1C 生成的标注。
            // 绝不能把孤立的 2000mm 吞进配对里——它是电缆。
            List<string> collapsed = AnnotationLabelPair.CollapseLines(
                new List<string> { "说明", "2000mm" }, Index());

            Assert.Equal(new List<string> { "说明", "2000mm" }, collapsed);
        }

        [Fact]
        public void CollapseLines_LeavesKnownModelAloneWhenSecondLineIsNotALength()
        {
            List<string> collapsed = AnnotationLabelPair.CollapseLines(
                new List<string> { "梯形桥架200Wx100H", "备注" }, Index());

            Assert.Equal(new List<string> { "梯形桥架200Wx100H", "备注" }, collapsed);
        }

        [Fact]
        public void CollapseLines_NeverPairsAcrossEntities_OnlyAdjacentLines()
        {
            // 同一 MTEXT 内三行：只有紧邻的型号+长度配对。
            List<string> collapsed = AnnotationLabelPair.CollapseLines(
                new List<string> { "梯形桥架200Wx100H", "2500mm",
                    "镀锌穿线管EMT PIPE 38mm(1-1/2\")", "2000mm" }, Index());

            Assert.Equal(new List<string> { "桥架200*100 2500mm", "⌀38线管 2000mm" },
                collapsed);
        }

        [Fact]
        public void CollapseLines_DoesNotMapHoseModels()
        {
            // 软管标注走 Ruanguan 块属性，不参与文字统计；没有映射就不能被误配成线管。
            List<string> collapsed = AnnotationLabelPair.CollapseLines(
                new List<string> { "20mm(3/4\") 包塑金属软管(波纹管)", "2000mm" }, Index());

            Assert.Equal(new List<string> { "20mm(3/4\") 包塑金属软管(波纹管)", "2000mm" },
                collapsed);
        }

        [Fact]
        public void CollapseText_SingleLineLegacyLabelIsReturnedUnchanged()
        {
            Assert.Equal("桥架200*100 2500mm",
                AnnotationLabelPair.CollapseText("桥架200*100 2500mm", Index()));
            Assert.Equal("2000mm", AnnotationLabelPair.CollapseText("2000mm", Index()));
        }

        [Fact]
        public void CollapseText_TwoLineLabelBecomesTheLegacyForm()
        {
            // 旧图上同位置的单行标注靠这个键才能被识别删除。
            Assert.Equal("桥架200*100 2500mm",
                AnnotationLabelPair.CollapseText("梯形桥架200Wx100H\\P2500mm", Index()));
            Assert.Equal("⌀20线管 2000mm",
                AnnotationLabelPair.CollapseText(
                    "镀锌穿线管EMT PIPE 20mm(3/4\")\\P2000mm", Index()));
        }

        [Fact]
        public void Build_ThrowsWhenTheSameCatalogModelAppearsTwice()
        {
            Assert.Throws<InvalidDataException>(() => AnnotationModelIndex.Build(new[]
            {
                Item("2.6", "桥架", "1.名称:梯形桥架200Wx100H", "200*100"),
                Item("2.9", "桥架", "1.名称:梯形桥架200Wx100H", "250*100")
            }));
        }

        [Theory]
        [InlineData("桥架100*100", "梯形桥架100Wx100H")]
        [InlineData("桥架200*100", "梯形桥架200Wx100H")]
        [InlineData("桥架400*100", "梯形桥架400Wx100H")]
        public void EmbeddedCatalog_ResolvesTheModelEveryU1QCommandWrites(
            string spec, string expectedModel)
        {
            // U1Q100/U1Q200/U1Q400 的默认规格必须都能在固定清单里查到型号，
            // 否则这三条命令会直接中止。
            ListItem item = ListItemReader.EmbeddedCatalogIndex.FindBridge(
                BoqCatalogIndex.NormalizeBridgeSpec(spec));

            Assert.NotNull(item);
            Assert.Equal(expectedModel, BoqFeatureName.Extract(item.Feature));
        }

        [Fact]
        public void EmbeddedCatalog_CollapsesTheDefaultU1QLabelBackToItsCommandSpec()
        {
            string label = AnnotationLabelPair.Build("梯形桥架200Wx100H", "2500mm");

            List<string> collapsed = AnnotationLabelPair.CollapseLines(
                TextParser.SplitMTextLines(label));

            Assert.Equal(new List<string> { "桥架200*100 2500mm" }, collapsed);
        }

        [Fact]
        public void EmbeddedCatalog_ResolvesTheDiameterEveryU1CCommandWrites()
        {
            foreach (string diameter in new[] { "20", "25", "32", "38", "51" })
            {
                ListItem item = ListItemReader.EmbeddedCatalogIndex
                    .FindRigidConduit(diameter);
                Assert.NotNull(item);
                Assert.NotEqual("", BoqFeatureName.Extract(item.Feature));
            }
        }

        [Fact]
        public void TwoLineBridgeLabel_StillFeedsBridgeStatistics()
        {
            // 读取端把两行还原成单行后，统计引擎必须照旧认出桥架。
            string contents = AnnotationLabelPair.Build("梯形桥架200Wx100H", "2500mm");
            List<string> lines = AnnotationLabelPair.CollapseLines(
                TextParser.SplitMTextLines(contents));

            CableStatResult stat = StatCalculator.Calculate(lines, 250.0);

            Assert.Single(stat.Bridges);
            Assert.Equal("桥架200*100", stat.Bridges[0].Spec);
            Assert.Equal(2500.0, stat.Bridges[0].TotalMm);
            Assert.Empty(stat.CableFormatted);
        }

        [Fact]
        public void TwoLineConduitLabel_StillFeedsConduitStatistics()
        {
            string contents = AnnotationLabelPair.Build(
                "镀锌穿线管EMT PIPE 20mm(3/4\")", "2000mm");
            List<string> lines = AnnotationLabelPair.CollapseLines(
                TextParser.SplitMTextLines(contents));

            CableStatResult stat = StatCalculator.Calculate(lines, 250.0);

            ConduitStat conduit = Assert.Single(stat.Conduits);
            Assert.Equal("⌀20线管", conduit.Spec);
            Assert.Equal(2000.0, conduit.TotalMm);
            Assert.Empty(stat.CableFormatted);
        }

        [Fact]
        public void EmbeddedCatalog_CollapsesTheDefaultU1CLabelBackToTheLineForm()
        {
            string model = BoqFeatureName.Extract(
                ListItemReader.EmbeddedCatalogIndex.FindRigidConduit("20").Feature);
            string label = AnnotationLabelPair.Build(model,
                ConduitLabelFormatter.LengthText);

            List<string> collapsed = AnnotationLabelPair.CollapseLines(
                TextParser.SplitMTextLines(label));

            Assert.Equal(new List<string> { "⌀20线管 2000mm" }, collapsed);
        }

        [Fact]
        public void EmbeddedCatalog_MigratedDiameterCollapsesToTheCatalogDiameter()
        {
            // U1C 支持 Φ32，但固定清单里没有 32mm 行，靠“别名1”映射到 38mm 那行。
            // 第一行写的是清单 1.名称（38mm 穿线管），所以还原后是 ⌀38线管——
            // 与它最终命中的清单行一致。清单编码不变，仍是 3.3。
            string model = BoqFeatureName.Extract(
                ListItemReader.EmbeddedCatalogIndex.FindRigidConduit("32").Feature);
            Assert.Contains("38mm", model);

            List<string> collapsed = AnnotationLabelPair.CollapseLines(
                TextParser.SplitMTextLines(AnnotationLabelPair.Build(model, "2000mm")));

            Assert.Equal(new List<string> { "⌀38线管 2000mm" }, collapsed);
        }

        [Fact]
        public void EveryU1QLabelCollapsesBackToTheSpecTheCommandWasRunWith()
        {
            foreach (string spec in new[]
            {
                "桥架100*100", "桥架200*100", "桥架400*100"
            })
            {
                string model = BoqFeatureName.Extract(ListItemReader.EmbeddedCatalogIndex
                    .FindBridge(BoqCatalogIndex.NormalizeBridgeSpec(spec)).Feature);
                Assert.NotEqual("", model);

                List<string> collapsed = AnnotationLabelPair.CollapseLines(
                    TextParser.SplitMTextLines(
                        AnnotationLabelPair.Build(model, "2500mm")));

                Assert.Equal(new List<string> { spec + " 2500mm" }, collapsed);
            }
        }

        [Fact]
        public void BareLengthInAMultiLineMText_IsStillCountedAsCable()
        {
            // 老图纸里沿用的多行 MTEXT：行首不是清单型号，长度行必须照旧算电缆。
            List<string> lines = AnnotationLabelPair.CollapseLines(
                TextParser.SplitMTextLines("说明\\P2000mm"));

            CableStatResult stat = StatCalculator.Calculate(lines, 250.0);

            Assert.Equal(new List<string> { "2" }, stat.CableFormatted);
            Assert.Empty(stat.Bridges);
            Assert.Empty(stat.Conduits);
        }
    }
}
