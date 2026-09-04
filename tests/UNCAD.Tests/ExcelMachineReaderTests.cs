using System;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>机台表读取测试：按统一 U_ 列和回路名称列绑定，不依赖活动表。</summary>
    public class ExcelMachineReaderTests
    {
        private static string CreateTempWorkbook()
        {
            string path = Path.Combine(Path.GetTempPath(), "uncad_machine_" + Guid.NewGuid().ToString("N") + ".xlsx");
            var wb = new XSSFWorkbook();

            var ws = wb.CreateSheet("机台数据");
            var hdr = ws.CreateRow(0);
            string[] cols = { "U_区域", "U_机台ID", "回路名称", "U_电缆型号", "U_上游编号", "U_配电信息",
                "U_序号", "U_软管直径", "U_上游类型", "U_设备楼层", "U_设备轴位", "U_上游轴位",
                "U_上游楼层", "U_厂务开关" };
            for (int c = 0; c < cols.Length; c++) hdr.CreateCell(c).SetCellValue(cols[c]);

            string[,] data = {
                { "LITHO", "MPAMT01", "AC Power box (main1)", "ZB-YJV-3*35+1*16", "FR01", "U208 3P4W 3P100A", "5", "38", "I-Line盘", "", "2/T", "化学实验室", "", "3P100A" },
                { "LITHO", "MPAMT01", "THC", "ZB-YJV-3*70+1*35", "FR01", "U208 3P4W 3P200A", "5", "51", "I-Line盘", "", "2/T", "化学实验室", "", "3P200A" },
                { "LITHO", "MPAMT01", "Machine Side", "ZB-YJVR-3*2.5", "FR01", "U220 1P3W 1P20A", "5", "20", "插座盘", "", "2/T", "化学实验室", "", "1P20A" },
                { "Track", "MPAMT09", "THC", "ZB-YJV-3*70+1*35", "FR09", "3P4W 3P200A", "1086", "51", "I-Line盘", "", "2/T", "化学实验室", "", "3P200A" }
            };
            for (int r = 0; r < data.GetLength(0); r++)
            {
                var row = ws.CreateRow(r + 1);
                for (int c = 0; c < data.GetLength(1); c++) row.CreateCell(c).SetCellValue(data[r, c]);
            }

            // Sheet2 = 无关表，且设为活动表（验证不依赖活动表）
            var junk = wb.CreateSheet("材料清单");
            junk.CreateRow(0).CreateCell(0).SetCellValue("编号");
            junk.CreateRow(1).CreateCell(0).SetCellValue("电缆");
            wb.SetActiveSheet(1); // 活动表指向非机台表，读取仍固定使用会勘总表

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                wb.Write(fs);
            return path;
        }

        private static string CreateBoundHeaderWorkbook(bool includeNext,
            string upstreamFloorHeader = "U_上游楼层")
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad_headers_" + Guid.NewGuid().ToString("N") + ".xlsx");
            var wb = new XSSFWorkbook();
            var ws = wb.CreateSheet("机台数据");
            string[] headers = includeNext
                ? new[] { "U_配电信息", "U_上游轴位", "U_机台ID", "U_软管直径", "U_区域",
                    "U_上游编号", "回路名称", "U_设备轴位", "U_上游类型", "U_序号", "U_电缆型号",
                    "U_设备楼层", upstreamFloorHeader, "U_厂务开关" }
                : new[] { "U_配电信息", "U_上游轴位", "U_机台ID", "U_软管直径", "U_区域",
                    "U_上游编号", "回路名称", "U_设备轴位", "U_序号", "U_电缆型号",
                    "U_设备楼层", upstreamFloorHeader, "U_厂务开关" };
            var header = ws.CreateRow(0);
            for (int c = 0; c < headers.Length; c++) header.CreateCell(c).SetCellValue(headers[c]);
            var row = ws.CreateRow(1);
            for (int c = 0; c < headers.Length; c++)
            {
                string value;
                switch (headers[c])
                {
                    case "U_配电信息": value = "U220 1P3W 1P20A"; break;
                    case "U_上游轴位": value = "化学实验室"; break;
                    case "U_机台ID": value = "HEADER01"; break;
                    case "U_软管直径": value = "20"; break;
                    case "U_区域": value = "LAB"; break;
                    case "U_上游编号": value = "3U-UPS28D1-PP-03"; break;
                    case "回路名称": value = "电脑插座"; break;
                    case "U_设备轴位": value = "2/T"; break;
                    case "U_上游类型": value = "插座盘"; break;
                    case "U_序号": value = "236"; break;
                    case "U_设备楼层": value = "3F"; break;
                    case "U_上游楼层": value = "2F"; break;
                    case "U_厂务开关": value = "1P20A"; break;
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
                Assert.Equal("3F", row.DeviceFloor);
                Assert.Equal("2F", row.PanelFloor);
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
                Assert.Contains("U_上游类型", ex.Message);
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
                Assert.Equal("", first.DeviceFloor);
                Assert.Equal("", first.PanelFloor);
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

        [Fact]
        public void FindRows_AcceptsUpstreamFloorHeader()
        {
            string path = CreateBoundHeaderWorkbook(true, "U_上游楼层");
            try
            {
                MachineRow row = Assert.Single(ExcelMachineReader.FindRows(path, "HEADER01"));
                Assert.Equal("3F", row.DeviceFloor);
                Assert.Equal("2F", row.PanelFloor);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void ReadAll_BindsHouYunLedgerLayoutWithoutSurveySheetName()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("机台需求+进度表1");
            string[] headers = { "区域", "机台名称", "回路名称", "整体进度", "设备\n位置",
                "柱位号", "电压", "相线", "机台\n开关", "厂务开关", "盘柜或母线位置",
                "盘柜或母线点位", "电缆规格" };
            var header = sheet.CreateRow(0);
            for (int column = 0; column < headers.Length; column++)
                header.CreateCell(column).SetCellValue(headers[column]);
            var row = sheet.CreateRow(1);
            string[] values = { "ETCH", "MEOXT15", "Ac Rack 1-1", "1", "2F", "54W",
                "N208V", "3P4W", "3P350A", "3P400A", "2F 54X", "2N-11C2A-116",
                "3*(2*70)+1*70" };
            for (int column = 0; column < values.Length; column++)
                row.CreateCell(column).SetCellValue(values[column]);
            // A machine-only placeholder must not become a BOQ/circuit row.
            var placeholder = sheet.CreateRow(2);
            placeholder.CreateCell(0).SetCellValue("M-PLACEHOLDER");
            placeholder.CreateCell(1).SetCellValue("ETCH");
            placeholder.CreateCell(2).SetCellValue("");
            placeholder.CreateCell(4).SetCellValue("2F");
            placeholder.CreateCell(5).SetCellValue("54W");
            placeholder.CreateCell(6).SetCellValue("N208V");
            placeholder.CreateCell(7).SetCellValue("3P4W");
            placeholder.CreateCell(8).SetCellValue("3P20A");
            placeholder.CreateCell(9).SetCellValue("3P20A");
            placeholder.CreateCell(10).SetCellValue("2F 54X");
            placeholder.CreateCell(11).SetCellValue("2N-11C2A-116");
            placeholder.CreateCell(12).SetCellValue("3*2.5");

            try
            {
                Assert.Throws<InvalidDataException>(() => ExcelMachineReader.ReadAll(workbook));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void ReadAll_RejectsRowsWhenUnifiedMachineIdFormulaCacheIsMissing()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("机台需求+进度表1");
            string[] legacy = { "区域", "机台名称", "回路名称", "设备\n位置", "柱位号",
                "电压", "相线", "机台\n开关", "厂务开关", "盘柜或母线位置",
                "盘柜或母线点位", "电缆规格" };
            string[] unified = { "U_区域", "U_机台ID", "U_设备楼层", "U_设备轴位",
                "U_上游编号", "U_上游楼层", "U_上游轴位", "U_配电信息",
                "U_电缆型号", "U_上游类型" };
            var header = sheet.CreateRow(0);
            for (int i = 0; i < legacy.Length; i++) header.CreateCell(i).SetCellValue(legacy[i]);
            for (int i = 0; i < unified.Length; i++) header.CreateCell(legacy.Length + i).SetCellValue(unified[i]);
            var row = sheet.CreateRow(1);
            string[] values = { "ETCH", "M01", "回路1", "2F", "54W", "N208V", "3P4W",
                "3P350A", "3P400A", "2F 54X", "2N-11C2A-116", "3*2.5" };
            for (int i = 0; i < values.Length; i++) row.CreateCell(i).SetCellValue(values[i]);
            // Formula cells intentionally have no cached result in a freshly-created
            // NPOI workbook, which reproduces a WPS/Excel save without recalculation.
            for (int i = 0; i < unified.Length; i++) row.CreateCell(legacy.Length + i).SetCellFormula("\"\"");

            try
            {
                InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                    ExcelMachineReader.ReadAll(workbook));
                Assert.Contains("U_机台ID", error.Message);
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void ReadAll_LegacyLayoutArgumentDoesNotDisableAutomaticDetection()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("机台需求+进度表1");
            string[] headers = { "区域", "机台名称", "回路名称", "设备/位置",
                "柱位号", "盘柜或母线位置", "电缆规格", "电压", "相线", "厂务开关" };
            var header = sheet.CreateRow(0);
            for (int column = 0; column < headers.Length; column++)
                header.CreateCell(column).SetCellValue(headers[column]);
            var row = sheet.CreateRow(1);
            string[] values = { "ETCH", "M01", "回路1", "2F", "54W", "2F 54X",
                "3*2.5", "U220", "1P3W", "1P20A" };
            for (int column = 0; column < values.Length; column++)
                row.CreateCell(column).SetCellValue(values[column]);
            try
            {
                Assert.Throws<InvalidDataException>(() =>
                    ExcelMachineReader.ReadAll(workbook));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void ReadAll_BindsA2CompactUColumns()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("U数据");
            string[] headers = { "U_区域", "U_机台ID", "U_设备楼层", "U_设备轴位",
                "U_上游编号", "U_上游楼层", "U_上游轴位", "U_配电信息",
                "U_电缆型号", "U_上游类型" };
            string[] values = { "ETCH", "MEOXT15", "2F", "54/W", "2N-11C2A-116",
                "2F", "54/X", "N208V 3P4W 3P400A", "3*(2*70)+1*70", "母线插接口" };
            var header = sheet.CreateRow(0);
            var row = sheet.CreateRow(1);
            for (int column = 0; column < headers.Length; column++)
            {
                header.CreateCell(column).SetCellValue(headers[column]);
                row.CreateCell(column).SetCellValue(values[column]);
            }
            try
            {
                Assert.Throws<InvalidDataException>(() => ExcelMachineReader.ReadAll(workbook));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void ReadAll_BindsHouYunWorkbookWithDerivedUColumnsAtEnd()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("机台需求+进度表1");
            string[] legacy = { "机台名称", "区域", "回路名称", "设备\n位置", "柱位号",
                "电压", "相线", "机台\n开关", "厂务开关", "盘柜或母线位置",
                "盘柜或母线点位", "电缆规格" };
            string[] unified = { "U_区域", "U_机台ID", "U_设备楼层", "U_设备轴位",
                "U_上游编号", "U_上游楼层", "U_上游轴位", "U_配电信息",
                "U_电缆型号", "U_上游类型", "U_厂务开关" };
            var header = sheet.CreateRow(0);
            for (int i = 0; i < legacy.Length; i++) header.CreateCell(i).SetCellValue(legacy[i]);
            for (int i = 0; i < unified.Length; i++) header.CreateCell(49 + i).SetCellValue(unified[i]);

            var row = sheet.CreateRow(1);
            string[] legacyValues = { "OLD-ID", "OLD-REGION", "旧回路", "1F", "1/A", "U220",
                "1P3W", "1P16A", "1P20A", "1F 2/B", "OLD-FR", "OLD-CABLE" };
            for (int i = 0; i < legacyValues.Length; i++) row.CreateCell(i).SetCellValue(legacyValues[i]);
            string[] unifiedValues = { "ETCH", "MEOXT15", "2F", "54/W", "2N-11C2A-116",
                "2F", "54/X", "N208V 3P4W 3P400A", "3*(2*70)+1*70", "母线插接口", "3P400A" };
            for (int i = 0; i < unifiedValues.Length; i++) row.CreateCell(49 + i).SetCellValue(unifiedValues[i]);

            try
            {
                MachineRow machine = Assert.Single(ExcelMachineReader.ReadAll(workbook));
                Assert.Equal("MEOXT15", machine.MachineId);
                Assert.Equal("ETCH", machine.Region);
                Assert.Equal("2F", machine.DeviceFloor);
                Assert.Equal("54/W", machine.DownstreamAxis);
                Assert.Equal("2N-11C2A-116", machine.Fr);
                Assert.Equal("2F", machine.PanelFloor);
                Assert.Equal("54/X", machine.UpstreamAxis);
                Assert.Equal("N208V 3P4W 3P400A", machine.Detail);
                Assert.Equal("3*(2*70)+1*70", machine.Cable);
                Assert.Equal("母线插接口", machine.Next);
                Assert.Equal("3P400A", machine.FacilitySwitch);
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void ReadAll_ReadsOnlyTheBoundColumnsWithoutExpandingMergedCells()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("机台数据");
            string[] headers = { "U_区域", "U_机台ID", "U_设备楼层", "U_设备轴位",
                "U_上游编号", "U_上游楼层", "U_上游轴位", "U_配电信息",
                "U_电缆型号", "U_上游类型", "U_厂务开关", "回路名称" };
            string[] shared = { "ETCH", "M01", "2F", "54/W", "UP-01", "1F", "54/X",
                "N208V 3P4W 3P400A", "3*2.5", "母线插接口", "3P400A" };
            IRow header = sheet.CreateRow(0);
            IRow first = sheet.CreateRow(1);
            IRow second = sheet.CreateRow(2);
            for (int column = 0; column < headers.Length; column++)
                header.CreateCell(column).SetCellValue(headers[column]);
            for (int column = 0; column < shared.Length; column++)
            {
                first.CreateCell(column).SetCellValue(shared[column]);
                sheet.AddMergedRegion(new CellRangeAddress(1, 2, column, column));
            }
            first.CreateCell(11).SetCellValue("回路一");
            second.CreateCell(11).SetCellValue("回路二");

            try
            {
                InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                    ExcelMachineReader.ReadAll(workbook));
                Assert.Contains("U_机台ID", error.Message);
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void ReadAll_RejectsLegacyHeadersWithoutFallback()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("会勘总表");
            string[] headers = { "区域", "机台ID", "回路名称", "电缆型号", "FR", "详情",
                "项目序号", "软管直径", "NEXT", "下游轴位", "上游轴位" };
            var header = sheet.CreateRow(0);
            for (int column = 0; column < headers.Length; column++)
                header.CreateCell(column).SetCellValue(headers[column]);

            var first = sheet.CreateRow(1);
            first.CreateCell(0).SetCellValue("LAB");
            first.CreateCell(1).SetCellValue("M01");
            first.CreateCell(2).SetCellValue("回路1");
            first.CreateCell(3).SetCellValue("CABLE");
            first.CreateCell(4).SetCellValue("FR01");
            first.CreateCell(5).SetCellValue("U220");
            first.CreateCell(6).SetCellValue("1");
            first.CreateCell(7).SetCellValue("20");
            first.CreateCell(8).SetCellValue("插座盘");
            first.CreateCell(9).SetCellValue("1/T");
            first.CreateCell(10).SetCellValue("US01");
            var second = sheet.CreateRow(2);
            second.CreateCell(2).SetCellValue("回路2");
            second.CreateCell(3).SetCellValue("CABLE");
            second.CreateCell(5).SetCellValue("U220");
            second.CreateCell(6).SetCellValue("2");
            second.CreateCell(7).SetCellValue("");
            second.CreateCell(8).SetCellValue("插座盘");
            second.CreateCell(9).SetCellValue("2/T");
            second.CreateCell(10).SetCellValue("US01");
            try
            {
                Assert.Throws<InvalidDataException>(() => ExcelMachineReader.ReadAll(workbook));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void ReadAll_SkipsCircuitNamesWithCellOrRichTextStrikeout()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("会勘总表");
            string[] headers = { "区域", "机台ID", "回路名称", "电缆型号", "FR", "详情",
                "项目序号", "软管直径", "NEXT", "下游轴位", "上游轴位" };
            var header = sheet.CreateRow(0);
            for (int column = 0; column < headers.Length; column++)
                header.CreateCell(column).SetCellValue(headers[column]);

            var strikeFont = workbook.CreateFont();
            strikeFont.IsStrikeout = true;
            for (int rowIndex = 1; rowIndex <= 3; rowIndex++)
            {
                var row = sheet.CreateRow(rowIndex);
                string[] values = { "LAB", "M0" + rowIndex, "正常回路", "CABLE", "FR01",
                    "U220", rowIndex.ToString(), "20", "插座盘", "1/T", "US01" };
                for (int column = 0; column < values.Length; column++)
                    row.CreateCell(column).SetCellValue(values[column]);
            }
            var strikeStyle = workbook.CreateCellStyle();
            strikeStyle.SetFont(strikeFont);
            sheet.GetRow(2).GetCell(2).CellStyle = strikeStyle;
            var richText = new XSSFRichTextString("部分删除回路");
            richText.ApplyFont(0, 2, strikeFont);
            sheet.GetRow(3).GetCell(2).SetCellValue(richText);

            try
            {
                Assert.Throws<InvalidDataException>(() => ExcelMachineReader.ReadAll(workbook));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void FindRows_BindsOnlineSurveyWorkbookLayout()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad_online_" + Guid.NewGuid().ToString("N") + ".xlsx");
            var workbook = new XSSFWorkbook();
            var incomplete = workbook.CreateSheet("其他机台表").CreateRow(0);
            incomplete.CreateCell(0).SetCellValue("机台ID");
            incomplete.CreateCell(1).SetCellValue("回路名称");
            var sheet = workbook.CreateSheet("会勘总表");
            string[] headers = { "序号", "批次", "标红优先做", "所属区域", "机台ID",
                "回路名称", "点位状态", "楼层", "轴位", "电压", "点位型号", "机台开关",
                "厂务开关", "tack off点", "验证", "插拔开关位置", "电缆型号", "电缆长度",
                "备注", "送电时间", "ISO图", "已审未交", "软管直径", "NEXT", "机台ID",
                "回路名称", "电压/开关", "电缆型号", "FR", "", "区域", "机台ID",
                "电压", "上游编号", "上游轴位", "上游轴位", "" };
            var header = sheet.CreateRow(0);
            for (int index = 0; index < headers.Length; index++)
                header.CreateCell(index).SetCellValue(headers[index]);
            var row = sheet.CreateRow(1);
            row.CreateCell(0).SetCellValue("236");
            row.CreateCell(4).SetCellValue("原始ID");
            row.CreateCell(5).SetCellValue("中央实验台1-1");
            row.CreateCell(7).SetCellValue("1F");
            row.CreateCell(8).SetCellValue("2/T");
            row.CreateCell(9).SetCellValue("U220");
            row.CreateCell(10).SetCellValue("1P3W");
            row.CreateCell(12).SetCellValue("1P20A");
            row.CreateCell(16).SetCellValue("3*2.5");
            row.CreateCell(22).SetCellValue("20");
            row.CreateCell(23).SetCellValue("I-Line盘");
            row.CreateCell(24).SetCellValue("装饰ID");
            row.CreateCell(25).SetCellValue("装饰回路");
            row.CreateCell(27).SetCellValue("ZB-YJVR-3*2.5");
            row.CreateCell(28).SetCellValue("3U-UPS28D1-PP-03(1F 化学实验室)");
            row.CreateCell(30).SetCellValue("1007a");
            row.CreateCell(31).SetCellValue("MQBAN01");
            row.CreateCell(34).SetCellValue("1F 化学实验室");
            row.CreateCell(35).SetCellValue("化学实验室");
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                workbook.Write(stream);
            workbook.Close();

            try
            {
                Assert.Throws<InvalidDataException>(() =>
                    ExcelMachineReader.FindRows(path, "MQBAN01"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void ReadAll_RejectsWorkbookWithoutSurveySheet()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("DATA");
            sheet.CreateRow(0).CreateCell(0).SetCellValue("机台ID");
            try
            {
                var ex = Assert.Throws<InvalidDataException>(() =>
                    ExcelMachineReader.ReadAll(workbook));
                Assert.Contains("U_区域", ex.Message);
                Assert.Contains("不支持旧字段", ex.Message);
            }
            finally { workbook.Close(); }
        }

    }
}
