using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>统计引擎测试：电缆、桥架、线管长度聚合与报表格式。</summary>
    public class StatCalculatorTests
    {
        [Fact]
        public void Calculate_SumCableLengths_OnlyStrictLabels()
        {
            // "电缆 2000mm" 带前缀不算；只有纯数字+00+mm 的整行才算
            var lines = new List<string> { "1200mm", "2000mm", "3000mm", "电缆 2000mm" };
            var r = StatCalculator.Calculate(lines, 250.0);

            Assert.Equal(3, r.CableFormatted.Count);
            Assert.Equal(1.2 + 2.0 + 3.0, r.CableSum, 4);
        }

        [Fact]
        public void Calculate_AggregatesBridgeGrids_BySpec()
        {
            var lines = new List<string>
            {
                "桥架200*100 10格",
                "桥架200*100 5.5格",
                "桥架400*100 20格"
            };
            var r = StatCalculator.Calculate(lines, 250.0);

            Assert.Equal(2, r.Bridges.Count);
            var b200 = r.Bridges.Single(b => b.Spec == "桥架200*100");
            var b400 = r.Bridges.Single(b => b.Spec == "桥架400*100");
            Assert.Equal(15.5, b200.TotalGrids, 4);
            Assert.Equal(20.0, b400.TotalGrids, 4);
        }

        [Fact]
        public void Calculate_NormalizesBridgeSeparatorsIntoOneGroup()
        {
            var r = StatCalculator.Calculate(new[]
            {
                "桥架200*100 2格", "桥架200x100 3格", "桥架200X100 4格",
                "桥架200×100 5格"
            }, 250.0);

            var bridge = Assert.Single(r.Bridges);
            Assert.Equal("桥架200*100", bridge.Spec);
            Assert.Equal(14.0, bridge.TotalGrids);
        }

        [Fact]
        public void Calculate_ReadsCurrentMillimetreBridgeLabels()
        {
            var result = StatCalculator.Calculate(new[]
            {
                "桥架200*100 2500mm", "桥架200×100 1250mm"
            }, 250.0);

            var bridge = Assert.Single(result.Bridges);
            Assert.Equal(15.0, bridge.TotalGrids, 4);
            Assert.Equal(3750.0, bridge.TotalMm, 4);
            Assert.Equal(3.75, bridge.TotalM, 4);
        }

        [Fact]
        public void Calculate_IgnoresLinesWithOnlyGridWord()
        {
            var r = StatCalculator.Calculate(new[] { "10格" }, 250.0);
            Assert.Empty(r.Bridges);
            Assert.Equal(0.0, r.CableSum);
        }

        [Fact]
        public void Calculate_IgnoresBridgeWordWithoutSpec()
        {
            // 含"桥架"但无规格尺寸 → 不生成假"桥架"行
            var r = StatCalculator.Calculate(new[] { "桥架安装完成 5格" }, 250.0);
            Assert.Empty(r.Bridges);
        }

        [Fact]
        public void Calculate_RequiresFullBridgeLabel()
        {
            // 只有"桥架开头 + xx*xx + 数字格结尾"的整行才算
            var lines = new List<string>
            {
                "桥架400*100 10格",
                "桥架200*100 13.5格",   // 小数格数
                "安装桥架400*100 10格", // 不以"桥架"开头 → 不算
                "桥架400*100 10格 说明" // 结尾不是"格" → 不算
            };
            var r = StatCalculator.Calculate(lines, 250.0);

            Assert.Equal(2, r.Bridges.Count);
            Assert.Contains(r.Bridges, b => b.Spec == "桥架400*100" && b.TotalGrids == 10.0);
            Assert.Contains(r.Bridges, b => b.Spec == "桥架200*100" && b.TotalGrids == 13.5);
        }

        [Fact]
        public void Calculate_AggregatesConduitLengths_ByDiameter()
        {
            var r = StatCalculator.Calculate(new[]
            {
                "⌀20线管 2000mm",
                "⌀20线管 2500mm",
                "Ø25线管 3000mm"
            }, 250.0);

            Assert.Equal(2, r.Conduits.Count);
            Assert.Equal(4500.0, r.Conduits.Single(c => c.Spec == "⌀20线管").TotalMm);
            Assert.Equal(3.0, r.Conduits.Single(c => c.Spec == "⌀25线管").TotalM);
        }

        [Fact]
        public void Calculate_RejectsBridgePrefixAndEmbeddedNotes()
        {
            var result = StatCalculator.Calculate(new[]
            {
                "(共用)桥架200*100 12格",
                "桥架200*100 共用 12格",
                "桥架200*100 12格 备注"
            }, 250.0);

            Assert.Empty(result.Bridges);
        }

        [Fact]
        public void Calculate_HonorsIndependentCategorySwitches()
        {
            var result = StatCalculator.Calculate(new[]
            {
                "2000mm", "桥架200*100 4格", "⌀20线管 3000mm"
            }, new StatCalculationOptions
            {
                MmPerGrid = 250.0,
                IncludeCable = false,
                IncludeBridge = true,
                IncludeConduit = false
            });

            Assert.Equal(0, result.CableSum);
            Assert.Single(result.Bridges);
            Assert.Equal(1.0, result.Bridges[0].TotalM);
            Assert.Empty(result.Conduits);
        }

        [Fact]
        public void BuildReport_ContainsConduitFormula()
        {
            var report = StatCalculator.BuildReport(new[]
            {
                "⌀20线管 2000mm", "⌀20线管 2500mm"
            }, 250.0);

            Assert.Contains("⌀20线管: 2+2.5=4.5M", report);
        }

        [Fact]
        public void BuildReport_FormatsCableZero_WhenNoCables()
        {
            var report = StatCalculator.BuildReport(new List<string>(), 250.0);
            Assert.Contains("电缆长度: 0M", report);
        }

        [Fact]
        public void BuildReport_DoesNotEmitDisabledCategory()
        {
            var report = StatCalculator.BuildReport(new[] { "桥架200*100 2格" },
                new StatCalculationOptions
                {
                    MmPerGrid = 250.0,
                    IncludeCable = false,
                    IncludeBridge = true,
                    IncludeConduit = false
                });

            Assert.DoesNotContain(report, line => line.StartsWith("电缆长度:"));
            Assert.Single(report);
            Assert.StartsWith("桥架200*100", report[0]);
        }

        [Fact]
        public void BuildReport_ContainsBridgeMath()
        {
            var lines = new List<string> { "桥架200*100 10格" };
            var report = StatCalculator.BuildReport(lines, 250.0);
            string line = report.Single(s => s.StartsWith("桥架200*100"));
            Assert.Contains("2500mm", line);
            Assert.Contains("2500mm", line); // 10 * 250
            Assert.Contains("2.5M", line);    // 2500 / 1000
        }

        [Fact]
        public void MmPerGrid_IsConfigurable()
        {
            var r = StatCalculator.Calculate(new[] { "桥架200*100 2格" }, 300.0);
            var b = r.Bridges.Single();
            Assert.Equal(300.0, b.MmPerGrid);
            Assert.Equal(600.0, b.TotalMm, 4);
        }
    }
}
