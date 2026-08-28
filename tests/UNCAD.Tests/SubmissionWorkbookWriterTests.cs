using System;
using System.IO;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Submission;
using Xunit;

namespace UNCAD.Tests
{
    public class SubmissionWorkbookWriterTests
    {
        [Fact]
        public void Upsert_ReplacesSameMachineDeviceAndPreservesFirstSubmissionTime()
        {
            string folder = Path.Combine(Path.GetTempPath(), "uncad_submit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                var first = Record("M01", "设备A", "旧详情");
                SubmissionWriteResult created = SubmissionWorkbookWriter.Upsert(path, first,
                    new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
                Assert.False(created.ReplacedExisting);

                var latest = Record("M01", "设备A", "新详情");
                latest.PanelType = "插座盘";
                SubmissionWriteResult updated = SubmissionWorkbookWriter.Upsert(path, latest,
                    new DateTimeOffset(2026, 8, 2, 11, 30, 0, TimeSpan.Zero));
                Assert.True(updated.ReplacedExisting);
                Assert.Equal(1, updated.RemovedDuplicates);
                Assert.Equal(created.SubmittedAt, updated.SubmittedAt);
                Assert.NotEqual(updated.SubmittedAt, updated.UpdatedAt);

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try
                    {
                        var sheet = workbook.GetSheet(SubmissionWorkbookWriter.SheetName);
                        Assert.Equal(1, sheet.LastRowNum);
                        var row = sheet.GetRow(1);
                        Assert.Equal("M01", row.GetCell(Column(sheet, "机台ID")).StringCellValue);
                        Assert.Equal("设备A", row.GetCell(Column(sheet, "设备名称")).StringCellValue);
                        Assert.Equal("插座盘", row.GetCell(Column(sheet, "盘柜类型")).StringCellValue);
                        Assert.Equal("新详情", row.GetCell(Column(sheet, "配电详情")).StringCellValue);
                        Assert.Equal("16.4", row.GetCell(Column(sheet, "电缆米数")).StringCellValue);
                        Assert.Equal("2", row.GetCell(Column(sheet, "软管米数")).StringCellValue);
                        Assert.Equal("4", row.GetCell(Column(sheet, "桥架米数")).StringCellValue);
                        Assert.Equal("5", row.GetCell(Column(sheet, "线管米数")).StringCellValue);
                        Assert.Equal(created.SubmittedAt, row.GetCell(Column(sheet, "提交时间")).StringCellValue);
                        Assert.Equal(updated.UpdatedAt, row.GetCell(Column(sheet, "更新时间")).StringCellValue);

                        var details = workbook.GetSheet(SubmissionWorkbookWriter.DetailSheetName);
                        Assert.Equal(2, details.LastRowNum);
                        Assert.Equal("电缆", details.GetRow(1)
                            .GetCell(Column(details, "材料名称")).StringCellValue);
                        Assert.Equal("16.4", details.GetRow(1)
                            .GetCell(Column(details, "数量")).StringCellValue);
                    }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Upsert_KeepsDifferentDeviceAsSeparateRecord()
        {
            string folder = Path.Combine(Path.GetTempPath(), "uncad_submit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                DateTimeOffset now = DateTimeOffset.Now;
                SubmissionWorkbookWriter.Upsert(path, Record("M01", "设备A", "A"), now);
                SubmissionWorkbookWriter.Upsert(path, Record("M01", "设备B", "B"), now);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try { Assert.Equal(2, workbook.GetSheet(SubmissionWorkbookWriter.SheetName).LastRowNum); }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Upsert_AppendsNewColumnsToLegacyWorkbook_WithoutLosingRows()
        {
            string folder = Path.Combine(Path.GetTempPath(), "uncad_submit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                var legacyWorkbook = new XSSFWorkbook();
                try
                {
                    var sheet = legacyWorkbook.CreateSheet(SubmissionWorkbookWriter.SheetName);
                    string[] oldHeaders =
                    {
                        "机台ID", "设备名称", "盘柜类型", "电缆型号", "FR", "配电详情",
                        "软管直径", "下游轴位", "上游轴位", "提交时间", "更新时间"
                    };
                    var header = sheet.CreateRow(0);
                    for (int i = 0; i < oldHeaders.Length; i++) header.CreateCell(i).SetCellValue(oldHeaders[i]);
                    var legacy = sheet.CreateRow(1);
                    legacy.CreateCell(0).SetCellValue("OLD");
                    legacy.CreateCell(1).SetCellValue("旧设备");
                    legacy.CreateCell(3).SetCellValue("LEGACY-CABLE");
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                        legacyWorkbook.Write(stream);
                }
                finally { legacyWorkbook.Close(); }

                SubmissionWorkbookWriter.Upsert(path, Record("NEW", "新设备", "详情"), DateTimeOffset.Now);

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try
                    {
                        var sheet = workbook.GetSheet(SubmissionWorkbookWriter.SheetName);
                        Assert.Equal(2, sheet.LastRowNum);
                        Assert.Equal("OLD", sheet.GetRow(1).GetCell(Column(sheet, "机台ID")).StringCellValue);
                        Assert.Equal("旧设备", sheet.GetRow(1).GetCell(Column(sheet, "设备名称")).StringCellValue);
                        foreach (string header in SubmissionWorkbookWriter.Headers)
                            Assert.True(Column(sheet, header) >= 0);
                        Assert.True(Column(sheet, "设备原电缆型号") >= 0);
                        Assert.True(Column(sheet, "清单电缆型号") >= 0);
                        Assert.Equal("LEGACY-CABLE", sheet.GetRow(1)
                            .GetCell(Column(sheet, "设备原电缆型号")).StringCellValue);
                        Assert.Equal("LEGACY-CABLE", sheet.GetRow(1)
                            .GetCell(Column(sheet, "清单电缆型号")).StringCellValue);
                        Assert.Equal("ZB-YJVR-3*2.5", sheet.GetRow(2)
                            .GetCell(Column(sheet, "电缆型号")).StringCellValue);
                        Assert.Equal("ORIGINAL-CABLE", sheet.GetRow(2)
                            .GetCell(Column(sheet, "设备原电缆型号")).StringCellValue);
                        Assert.Equal("16.4", sheet.GetRow(2).GetCell(Column(sheet, "电缆米数")).StringCellValue);
                        var details = workbook.GetSheet(SubmissionWorkbookWriter.DetailSheetName);
                        Assert.Equal(2, details.LastRowNum);
                        foreach (string header in SubmissionWorkbookWriter.DetailHeaders)
                            Assert.True(Column(details, header) >= 0);
                    }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        private static int Column(NPOI.SS.UserModel.ISheet sheet, string header)
        {
            NPOI.SS.UserModel.IRow row = sheet.GetRow(0);
            for (int column = 0; column < row.LastCellNum; column++)
                if (string.Equals(row.GetCell(column)?.StringCellValue, header,
                    StringComparison.Ordinal)) return column;
            throw new InvalidDataException("Missing header: " + header);
        }

        private static SubmissionRecord Record(string machine, string device, string detail)
            => new SubmissionRecord
            {
                MachineId = machine,
                DeviceName = device,
                PanelType = "I-Line盘",
                OriginalCable = "ORIGINAL-CABLE",
                Cable = "ZB-YJVR-3*2.5",
                CableMeters = "16.4",
                Fr = "FR-01",
                Detail = detail,
                Diameter = "20",
                FlexibleConduitMeters = "2",
                BridgeInfo = "桥架200*100 4M",
                BridgeMeters = "4",
                ConduitInfo = "⌀20线管 5M",
                ConduitMeters = "5",
                DownstreamAxis = "2/T",
                UpstreamAxis = "LAB",
                Materials = new System.Collections.Generic.List<SubmissionMaterial>
                {
                    new SubmissionMaterial
                    {
                        Number = "1", Name = "电缆", Description = "电缆描述",
                        Unit = "M", Quantity = "16.4", Code = "1.1"
                    },
                    new SubmissionMaterial
                    {
                        Number = "2", Name = "包塑金属软管", Description = "软管描述",
                        Unit = "M", Quantity = "2", Code = "3.1"
                    }
                }
            };
    }
}
