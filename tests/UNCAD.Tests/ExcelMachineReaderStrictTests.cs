using System;
using System.IO;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class ExcelMachineReaderStrictTests
    {
        [Fact]
        public void LegacyColumnsAreRejectedInsteadOfUsedAsFallback()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("旧表");
            sheet.CreateRow(0).CreateCell(0).SetCellValue("机台ID");
            sheet.GetRow(0).CreateCell(1).SetCellValue("回路名称");
            sheet.CreateRow(1).CreateCell(0).SetCellValue("OLD");
            sheet.GetRow(1).CreateCell(1).SetCellValue("旧回路");
            try
            {
                Assert.Throws<InvalidDataException>(() => ExcelMachineReader.ReadAll(workbook));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void UnifiedColumnsWinWhenLegacyColumnsAreAlsoPresent()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("机台数据");
            string[] headers = {
                "机台ID", "回路名称", "U_区域", "U_机台ID", "U_设备楼层", "U_设备轴位",
                "U_上游编号", "U_上游楼层", "U_上游轴位", "U_配电信息", "U_电缆型号",
                "U_上游类型", "U_厂务开关" };
            string[] values = {
                "OLD-ID", "正常回路", "ETCH", "NEW-ID", "2F", "54/W", "UP-01", "1F",
                "54/X", "N208V 3P4W 3P400A", "3*2.5", "母线插接口", "1P20A" };
            var header = sheet.CreateRow(0);
            var row = sheet.CreateRow(1);
            for (int index = 0; index < headers.Length; index++)
            {
                header.CreateCell(index).SetCellValue(headers[index]);
                row.CreateCell(index).SetCellValue(values[index]);
            }
            try
            {
                MachineRow result = Assert.Single(ExcelMachineReader.ReadAll(workbook));
                Assert.Equal("NEW-ID", result.MachineId);
                Assert.Equal("ETCH", result.Region);
                Assert.Equal("3*2.5", result.Cable);
                Assert.Equal("母线插接口", result.Next);
            }
            finally { workbook.Close(); }
        }
    }
}
