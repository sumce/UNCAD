using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UNCAD.Core.Contracts;

namespace UNCAD.Infra
{
    /// <summary>
    /// 文件日志（ILogger 实现）：%APPDATA%\UNCAD\logs\uncad-YYYYMMDD.log，按天轮转并自动清理过期文件。
    /// 级别过滤与保留天数来自 U1SET 可改的 ConfigKeys.LogLevel / LogRetentionDays；
    /// 每行固定包含时间、级别、命令标签（Log.Command 作用域）和线程号，便于现场按命令/线程排障。
    /// 日志失败绝不影响插件主流程（与旧实现一致，写入异常被吞掉）。
    /// </summary>
    public sealed class FileLogger : ILogger
    {
        public static readonly FileLogger Instance = new FileLogger();

        internal const string DefaultLevel = "INFO";
        internal const int DefaultRetentionDays = 30;
        private const int MaxRetentionDays = 3650;
        private const string FilePrefix = "uncad-";
        private const string FileSuffix = ".log";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // 配置读取会经过 Settings → 注册表实现；注册表异常时它会调用 Log.Warn，
        // 若此时又进入日志写入并再次读配置就会无限递归。用线程级标记截断这一环。
        [ThreadStatic] private static bool _resolvingSettings;
        private readonly object _lock = new object();
        private readonly string _directory;
        private readonly Func<string> _levelProvider;
        private readonly Func<int> _retentionDaysProvider;
        private DateTime _lastPurgeDate = DateTime.MinValue;

        public FileLogger() : this(null, null, null)
        {
        }

        /// <summary>
        /// 测试/工具用构造：目录、级别来源和保留天数来源可注入，避免测试写真实 APPDATA；
        /// 生产路径使用无参构造并通过 Settings 读取 U1SET 配置。
        /// </summary>
        internal FileLogger(string directory, Func<string> levelProvider,
            Func<int> retentionDaysProvider)
        {
            _levelProvider = levelProvider
                ?? (() => Settings.Get(ConfigKeys.LogLevel, DefaultLevel));
            _retentionDaysProvider = retentionDaysProvider
                ?? (() => SafeRetentionDays(Settings.GetDouble(
                    ConfigKeys.LogRetentionDays, DefaultRetentionDays)));
            _directory = ResolveDirectory(directory);
        }

        /// <summary>日志目录；目录不可写时退到临时目录，仍不可用则写日志变成空操作。</summary>
        internal string LogDirectory => _directory ?? "";

        public void Info(string message) => Write(LogSeverity.Info, message, null);
        public void Warn(string message) => Write(LogSeverity.Warn, message, null);
        public void Error(string message, Exception ex = null)
            => Write(LogSeverity.Error, message, ex);

        private void Write(LogSeverity severity, string message, Exception ex)
        {
            if (_directory == null) return;
            try
            {
                if ((int)severity < ParseLevel(ConfiguredLevel())) return;
            }
            catch { /* 配置读取失败时不过滤，宁可多写 */ }

            try
            {
                lock (_lock)
                {
                    DateTime now = DateTime.Now;
                    PurgeOldLogsLocked(now);
                    string path = Path.Combine(_directory,
                        FilePrefix + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                        + FileSuffix);
                    string line = "[" + now.ToString("yyyy-MM-dd HH:mm:ss.fff",
                            CultureInfo.InvariantCulture)
                        + "] [" + ScaleName(severity)
                        + "] [cmd=" + CommandLabel()
                        + "] [t" + Thread.CurrentThread.ManagedThreadId + "] "
                        + (message ?? "")
                        + (ex != null ? " | " + ex : "")
                        + Environment.NewLine;
                    File.AppendAllText(path, line, Utf8NoBom);
                }
            }
            catch { /* 日志失败不影响插件运行 */ }
        }

        private string ConfiguredLevel()
        {
            if (_resolvingSettings) return DefaultLevel;
            try
            {
                _resolvingSettings = true;
                return _levelProvider();
            }
            catch { return DefaultLevel; }
            finally { _resolvingSettings = false; }
        }

        private int ConfiguredRetentionDays()
        {
            if (_resolvingSettings) return DefaultRetentionDays;
            try
            {
                _resolvingSettings = true;
                return SafeRetentionDays(_retentionDaysProvider());
            }
            catch { return DefaultRetentionDays; }
            finally { _resolvingSettings = false; }
        }

        private static string CommandLabel()
        {
            string command = Log.CurrentCommand;
            return command.Length == 0 ? "-" : command;
        }

        /// <summary>按级别数值过滤：INFO=0、WARN=1、ERROR=2；未识别的值按 INFO 处理。</summary>
        internal static int ParseLevel(string value)
        {
            string normalized = (value ?? "").Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "WARN":
                case "WARNING":
                    return 1;
                case "ERROR":
                    return 2;
                default:
                    return 0;
            }
        }

        private static string ScaleName(LogSeverity severity)
        {
            switch (severity)
            {
                case LogSeverity.Warn: return "WARN";
                case LogSeverity.Error: return "ERROR";
                default: return "INFO";
            }
        }

        private static int SafeRetentionDays(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return DefaultRetentionDays;
            int days = (int)value;
            if (days < 1) return DefaultRetentionDays;
            return days > MaxRetentionDays ? MaxRetentionDays : days;
        }

        /// <summary>删除超过保留天数的 uncad-YYYYMMDD.log；返回删除数量（测试可直接调用）。</summary>
        internal int PurgeOldLogs(DateTime now)
        {
            if (_directory == null) return 0;
            lock (_lock) return PurgeOldLogsLocked(now);
        }

        private int PurgeOldLogsLocked(DateTime now)
        {
            if (now.Date == _lastPurgeDate.Date) return 0;
            _lastPurgeDate = now.Date;
            int retentionDays = ConfiguredRetentionDays();
            DateTime cutoff = now.Date.AddDays(-(retentionDays - 1));
            int removed = 0;
            try
            {
                foreach (string file in Directory.GetFiles(_directory,
                    FilePrefix + "*" + FileSuffix))
                {
                    if (!TryParseLogDate(Path.GetFileNameWithoutExtension(file),
                        out DateTime date)) continue;
                    if (date.Date >= cutoff) continue;
                    try
                    {
                        File.Delete(file);
                        removed++;
                    }
                    catch { /* 单个文件被占用时保留下一天再清理 */ }
                }
            }
            catch { /* 目录读取失败不影响写日志 */ }
            return removed;
        }

        private static bool TryParseLogDate(string nameWithoutExtension, out DateTime date)
        {
            date = default;
            if (string.IsNullOrEmpty(nameWithoutExtension)
                || !nameWithoutExtension.StartsWith(FilePrefix,
                    StringComparison.OrdinalIgnoreCase)
                || nameWithoutExtension.Length != FilePrefix.Length + 8)
                return false;
            return DateTime.TryParseExact(
                nameWithoutExtension.Substring(FilePrefix.Length), "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static string ResolveDirectory(string requested)
        {
            string[] candidates = string.IsNullOrWhiteSpace(requested)
                ? new[]
                {
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "UNCAD", "logs"),
                    Path.Combine(Path.GetTempPath(), "UNCAD", "logs")
                }
                : new[] { requested };
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                try
                {
                    Directory.CreateDirectory(candidate);
                    return candidate;
                }
                catch { /* 换下一个候选目录 */ }
            }
            return null;
        }

        private enum LogSeverity
        {
            Info = 0,
            Warn = 1,
            Error = 2
        }
    }
}
