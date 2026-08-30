using System.IO;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    public class ExcelHeaderBinderTests
    {
        [Fact]
        public void Require_TreatsAliasesAsAlternatives()
        {
            var workbook = new XSSFWorkbook();
            var header = workbook.CreateSheet("机台").CreateRow(0);
            header.CreateCell(0).SetCellValue("回路名称");
            header.CreateCell(1).SetCellValue("设备名称");

            Assert.Equal(0, ExcelHeaderBinder.Require(header, "机台", "回路名称", "设备名称"));
        }

        [Fact]
        public void Require_RejectsRepeatedSameAlias()
        {
            var workbook = new XSSFWorkbook();
            var header = workbook.CreateSheet("机台").CreateRow(0);
            header.CreateCell(0).SetCellValue("回路名称");
            header.CreateCell(1).SetCellValue("回路名称");

            var error = Assert.Throws<InvalidDataException>(() =>
                ExcelHeaderBinder.Require(header, "机台", "回路名称", "设备名称"));
            Assert.Contains("回路名称", error.Message);
        }
    }
}
