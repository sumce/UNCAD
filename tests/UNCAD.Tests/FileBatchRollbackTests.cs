using System;
using System.IO;
using System.Security.Cryptography;
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
                    batch.BeginWrite(path);
                    SubmissionWorkbookWriter.Upsert(path, Record("批次修改"),
                        DateTimeOffset.UtcNow.AddMinutes(1));
                    batch.MarkWritten(path);
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

        [Fact]
        public void Rollback_DoesNotTouchTargetWhoseWriteNeverStarted()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_untouched_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "target.txt");
            try
            {
                File.WriteAllText(path, "original");
                using (var batch = new FileBatchRollback(new[] { path }))
                {
                    File.WriteAllText(path, "external");
                    batch.Rollback();
                }

                Assert.Equal("external", File.ReadAllText(path));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void BeginWrite_RejectsChangeMadeAfterInitialSnapshot()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_initial_conflict_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "target.txt");
            try
            {
                File.WriteAllText(path, "original");
                using (var batch = new FileBatchRollback(new[] { path }))
                {
                    File.WriteAllText(path, "external");

                    IOException error = Assert.Throws<IOException>(() => batch.BeginWrite(path));
                    Assert.Contains("批次准备后被外部修改", error.Message);
                    batch.Rollback();
                }

                Assert.Equal("external", File.ReadAllText(path));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Rollback_DoesNotOverwriteExternalChangeAfterCompletedWrite()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_conflict_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "target.txt");
            try
            {
                File.WriteAllText(path, "original");
                using (var batch = new FileBatchRollback(new[] { path }))
                {
                    batch.BeginWrite(path);
                    File.WriteAllText(path, "uncad");
                    batch.MarkWritten(path);
                    File.WriteAllText(path, "external");

                    AggregateException error = Assert.Throws<AggregateException>(
                        () => batch.Rollback());
                    Assert.Contains("外部修改", error.ToString());
                }

                Assert.Equal("external", File.ReadAllText(path));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Rollback_DoesNotAdoptExternalChangeMadeBeforeWriterHandoff()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_handoff_conflict_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "target.txt");
            try
            {
                File.WriteAllText(path, "original");
                using (var batch = new FileBatchRollback(new[] { path }))
                {
                    batch.BeginWrite(path);
                    File.WriteAllText(path, "uncad");
                    byte[] writerFingerprint = Fingerprint(path);
                    File.WriteAllText(path, "external");
                    batch.MarkWritten(path, writerFingerprint);

                    Assert.Throws<AggregateException>(() => batch.Rollback());
                }

                Assert.Equal("external", File.ReadAllText(path));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        private static byte[] Fingerprint(string path)
        {
            using (var stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
                return sha256.ComputeHash(stream);
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
