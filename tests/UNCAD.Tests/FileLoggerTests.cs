using System;
using System.IO;
using System.Text.RegularExpressions;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FileLoggerTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(),
            "uncad-log-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_directory, true); } catch { }
        }

        private string LogPath(DateTime date) => Path.Combine(_directory,
            "uncad-" + date.ToString("yyyyMMdd") + ".log");

        [Fact]
        public void Writes_DailyFileWithLevelCommandAndThread()
        {
            var logger = new FileLogger(_directory, () => "INFO", () => 30);

            logger.Info("开始生成清单");

            string path = LogPath(DateTime.Now);
            Assert.True(File.Exists(path));
            string line = File.ReadAllText(path);
            Assert.Contains("[INFO]", line);
            Assert.Contains("[cmd=-]", line);
            Assert.Contains("开始生成清单", line);
            Assert.Matches(new Regex(@"[t[1-9][0-9]*]"), line);
        }

        [Fact]
        public void LevelFilter_DropsMessagesBelowConfiguredLevel()
        {
            var logger = new FileLogger(_directory, () => "WARN", () => 30);

            logger.Info("不应写入");
            Assert.False(File.Exists(LogPath(DateTime.Now)));

            logger.Warn("写入警告");
            logger.Error("写入错误", new InvalidOperationException("boom"));

            string text = File.ReadAllText(LogPath(DateTime.Now));
            Assert.DoesNotContain("不应写入", text);
            string[] lines = File.ReadAllLines(LogPath(DateTime.Now));
            Assert.Contains(lines, line => line.Contains("[WARN]")
                && line.Contains("写入警告"));
            Assert.Contains(lines, line => line.Contains("[ERROR]")
                && line.Contains("写入错误"));
            Assert.Contains("InvalidOperationException", text);
        }

        [Fact]
        public void CommandScope_TagsLinesAndRestoresNestedOuterScope()
        {
            var logger = new FileLogger(_directory, () => "INFO", () => 30);

            using (Log.Command("U1F"))
            {
                logger.Info("外层");
                using (Log.Command("U1U")) logger.Info("内层");
                logger.Info("回到外层");
            }
            logger.Info("无作用域");

            string[] lines = File.ReadAllLines(LogPath(DateTime.Now));
            Assert.Contains(lines, line => line.Contains("[cmd=U1F]")
                && line.Contains("外层"));
            Assert.Contains(lines, line => line.Contains("[cmd=U1U]")
                && line.Contains("内层"));
            Assert.Contains(lines, line => line.Contains("[cmd=U1F]")
                && line.Contains("回到外层"));
            Assert.Contains(lines, line => line.Contains("[cmd=-]")
                && line.Contains("无作用域"));
        }

        [Fact]
        public void BlankCommand_DoesNotReplaceOuterTag()
        {
            var logger = new FileLogger(_directory, () => "INFO", () => 30);

            using (Log.Command("U1C"))
            using (Log.Command("  "))
                logger.Info("空白标签");

            Assert.Contains(File.ReadAllLines(LogPath(DateTime.Now)),
                line => line.Contains("[cmd=U1C]") && line.Contains("空白标签"));
        }

        [Theory]
        [InlineData("INFO", 0)]
        [InlineData("info", 0)]
        [InlineData("WARN", 1)]
        [InlineData("WARNING", 1)]
        [InlineData("ERROR", 2)]
        [InlineData("error", 2)]
        [InlineData("", 0)]
        [InlineData("bogus", 0)]
        public void ParseLevel_MapsConfigurationValues(string value, int expected)
            => Assert.Equal(expected, FileLogger.ParseLevel(value));

        [Fact]
        public void PurgeOldLogs_DeletesFilesOutsideRetentionWindow()
        {
            Directory.CreateDirectory(_directory);
            string old = Path.Combine(_directory, "uncad-20200101.log");
            string justOutside = Path.Combine(_directory, "uncad-20250930.log");
            string inside = Path.Combine(_directory, "uncad-20261005.log");
            string today = Path.Combine(_directory, "uncad-20261101.log");
            string unrelated = Path.Combine(_directory, "other.log");
            foreach (string file in new[] { old, justOutside, inside, today, unrelated })
                File.WriteAllText(file, "x");
            var logger = new FileLogger(_directory, () => "INFO", () => 30);

            int removed = logger.PurgeOldLogs(new DateTime(2026, 11, 1));

            Assert.Equal(2, removed);
            Assert.False(File.Exists(old));
            Assert.False(File.Exists(justOutside));
            Assert.True(File.Exists(inside));
            Assert.True(File.Exists(today));
            Assert.True(File.Exists(unrelated));
        }

        [Fact]
        public void PurgeOldLogs_InvalidConfiguredDaysFallsBackToDefault()
        {
            Directory.CreateDirectory(_directory);
            string fortyDaysOld = Path.Combine(_directory, "uncad-20260901.log");
            File.WriteAllText(fortyDaysOld, "x");
            var logger = new FileLogger(_directory, () => "INFO", () => 0);

            int removed = logger.PurgeOldLogs(new DateTime(2026, 11, 1));

            Assert.Equal(1, removed);
            Assert.False(File.Exists(fortyDaysOld));
        }
    }
}
