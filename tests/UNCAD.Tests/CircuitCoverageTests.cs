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
    }
}