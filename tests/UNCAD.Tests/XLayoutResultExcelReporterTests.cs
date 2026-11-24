using System;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Dwg;
using UNCAD.Core.Report;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XLayoutResultExcelReporterTests
    {
        [Fact]
        public void WriteToFile_ProducesFilterAndFrameSheetsWithDoneAndNotFound()
        {
            var report = new XLayoutResultReport
            {
                HasFilterList = true,
                FilterSourcePath = @"C:\tmp\清单.xlsx",
                GeneratedAt = new DateTime(2026, 11, 22, 12, 0, 0)
            };
            report.FilterRows.Add(new XLayoutResultListRow
            {
                MachineId = "M1",
                CircuitName = "A",
                Outcome = XLayoutFilterOutcome.Done,
                FrameHandles = "h1",
                Note = "2 个回路参与排版"
            });
            report.FilterRows.Add(new XLayoutResultListRow
            {
                MachineId = "M9",
                CircuitName = "Ghost",
                Outcome = XLayoutFilterOutcome.NotFound
            });
            report.FrameRows.Add(new XLayoutResultFrameRow
            {
                MachineId = "M1",
                CircuitName = "A",
                UpstreamId = "3N-11B1A-PP-02",
                FrameHandle = "001234",
                Note = ""
            });

            string path = Path.Combine(Path.GetTempPath(),
                "uncad_xlayout_result_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                XLayoutResultExcelReporter.WriteToFile(report, path);
                using (var input = File.OpenRead(path))
                {
                    var workbook = new XSSFWorkbook(input);
                    try
                    {
                        Assert.Equal(new[] { "清单结果", "排版图框" },
                            new[] { workbook.GetSheetName(0), workbook.GetSheetName(1) });

                        ISheet filter = workbook.GetSheet("清单结果");
                        // 0 标题、1 生成时间、2 表头、3+ 数据。
                        Assert.Equal("机台ID", filter.GetRow(2).GetCell(0).StringCellValue);
                        // D-057：回路名后插入“上游编号”列（同机台同名回路靠它认领），
                        // 状态/句柄/备注顺移一位。
                        Assert.Equal("上游编号", filter.GetRow(2).GetCell(2).StringCellValue);
                        Assert.Equal("Done", filter.GetRow(3).GetCell(3).StringCellValue);
                        Assert.Equal("找不到", filter.GetRow(4).GetCell(3).StringCellValue);
                        // 句柄按文本写（防 Excel 数值化丢前导零）。
                        ISheet frames = workbook.GetSheet("排版图框");
                        Assert.Equal("上游编号", frames.GetRow(2).GetCell(2).StringCellValue);
                        Assert.Equal("3N-11B1A-PP-02", frames.GetRow(3).GetCell(2).StringCellValue);
                        Assert.Equal(CellType.String,
                            frames.GetRow(3).GetCell(3).CellType);
                        Assert.Equal("001234",
                            frames.GetRow(3).GetCell(3).StringCellValue);
                        var typed = (XSSFSheet)filter;
                        Assert.True(typed.PaneInformation.IsFreezePane());
                        // 行冻结在第 3 行（表头下方）：NPOI 里水平分割线的行位
                        // 报告为 HorizontalSplitPosition。
                        Assert.Equal(3, typed.PaneInformation.HorizontalSplitPosition);
                        workbook.Close();
                    }
                    finally { workbook.Close(); }
                }
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void WriteToFile_WithoutFilterList_HasOnlyFrameSheet()
        {
            var report = new XLayoutResultReport
            {
                GeneratedAt = new DateTime(2026, 11, 22, 12, 0, 0)
            };
            report.FrameRows.Add(new XLayoutResultFrameRow
            {
                MachineId = "M1",
                CircuitName = "A",
                FrameHandle = "h1"
            });

            string path = Path.Combine(Path.GetTempPath(),
                "uncad_xlayout_result_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                XLayoutResultExcelReporter.WriteToFile(report, path);
                using (var input = File.OpenRead(path))
                {
                    var workbook = new XSSFWorkbook(input);
                    try
                    {
                        Assert.Equal(1, workbook.NumberOfSheets);
                        Assert.Equal("排版图框", workbook.GetSheetName(0));
                    }
                    finally { workbook.Close(); }
                }
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
