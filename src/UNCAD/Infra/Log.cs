using System;
using UNCAD.Core.Contracts;

namespace UNCAD.Infra
{
    /// <summary>日志门面：静态访问入口，委托给 ILogger 实现（文件日志）。</summary>
    public static class Log
    {
        private static readonly ILogger Logger = FileLogger.Instance;

        public static void Info(string message) => Logger.Info(message);
        public static void Warn(string message) => Logger.Warn(message);
        public static void Error(string message, Exception ex = null) => Logger.Error(message, ex);
    }
}
