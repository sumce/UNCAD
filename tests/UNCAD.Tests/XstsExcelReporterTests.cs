using NPOI.SS.UserModel;
using UNCAD.Core.Report;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XstsExcelReporterTests
    {
        [Fact]
        public void CreateWorkbook_WritesUnknownCoverageAndFrameIssuesExplicitly()
        {
            XstsReport report = XstsReportBuilder.Build(new[]
            {
                new XstsCircuitRecord("M1", "A"),
                new XstsCircuitRecord("", "B")
            }, new XstsCircuitRecord[0], XstsExpectedDataStatus.NotConfigured,
                "未配置机台 Excel");

            IWorkbook workbook = XstsExcelReporter.CreateWorkbook(report);
            try
            {
                ISheet summary = workbook.GetSheet("回路统计");
                Assert.Contains("无法判断", summary.GetRow(summary.LastRowNum)
                    .GetCell(2).StringCellValue);
                ISheet issues = workbook.GetSheet("异常图框");
                Assert.NotNull(issues);
                Assert.True(issues.LastRowNum >= 1);
                Assert.Equal("缺少机台ID", issues.GetRow(1).GetCell(3).StringCellValue);
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void CreateWorkbook_WritesKnownCountsAsNumericCells()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("M1", "A") },
                new[] { new XstsCircuitRecord("M1", "A") });

            IWorkbook workbook = XstsExcelReporter.CreateWorkbook(report);
            try
            {
                ISheet summary = workbook.GetSheet("回路统计");
                Assert.Equal(CellType.Numeric, summary.GetRow(2).GetCell(1).CellType);
                Assert.Equal(1d, summary.GetRow(2).GetCell(1).NumericCellValue);
                Assert.Equal(CellType.Numeric, summary.GetRow(5).GetCell(1).CellType);
                Assert.Equal(CellType.Numeric, summary.GetRow(5).GetCell(2).CellType);
                Assert.Equal(CellType.Numeric, summary.GetRow(6).GetCell(1).CellType);
                Assert.Equal(CellType.Numeric, summary.GetRow(6).GetCell(2).CellType);
            }
            finally { workbook.Close(); }
        }
    }
}
