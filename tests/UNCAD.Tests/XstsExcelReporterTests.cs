using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Report;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XstsExcelReporterTests
    {
        [Fact]
        public void WriteToFile_RoundTripsAllFrameItemsWithoutBlindOrNearNameSheets()
        {
            var first = new XstsCircuitRecord("M1", "主回路") { FrameHandle = "001234" };
            first.TableRows.Add(new List<string> { "1", "电缆", "1.名称:YJV-3*25\n2.说明:现场敷设", "m", "12.50", "1.1" });
            first.TableRows.Add(new List<string> { "2", "桥架", "200*100", "m", "8", "2.1" });
            first.TableRows.Add(new List<string> { "自填", "其他配件", "=保留原文", "套", "待确认", "00123" });
            first.TableRows.Add(new List<string> { "4", "断路器", "3P63A", "个", "0", "5.2" });
            var second = new XstsCircuitRecord("M1", "主回路") { FrameHandle = "001235" };
            XstsReport report = XstsReportBuilder.Build(new[] { first, second },
                new[] { new XstsCircuitRecord("M1", "主回路") });
            string path = Path.Combine(Path.GetTempPath(), "uncad_xsts_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                XstsExcelReporter.WriteToFile(report, path);
                using (var input = File.OpenRead(path))
                {
                    var workbook = new XSSFWorkbook(input);
                    try
                    {
                        Assert.Equal(new[] { "回路统计", "图框明细", "异常图框" },
                            Enumerable.Range(0, workbook.NumberOfSheets).Select(workbook.GetSheetName));
                        ISheet summary = workbook.GetSheet("回路统计");
                        Assert.Equal("框选图框数量", summary.GetRow(2).GetCell(0).StringCellValue);
                        Assert.Equal(2d, summary.GetRow(2).GetCell(1).NumericCellValue);
                        Assert.Equal("机台数量", summary.GetRow(2).GetCell(2).StringCellValue);
                        Assert.Equal(1d, summary.GetRow(2).GetCell(3).NumericCellValue);
                        var details = (XSSFSheet)workbook.GetSheet("图框明细");
                        Assert.Equal(2, details.LastRowNum);
                        // D-057：图框明细在“回路名称”后插入“上游编号”（同机台同名回路靠它认领），
                        // 其后各列顺移一位。
                        Assert.Equal(new[] { "机台 ID", "回路名称", "上游编号", "图框ID", "电缆型号", "电缆米数",
                            "桥架型号", "桥架米数", "软管型号", "软管米数", "线管型号", "线管米数",
                            "其他配件型号", "其他配件数量（套）", "断路器型号", "断路器数量（个）" },
                            details.GetRow(0).Cells.Select(cell => cell.StringCellValue));
                        Assert.Equal("001234", details.GetRow(1).GetCell(3).StringCellValue);
                        Assert.Equal("", details.GetRow(1).GetCell(2).StringCellValue);
                        Assert.Equal("YJV-3*25", details.GetRow(1).GetCell(4).StringCellValue);
                        Assert.Equal("12.50", details.GetRow(1).GetCell(5).StringCellValue);
                        Assert.Contains("2.说明:现场敷设", details.GetRow(1).GetCell(4).CellComment.String.String);
                        Assert.Equal("200*100", details.GetRow(1).GetCell(6).StringCellValue);
                        Assert.Equal("8", details.GetRow(1).GetCell(7).StringCellValue);
                        Assert.Equal("", details.GetRow(1).GetCell(8).StringCellValue);
                        Assert.Equal("", details.GetRow(1).GetCell(9).StringCellValue);
                        Assert.Equal("", details.GetRow(1).GetCell(10).StringCellValue);
                        Assert.Equal("", details.GetRow(1).GetCell(11).StringCellValue);
                        Assert.Equal(CellType.String, details.GetRow(1).GetCell(12).CellType);
                        Assert.Equal("=保留原文", details.GetRow(1).GetCell(12).StringCellValue);
                        Assert.Equal("待确认", details.GetRow(1).GetCell(13).StringCellValue);
                        Assert.Contains("编码：00123", details.GetRow(1).GetCell(12).CellComment.String.String);
                        Assert.Equal("3P63A", details.GetRow(1).GetCell(14).StringCellValue);
                        Assert.Equal("0", details.GetRow(1).GetCell(15).StringCellValue);
                        Assert.Equal("001235", details.GetRow(2).GetCell(3).StringCellValue);
                        Assert.Equal("", details.GetRow(2).GetCell(4).StringCellValue);
                        Assert.True(details.GetRow(1).GetCell(4).CellStyle.WrapText);
                        Assert.Equal(32, details.GetRow(1).HeightInPoints);
                        Assert.True(details.PaneInformation.IsFreezePane());
                        Assert.Equal(4, details.PaneInformation.VerticalSplitPosition);
                        Assert.Equal("A1:P3", details.GetCTWorksheet().autoFilter.@ref);
                        ISheet issues = workbook.GetSheet("异常图框");
                        Assert.Equal("图框ID", issues.GetRow(0).GetCell(0).StringCellValue);
                        Assert.Equal("001235", issues.GetRow(1).GetCell(0).StringCellValue);
                        Assert.Contains("重复", issues.GetRow(1).GetCell(3).StringCellValue);
                        // D-057：机台表追加“重复图框”“待确认”两列（老列位不变）。
                        Assert.Equal("重复图框", summary.GetRow(4).GetCell(7).StringCellValue);
                        Assert.Equal("待确认", summary.GetRow(4).GetCell(8).StringCellValue);
                        Assert.Equal(1d, summary.GetRow(5).GetCell(7).NumericCellValue);
                        Assert.Equal(0d, summary.GetRow(5).GetCell(8).NumericCellValue);
                        Assert.True(workbook.NumCellStyles < 10);
                    }
                    finally { workbook.Close(); }
                }
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void CreateWorkbook_EmptyAndUnnamedListsKeepOneRowPerFrame()
        {
            var unnamed = new XstsCircuitRecord("M1", "A") { FrameHandle = "0001" };
            unnamed.TableRows.Add(new List<string> { "", "", "只有特征", "", "", "" });
            var empty = new XstsCircuitRecord("", "", "读取失败") { FrameHandle = "0002" };
            XstsReport report = XstsReportBuilder.Build(new[] { unnamed, empty },
                new XstsCircuitRecord[0], XstsExpectedDataStatus.NotConfigured);

            IWorkbook workbook = XstsExcelReporter.CreateWorkbook(report);
            try
            {
                ISheet details = workbook.GetSheet("图框明细");
                Assert.Equal(2, details.LastRowNum);
                Assert.Equal("未命名项目型号", details.GetRow(0).GetCell(12).StringCellValue);
                Assert.Contains("只有特征", details.GetRow(1).GetCell(12).StringCellValue);
                Assert.Equal("0002", details.GetRow(2).GetCell(3).StringCellValue);
                Assert.Equal("", details.GetRow(2).GetCell(4).StringCellValue);
            }
            finally { workbook.Close(); }

            report = XstsReportBuilder.Build(new[] { empty }, new XstsCircuitRecord[0]);
            workbook = XstsExcelReporter.CreateWorkbook(report);
            try
            {
                ISheet details = workbook.GetSheet("图框明细");
                Assert.Equal(1, details.LastRowNum);
                Assert.Equal(12, details.GetRow(0).Cells.Count);
                Assert.Equal("0002", details.GetRow(1).GetCell(3).StringCellValue);
            }
            finally { workbook.Close(); }
        }

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
                    .GetCell(4).StringCellValue);
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
                Assert.Equal(CellType.Numeric, summary.GetRow(5).GetCell(3).CellType);
                Assert.Equal(CellType.Numeric, summary.GetRow(5).GetCell(4).CellType);
                Assert.Equal(CellType.Numeric, summary.GetRow(6).GetCell(3).CellType);
                Assert.Equal(CellType.Numeric, summary.GetRow(6).GetCell(4).CellType);
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void CreateWorkbook_WritesMachineBatchAndSequenceColumns()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("M1", "A") },
                new[] { new XstsCircuitRecord("M1", "A", "B-01", "9") });

            IWorkbook workbook = XstsExcelReporter.CreateWorkbook(report);
            try
            {
                ISheet summary = workbook.GetSheet("回路统计");
                Assert.Equal("批次", summary.GetRow(4).GetCell(1).StringCellValue);
                Assert.Equal("序号", summary.GetRow(4).GetCell(2).StringCellValue);
                Assert.Equal("B-01", summary.GetRow(5).GetCell(1).StringCellValue);
                Assert.Equal("9", summary.GetRow(5).GetCell(2).StringCellValue);
            }
            finally { workbook.Close(); }
        }
    }
}
