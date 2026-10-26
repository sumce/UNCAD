using System;
using System.Collections.Generic;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Submission;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// DATA 工作表：一行一个回路；重复写入按「机台ID + 回路名」更新而不是追加，
    /// 同机台其它回路的行保留不动。
    /// </summary>
    public sealed class BoqDataSheetTests
    {
        private static readonly DateTime FixedTime = new DateTime(2026, 10, 26, 16, 30, 5);

        [Fact]
        public void Write_CreatesHeaderAndOneRowPerDevice()
        {
            var workbook = new XSSFWorkbook();
            try
            {
                BoqDataSheet.Write(workbook,
                    new[] { Record("设备A", "100"), Record("设备B", "200") }, FixedTime);

                ISheet sheet = workbook.GetSheet("DATA");
                Assert.NotNull(sheet);
                Assert.Equal("更新时间", Cell(sheet, 0, 0));
                Assert.Equal("回路", Cell(sheet, 0, 2));
                Assert.Equal("电缆型号", Cell(sheet, 0, 4));
                Assert.Equal("材料行数", Cell(sheet, 0, 14));

                Assert.Equal("2026-10-26 16:30:05", Cell(sheet, 1, 0));
                Assert.Equal("MACHINE01", Cell(sheet, 1, 1));
                Assert.Equal("设备A", Cell(sheet, 1, 2));
                Assert.Equal("I-Line盘", Cell(sheet, 1, 3));
                Assert.Equal("ZB-YJV", Cell(sheet, 1, 4));
                Assert.Equal(100d, sheet.GetRow(1).GetCell(5).NumericCellValue, 3);
                Assert.Equal("FLEX", Cell(sheet, 1, 6));
                Assert.Equal(7.5d, sheet.GetRow(1).GetCell(7).NumericCellValue, 3);
                Assert.Equal("1/T", Cell(sheet, 1, 12));
                Assert.Equal(2d, sheet.GetRow(1).GetCell(14).NumericCellValue, 3);

                Assert.Equal("设备B", Cell(sheet, 2, 2));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void Write_SecondRunUpdatesRowInsteadOfAppending()
        {
            var workbook = new XSSFWorkbook();
            try
            {
                BoqDataSheet.Write(workbook, new[] { Record("设备A", "100") }, FixedTime);
                BoqDataSheet.Write(workbook, new[] { Record("设备A", "150") },
                    FixedTime.AddMinutes(5));

                ISheet sheet = workbook.GetSheet("DATA");
                Assert.Equal(1, DataRowCount(sheet));
                Assert.Equal(150d, sheet.GetRow(1).GetCell(5).NumericCellValue, 3);
                Assert.Equal("2026-10-26 16:35:05", Cell(sheet, 1, 0));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void Write_PreservesOtherDeviceRows()
        {
            var workbook = new XSSFWorkbook();
            try
            {
                BoqDataSheet.Write(workbook, new[] { Record("设备A", "100") }, FixedTime);
                BoqDataSheet.Write(workbook, new[] { Record("设备B", "200") }, FixedTime);
                BoqDataSheet.Write(workbook, new[] { Record("设备A", "120") },
                    FixedTime.AddMinutes(1));

                ISheet sheet = workbook.GetSheet("DATA");
                Assert.Equal(2, DataRowCount(sheet));
                Assert.Equal("设备A", Cell(sheet, 1, 2));
                Assert.Equal(120d, sheet.GetRow(1).GetCell(5).NumericCellValue, 3);
                Assert.Equal("设备B", Cell(sheet, 2, 2));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void BoqWrite_AddsDataSheetToTargetWorkbook()
        {
            // 集成：走真实 BOQ 写入，确认目标文件里出现 DATA 表且数据落格。
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_data_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "machine", "[BOQ]MACHINE01.xlsx");
            try
            {
                var builder = new XSSFWorkbook();
                ISheet sheet = builder.CreateSheet("Sheet1");
                sheet.CreateRow(0).CreateCell(0).SetCellValue("1.1");
                sheet.GetRow(0).CreateCell(4).SetCellValue(0d);
                using (var stream = new FileStream(template, FileMode.CreateNew))
                    builder.Write(stream);
                builder.Close();

                BoqWorkbookWriter.Write(target, template, new[]
                {
                    new SubmissionRecord
                    {
                        MachineId = "MACHINE01",
                        DeviceName = "设备A",
                        Cable = "ZB-YJV-4*25",
                        CableMeters = "57.7",
                        Diameter = "25",
                        FlexibleConduitMeters = "1.5",
                        ConduitInfo = "25",
                        ConduitMeters = "12.3",
                        BridgeInfo = "200x100",
                        BridgeMeters = "2.3",
                        UpstreamAxis = "1/T",
                        DownstreamAxis = "2/T",
                        Materials = new List<SubmissionMaterial>
                        {
                            new SubmissionMaterial { Code = "1.1", Quantity = "1" }
                        }
                    }
                });

                using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read))
                {
                    var result = new XSSFWorkbook(stream);
                    ISheet data = result.GetSheet("DATA");
                    Assert.NotNull(data);
                    Assert.Equal("更新时间", data.GetRow(0).GetCell(0).StringCellValue);
                    Assert.Equal("设备A", data.GetRow(1).GetCell(2).StringCellValue);
                    Assert.Equal("ZB-YJV-4*25", data.GetRow(1).GetCell(4).StringCellValue);
                    Assert.Equal(57.7d, data.GetRow(1).GetCell(5).NumericCellValue, 3);
                    Assert.Equal(12.3d, data.GetRow(1).GetCell(11).NumericCellValue, 3);
                    Assert.False(string.IsNullOrWhiteSpace(
                        data.GetRow(1).GetCell(0).StringCellValue));
                    result.Close();
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static SubmissionRecord Record(string deviceName, string cableMeters)
            => new SubmissionRecord
            {
                MachineId = "MACHINE01",
                DeviceName = deviceName,
                PanelType = "I-Line盘",
                Cable = "ZB-YJV",
                CableMeters = cableMeters,
                Diameter = "FLEX",
                FlexibleConduitMeters = "7.5",
                BridgeInfo = "200x100",
                BridgeMeters = "2.3",
                ConduitInfo = "25",
                ConduitMeters = "12.3",
                UpstreamAxis = "1/T",
                DownstreamAxis = "2/T",
                Materials = new List<SubmissionMaterial>
                {
                    new SubmissionMaterial { Code = "1.1", Quantity = "1" },
                    new SubmissionMaterial { Code = "3.3", Quantity = "2" }
                }
            };

        private static int DataRowCount(ISheet sheet) => Math.Max(0, sheet.LastRowNum);

        private static string Cell(ISheet sheet, int row, int column)
        {
            ICell cell = sheet.GetRow(row)?.GetCell(column);
            return cell == null ? "" : (cell.ToString() ?? "").Trim();
        }
    }
}
