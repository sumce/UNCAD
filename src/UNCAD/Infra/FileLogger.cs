using System;
using System.IO;
using UNCAD.Core.Contracts;

namespace UNCAD.Infra
{
    /// <summary>文件日志（ILogger 实现）：%APPDATA%\UNCAD\logs\uncad.log。</summary>
    public sealed class FileLogger : ILogger
    {
        public static readonly FileLogger Instance = new FileLogger();

        private readonly object _lock = new object();
        private readonly string _logPath;

        public FileLogger()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "UNCAD", "logs");
                Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, "uncad.log");
            }
            catch { _logPath = Path.GetTempFileName(); }
        }

        public void Info(string message) => Write("INFO", message, null);
        public void Warn(string message) => Write("WARN", message, null);
        public void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (_lock)
                {
                    // 简单轮转：超过 5MB 时把旧日志归档为 .old，避免无限增长
                    try
                    {
                        var fi = new FileInfo(_logPath);
                        if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                        {
                            if (File.Exists(_logPath + ".old")) File.Delete(_logPath + ".old");
                            File.Move(_logPath, _logPath + ".old");
                        }
                    }
                    catch { /* 轮转失败不影响写日志 */ }
                    File.AppendAllText(_logPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}"
                        + (ex != null ? " | " + ex : "")
                        + Environment.NewLine);
                }
            }
            catch { /* 日志失败不影响插件运行 */ }
        }
    }
}
