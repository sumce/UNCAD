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

        [Fact]
        public void Rollback_AfterCompleteKeepsCompletedWriteAndReportsNothing()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_completed_" + Guid.NewGuid().ToString("N"));
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
                    batch.Complete();

                    // Complete() 已删除备份。此时再回滚不得伪造"无法完整恢复"，
                    // 也不得用已删除的备份覆盖已经提交的输出。
                    batch.Rollback();
                    batch.Rollback();
                }

                Assert.Equal("uncad", File.ReadAllText(path));
                Assert.Empty(Directory.GetFiles(folder, "*.bak"));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Rollback_RestoresOnlyOnceAndStaysQuietOnRepeat()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_repeat_" + Guid.NewGuid().ToString("N"));
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
                    batch.Rollback();
                    // 第二次回滚必须是空操作：第一次已经恢复并删除备份。
                    batch.Rollback();
                }

                Assert.Equal("original", File.ReadAllText(path));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void BeginWrite_ProtectsAPathThatWasNotInTheInitialBatch()
        {
            // U1U 批量用计划侧机台号建批次，实际写盘路径来自 CAD 读出的机台号；
            // 两者文本可能不同（D-021 只差内部空白）。这种路径必须按需纳入备份与锁，
            // 而不是抛"文件不属于当前回滚批次"。
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_late_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string a = Path.Combine(folder, "a.txt");
            string late = Path.Combine(folder, "late", "b.txt");
            try
            {
                File.WriteAllText(a, "a-old");
                using (var batch = new FileBatchRollback(new[] { a }))
                {
                    batch.BeginWrite(late);                 // 批次里没有这个路径
                    File.WriteAllText(late, "b-new");
                    batch.MarkWritten(late);
                    batch.Rollback();
                }

                Assert.Equal("a-old", File.ReadAllText(a));
                Assert.False(File.Exists(late));            // 写入前不存在 → 回滚后删除
                // 目录不删：回滚只负责文件，删目录可能误伤用户目录或同目录其它文件。
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void BeginWrite_RestoresALatePathThatExistedBefore()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_late_existing_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string a = Path.Combine(folder, "a.txt");
            string late = Path.Combine(folder, "b.txt");
            try
            {
                File.WriteAllText(a, "a-old");
                File.WriteAllText(late, "b-old");
                using (var batch = new FileBatchRollback(new[] { a }))
                {
                    batch.BeginWrite(late);
                    File.WriteAllText(late, "b-written");
                    batch.MarkWritten(late);
                    batch.Rollback();
                }

                Assert.Equal("a-old", File.ReadAllText(a));
                Assert.Equal("b-old", File.ReadAllText(late));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Complete_CreatesMissingParentFoldersForItsEntries()
        {
            // 批次自己负责建目录：预期文件集现在不再提前建目录。
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_folder_" + Guid.NewGuid().ToString("N"));
            string target = Path.Combine(folder, "machine", "out.txt");
            try
            {
                using (var batch = new FileBatchRollback(new[] { target }))
                {
                    batch.BeginWrite(target);
                    File.WriteAllText(target, "written");
                    batch.MarkWritten(target);
                    batch.Complete();
                }

                Assert.True(File.Exists(target));
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
