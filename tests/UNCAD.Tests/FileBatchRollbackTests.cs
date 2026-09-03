using System;
using System.IO;
using UNCAD.Core.Submission;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FileBatchRollbackTests
    {
        [Fact]
        public void Rollback_HoldsReusableWriterLockAndRestoresWorkbook()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_rollback_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                SubmissionWorkbookWriter.Upsert(path, Record("原始"), DateTimeOffset.UtcNow);
                using (var batch = new FileBatchRollback(new[] { path }))
                {
                    SubmissionWorkbookWriter.Upsert(path, Record("批次修改"),
                        DateTimeOffset.UtcNow.AddMinutes(1));
                    batch.Rollback();
                }

                SubmissionWriteResult afterRollback = SubmissionWorkbookWriter.Upsert(path,
                    Record("回滚后"), DateTimeOffset.UtcNow.AddMinutes(2));
                Assert.True(afterRollback.ReplacedExisting);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite))
                {
                    var workbook = new NPOI.XSSF.UserModel.XSSFWorkbook(stream);
                    try
                    {
                        var sheet = workbook.GetSheet(SubmissionWorkbookWriter.SheetName);
                        Assert.Equal(2, sheet.LastRowNum);
                        Assert.Equal("原始", sheet.GetRow(1).GetCell(8).StringCellValue);
                        Assert.Equal("回滚后", sheet.GetRow(2).GetCell(8).StringCellValue);
                    }
                    finally { workbook.Close(); }
                }
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        private static SubmissionRecord Record(string detail)
            => new SubmissionRecord
            {
                MachineId = "M01",
                DeviceName = "设备A",
                Detail = detail
            };
    }
}
