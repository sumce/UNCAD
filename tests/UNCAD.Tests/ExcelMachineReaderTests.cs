using System;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>机台表读取测试：两个工作表，活动表指向非机台表，验证自动定位。</summary>
    public class ExcelMachineReaderTests
    {
        private static string CreateTempWorkbook()
        {
            string path = Path.Combine(Path.GetTempPath(), "uncad_machine_" + Guid.NewGuid().ToString("N") + ".xlsx");
            var wb = new XSSFWorkbook();

            // Sheet1 = 机台表（表头含"机台ID"）
            var ws = wb.CreateSheet("机台数据");
            var hdr = ws.CreateRow(0);
            string[] cols = { "所属区域", "机台ID", "回路名称", "电缆型号", "FR", "详情",
                "项目序号", "软管直径", "NEXT", "", "下游轴位", "上游轴位" };
            for (int c = 0; c < cols.Length; c++) hdr.CreateCell(c).SetCellValue(cols[c]);

            string[,] data = {
                { "LITHO", "MPAMT01", "AC Power box (main1)", "ZB-YJV-3*35+1*16", "-", "U208 3P4W 3P100A", "5", "38" },
                { "LITHO", "MPAMT01", "THC", "ZB-YJV-3*70+1*35", "-", "U208 3P4W 3P200A", "5", "51" },
                { "LITHO", "MPAMT01", "Machine Side", "ZB-YJVR-3*2.5", "-", "U220 1P3W 1P20A", "5", "20" },
                { "Track", "MPAMT09", "THC", "ZB-YJV-3*70+1*35", "-", "3P4W 3P200A", "1086", "51" }
            };
            for (int r = 0; r < data.GetLength(0); r++)
            {
                var row = ws.CreateRow(r + 1);
                for (int c = 0; c < data.GetLength(1); c++) row.CreateCell(c).SetCellValue(data[r, c]);
                row.CreateCell(8).SetCellValue(r == 0 ? "I-Line盘" : "-");
                row.CreateCell(10).SetCellValue((r + 2) + "/T");
                row.CreateCell(11).SetCellValue("化学实验室");
            }

            // Sheet2 = 无关表，且设为活动表（验证不依赖活动表）
            var junk = wb.CreateSheet("材料清单");
            junk.CreateRow(0).CreateCell(0).SetCellValue("编号");
            junk.CreateRow(1).CreateCell(0).SetCellValue("电缆");
            wb.SetActiveSheet(1); // 活动表指向非机台表，验证自动定位不依赖活动表

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                wb.Write(fs);
            return path;
        }

        private static string CreateBoundHeaderWorkbook(bool includeNext)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad_headers_" + Guid.NewGuid().ToString("N") + ".xlsx");
            var wb = new XSSFWorkbook();
            var ws = wb.CreateSheet("动态机台数据");
            string[] headers = includeNext
                ? new[] { "详情", "上游轴位", "机台ID", "软管直径", "所属区域",
                    "FR", "回路名称", "下游轴位", "NEXT", "项目序号", "电缆型号" }
                : new[] { "详情", "上游轴位", "机台ID", "软管直径", "所属区域",
                    "FR", "回路名称", "下游轴位", "项目序号", "电缆型号" };
            var header = ws.CreateRow(0);
            for (int c = 0; c < headers.Length; c++) header.CreateCell(c).SetCellValue(headers[c]);
            var row = ws.CreateRow(1);
            for (int c = 0; c < headers.Length; c++)
            {
                string value;
                switch (headers[c])
                {
                    case "详情": value = "U220 1P3W 1P20A"; break;
                    case "上游轴位": value = "化学实验室"; break;
                    case "机台ID": value = "HEADER01"; break;
                    case "软管直径": value = "20"; break;
                    case "所属区域": value = "LAB"; break;
                    case "FR": value = "3U-UPS28D1-PP-03"; break;
                    case "回路名称": value = "电脑插座"; break;
                    case "下游轴位": value = "2/T"; break;
                    case "NEXT": value = "插座盘"; break;
                    case "项目序号": value = "236"; break;
                    default: value = "ZB-YJVR-3*2.5"; break;
                }
                row.CreateCell(c).SetCellValue(value);
            }
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                wb.Write(stream);
            wb.Close();
            return path;
        }

        [Fact]
        public void FindRows_BindsReorderedColumnsByHeader()
        {
            string path = CreateBoundHeaderWorkbook(true);
            try
            {
                MachineRow row = Assert.Single(ExcelMachineReader.FindRows(path, "HEADER01"));
                Assert.Equal("电脑插座", row.CircuitName);
                Assert.Equal("插座盘", row.Next);
                Assert.Equal("3U-UPS28D1-PP-03", row.Fr);
                Assert.Equal("2/T", row.DownstreamAxis);
                Assert.Equal("化学实验室", row.UpstreamAxis);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void FindRows_ReportsMissingRequiredHeader()
        {
            string path = CreateBoundHeaderWorkbook(false);
            try
            {
                var ex = Assert.Throws<InvalidDataException>(() =>
                    ExcelMachineReader.FindRows(path, "HEADER01"));
                Assert.Contains("NEXT", ex.Message);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void FindRows_ByMachineId_ReturnsAllCircuits()
        {
            string path = CreateTempWorkbook();
            try
            {
                var rows = ExcelMachineReader.FindRows(path, "MPAMT01");
                Assert.Equal(3, rows.Count);
                Assert.All(rows, r => Assert.Equal("MPAMT01", r.MachineId));
                Assert.Contains(rows, r => r.CircuitName == "THC" && r.Cable == "ZB-YJV-3*70+1*35" && r.Dia == "51");
                var first = rows.First(r => r.CircuitName == "AC Power box (main1)");
                Assert.Equal("I-Line盘", first.Next);
                Assert.Equal("2/T", first.DownstreamAxis);
                Assert.Equal("化学实验室", first.UpstreamAxis);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void FindRows_ByName_WhenNoMachineMatch()
        {
            string path = CreateTempWorkbook();
            try
            {
                // 没有叫 THX 的机台 → 按回路名称包含匹配
                var rows = ExcelMachineReader.FindRows(path, "THC");
                Assert.Equal(2, rows.Count); // MPAMT01-THC + MPAMT09-THC
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void FindRows_NotFound_ReturnsEmpty()
        {
            string path = CreateTempWorkbook();
            try
            {
                Assert.Empty(ExcelMachineReader.FindRows(path, "NO_SUCH_MACHINE_XYZ"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
