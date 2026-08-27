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
                        Assert.Equal("M01", row.GetCell(0).StringCellValue);
                        Assert.Equal("设备A", row.GetCell(1).StringCellValue);
                        Assert.Equal("插座盘", row.GetCell(2).StringCellValue);
                        Assert.Equal("新详情", row.GetCell(5).StringCellValue);
                        Assert.Equal(created.SubmittedAt, row.GetCell(9).StringCellValue);
                        Assert.Equal(updated.UpdatedAt, row.GetCell(10).StringCellValue);
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

        private static SubmissionRecord Record(string machine, string device, string detail)
            => new SubmissionRecord
            {
                MachineId = machine, DeviceName = device, PanelType = "I-Line盘",
                Cable = "ZB-YJVR-3*2.5", Fr = "FR-01", Detail = detail,
                Diameter = "20", DownstreamAxis = "2/T", UpstreamAxis = "LAB"
            };
    }
}
