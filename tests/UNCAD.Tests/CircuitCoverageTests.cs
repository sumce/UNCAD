using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 2.10.0 整机回路校验：生成 BOQ/DWG 前必须覆盖机台数据里的所有回路。
    /// 比较忽略 Unicode 空白与大小写，结果保留原始写法供命令行核对。
    /// </summary>
    public sealed class CircuitCoverageTests
    {
        [Fact]
        public void Evaluate_CompleteWhenSameCircuitsIgnoringOrderWhitespaceAndCase()
        {
            CircuitCoverageResult result = CircuitCoverage.Evaluate("M1",
                new[] { "1AA1", "1AA2", "1AA3" },
                new[] { "1aa3", " 1AA1 ", "1AA2" });

            Assert.True(result.Complete);
            Assert.Empty(result.Missing);
            Assert.Equal(3, result.Expected.Count);
            Assert.Equal(3, result.Selected.Count);
        }

        [Fact]
        public void Evaluate_ReportsMissingInExpectedOrder()
        {
            CircuitCoverageResult result = CircuitCoverage.Evaluate("M1",
                new[] { "1AA1", "1AA2", "1AA3" },
                new[] { "1AA3" });

            Assert.False(result.Complete);
            Assert.Equal(new[] { "1AA1", "1AA2" }, result.Missing);
            Assert.Equal("M1", result.MachineId);
        }

        [Fact]
        public void Evaluate_ExtraSelectedCircuitsDoNotFailCoverage()
        {
            // 机台数据里没有、但图上框选到的回路不应被判成"缺回路"。
            CircuitCoverageResult result = CircuitCoverage.Evaluate("M1",
                new[] { "1AA1" },
                new[] { "1AA1", "1AA9" });

            Assert.True(result.Complete);
        }

        [Fact]
        public void Evaluate_EmptyExpectedIsComplete()
        {
            CircuitCoverageResult result = CircuitCoverage.Evaluate("M1",
                new string[0], new[] { "1AA1" });

            Assert.True(result.Complete);
        }

        [Fact]
        public void Evaluate_DeduplicatesAndIgnoresBlankValues()
        {
            CircuitCoverageResult result = CircuitCoverage.Evaluate("  M1  ",
                new[] { "1AA1", " 1AA1 ", "", null },
                new[] { " ", "1AA1" });

            Assert.True(result.Complete);
            Assert.Equal("M1", result.MachineId);
            Assert.Single(result.Expected);
            Assert.Single(result.Selected);
        }

        // ---- D-056 带键口径：同机台同名回路 ----

        private static CircuitSelection Selection(string name, string upstream,
            string key = null)
        {
            return new CircuitSelection
            {
                MachineId = "M01",
                CircuitName = name,
                CircuitKey = key ?? CircuitIdentityKey.Of(
                    new UNCAD.Core.Excel.MachineRow
                        { MachineId = "M01", CircuitName = name, Fr = upstream }),
                UpstreamId = upstream
            };
        }

        [Fact]
        public void Evaluate_Keyed_SameNameDrawnOnceReportsTheOtherMissing()
        {
            // 旧口径（按名 Distinct）会塌缩：两条 ECS 只画一条也判全齐。
            CircuitCoverageResult result = CircuitCoverage.Evaluate("M01",
                new[] { Selection("ECS", "3N-11B1A-PP-02"), Selection("ECS", "3N-31D2A-PP-03") },
                new[] { Selection("ECS", "3N-31D2A-PP-03") });

            Assert.False(result.Complete);
            string missing = Assert.Single(result.Missing);
            Assert.Contains("ECS", missing);
            Assert.Contains("3N-11B1A-PP-02", missing);
            Assert.Equal(2, result.Expected.Count);
        }

        [Fact]
        public void Evaluate_Keyed_SameNameBothDrawnPasses()
        {
            CircuitCoverageResult result = CircuitCoverage.Evaluate("M01",
                new[] { Selection("ECS", "3N-11B1A-PP-02"), Selection("ECS", "3N-31D2A-PP-03"), Selection("LP", "3N-A-PP-01") },
                new[] { Selection("LP", "3N-A-PP-01"), Selection("ECS", "3N-31D2A-PP-03"), Selection("ECS", "3N-11B1A-PP-02") });

            Assert.True(result.Complete);
            Assert.Empty(result.Missing);
        }

        [Fact]
        public void Evaluate_Keyed_LegacyFrameWithoutKeyMatchesByNameOnce()
        {
            // 老图框（无键）+ 同名双回路：名字只能满足其中一条，另一条报缺。
            var legacyFrame = new CircuitSelection
                { MachineId = "M01", CircuitName = "ECS" };
            CircuitCoverageResult result = CircuitCoverage.Evaluate("M01",
                new[] { Selection("ECS", "3N-11B1A-PP-02"), Selection("ECS", "3N-31D2A-PP-03") },
                new[] { legacyFrame });

            Assert.False(result.Complete);
            Assert.Single(result.Missing);
        }

        [Fact]
        public void Evaluate_Keyed_UpstreamRehomeStillMatchesByName()
        {
            // 帧上键过期（上游已改挂），但名字唯一：按名回退命中，不误报缺。
            string staleKey = CircuitIdentityKey.Of(new UNCAD.Core.Excel.MachineRow
                { MachineId = "M01", CircuitName = "LP", Fr = "3N-OLD-PP-01" });
            CircuitCoverageResult result = CircuitCoverage.Evaluate("M01",
                new[] { Selection("LP", "3N-NEW-PP-01") },
                new[] { new CircuitSelection
                    { MachineId = "M01", CircuitName = "LP", CircuitKey = staleKey } });

            Assert.True(result.Complete);
        }
    }
}