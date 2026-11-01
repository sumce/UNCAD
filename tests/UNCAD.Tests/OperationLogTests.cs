using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class OperationLogTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(),
            "uncad-history-tests", Guid.NewGuid().ToString("N"));

        public OperationLogTests()
        {
            OperationLog.ResetDirectoryForTests();
            OperationLog.SetDirectoryForTests(_directory);
            OperationLog.SetRetentionMonthsForTests(6);
        }

        public void Dispose()
        {
            OperationLog.ResetDirectoryForTests();
            try { Directory.Delete(_directory, true); } catch { }
        }

        private string CurrentFile => Path.Combine(_directory,
            OperationLog.FileNameFor(DateTime.Now));

        [Fact]
        public void Complete_WritesOneJsonLineWithFixedFields()
        {
            using (OperationLogScope scope = OperationLog.Begin("U1F", "生成清单"))
            {
                scope.Machine("M-01", "回路1");
                scope.Machine("M-01", "回路2");
                scope.Machine("M-02", "回路1");
                scope.Machine("M-01", "回路1");
                scope.Output("BOQ", @"C:\out\[BOQ]M-01.xlsx");
                scope.Output("BOQ", @"C:\out\[BOQ]M-01.xlsx");
                scope.Complete("写入 3 行");
            }

            Assert.True(File.Exists(CurrentFile));
            Assert.Single(File.ReadAllLines(CurrentFile));
            OperationLogEntry entry = Assert.Single(OperationLog.ReadRecent(10));
            Assert.Equal("U1F", entry.Command);
            Assert.Equal("生成清单", entry.Summary);
            Assert.Equal("SUCCESS", entry.Result);
            Assert.Equal("写入 3 行", entry.Detail);
            Assert.Equal(3, entry.MachineCount);
            Assert.Single(entry.OutputPaths);
            Assert.StartsWith("BOQ: ", entry.OutputPaths[0]);
            Assert.True(entry.DurationMs >= 0);
            Assert.True(entry.ThreadId > 0);
            Assert.True((DateTimeOffset.Now - entry.Timestamp).Duration()
                < TimeSpan.FromMinutes(1));
        }

        [Fact]
        public void DisposeWithoutTerminalCall_StillRecordsOneEntry()
        {
            using (OperationLog.Begin("U1L", "画线")) { }

            OperationLogEntry entry = Assert.Single(OperationLog.ReadRecent(10));
            Assert.Equal("U1L", entry.Command);
            Assert.Equal("SUCCESS", entry.Result);
            Assert.Single(File.ReadAllLines(CurrentFile));
        }

        [Fact]
        public void Fail_RecordsFailureDetailAndExceptionMessage()
        {
            using (OperationLogScope scope = OperationLog.Begin("U1U", "更新清单"))
            {
                scope.Fail("写入 CAD 表格失败",
                    new InvalidOperationException("目标表格已损坏"));
                scope.Complete("重复调用不应覆盖失败");
            }

            OperationLogEntry entry = Assert.Single(OperationLog.ReadRecent(10));
            Assert.Equal("FAILED", entry.Result);
            Assert.Contains("写入 CAD 表格失败", entry.Detail);
            Assert.Contains("InvalidOperationException: 目标表格已损坏", entry.Detail);
        }

        [Fact]
        public void Cancel_RecordsCancelledResultAndDetail()
        {
            using (OperationLogScope scope = OperationLog.Begin("U1L", "画线"))
                scope.Cancel("用户取消（ESC）");

            OperationLogEntry entry = Assert.Single(OperationLog.ReadRecent(10));
            Assert.Equal("CANCELLED", entry.Result);
            Assert.Contains("用户取消", entry.Detail);
        }

        [Fact]
        public void ReadRecent_ReturnsNewestFirstAndHonorsMax()
        {
            foreach (string command in new[] { "U1L", "U1Q1", "U1C" })
                using (OperationLogScope scope = OperationLog.Begin(command))
                    scope.Complete();

            IReadOnlyList<OperationLogEntry> two = OperationLog.ReadRecent(2);
            Assert.Equal(2, two.Count);
            Assert.Equal("U1C", two[0].Command);
            Assert.Equal("U1Q1", two[1].Command);
            Assert.Equal(3, OperationLog.ReadRecent(200).Count);
        }

        [Fact]
        public void ConcurrentWrites_ProduceOneValidJsonLineEach()
        {
            const int count = 40;
            Parallel.For(0, count, index =>
            {
                using (OperationLogScope scope = OperationLog.Begin("U1F", "并发" + index))
                {
                    scope.Machine("M-" + index, "C1");
                    scope.Complete();
                }
            });

            string[] lines = File.ReadAllLines(CurrentFile);
            Assert.Equal(count, lines.Length);
            Assert.Equal(count, OperationLog.ReadRecent(1000).Count);
            Assert.All(lines, line => Assert.StartsWith("{", line));
        }

        [Fact]
        public void PurgeOldHistory_RemovesOnlyFilesOlderThanRetentionMonths()
        {
            Directory.CreateDirectory(_directory);
            string old = Path.Combine(_directory, "operations-202001.jsonl");
            string justOutside = Path.Combine(_directory, "operations-202605.jsonl");
            string inside = Path.Combine(_directory, "operations-202607.jsonl");
            string current = Path.Combine(_directory, OperationLog.FileNameFor(
                new DateTime(2026, 11, 1)));
            string unrelated = Path.Combine(_directory, "operations-bad.jsonl");
            foreach (string file in new[] { old, justOutside, inside, current, unrelated })
                File.WriteAllText(file, "{}");

            int removed = OperationLog.PurgeOldHistory(new DateTime(2026, 11, 15));

            Assert.Equal(2, removed);
            Assert.False(File.Exists(old));
            Assert.False(File.Exists(justOutside));
            Assert.True(File.Exists(inside));
            Assert.True(File.Exists(current));
            Assert.True(File.Exists(unrelated));
        }

        [Fact]
        public void ReadRecent_SkipsCorruptedLines()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(CurrentFile,
                "not-json" + Environment.NewLine
                + "{\"command\":\"U1R\",\"summary\":\"\",\"result\":\"SUCCESS\","
                + "\"detail\":\"\",\"machineCount\":0,\"outputPaths\":[],"
                + "\"durationMs\":1,\"threadId\":1,"
                + "\"timestamp\":\"2026-11-01T10:00:00.0000000+08:00\"}"
                + Environment.NewLine);

            OperationLogEntry entry = Assert.Single(OperationLog.ReadRecent(10));
            Assert.Equal("U1R", entry.Command);
        }

        [Fact]
        public void WriteFailure_DoesNotThrowOrBreakTheCommand()
        {
            string blocked = Path.Combine(_directory, "blocked");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(blocked, "not a directory");
            OperationLog.SetDirectoryForTests(blocked);

            using (OperationLogScope scope = OperationLog.Begin("U1F", "写失败"))
                scope.Complete();

            Assert.Empty(OperationLog.ReadRecent(10));
        }
    }
}
