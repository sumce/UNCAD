using System;
using System.Threading;
using UNCAD.Core.Contracts;

namespace UNCAD.Infra
{
    /// <summary>
    /// 日志门面：静态访问入口，委托给 ILogger 实现（文件日志）。
    /// <see cref="Command"/> 提供当前命令标签作用域：AsyncLocal 保存标签，
    /// FileLogger 会把标签写进每一行，OperationLog 也可读取；嵌套作用域内层覆盖外层。
    /// </summary>
    public static class Log
    {
        private static readonly ILogger Logger = FileLogger.Instance;
        private static readonly AsyncLocal<string> CommandTag = new AsyncLocal<string>();

        /// <summary>当前异步/线程上下文里的命令标签；没有作用域时为空字符串。</summary>
        public static string CurrentCommand => CommandTag.Value ?? "";

        public static void Info(string message) => Logger.Info(message);
        public static void Warn(string message) => Logger.Warn(message);
        public static void Error(string message, Exception ex = null) => Logger.Error(message, ex);

        /// <summary>
        /// 标记当前命令，供日志行和操作记录使用。命令入口用 using 包裹执行阶段：
        /// 返回的 IDisposable 在释放时恢复外层标签。null/空白命令名不覆盖外层标签。
        /// </summary>
        public static IDisposable Command(string command)
            => new CommandScope(command);

        private sealed class CommandScope : IDisposable
        {
            private readonly string _previous;
            private bool _disposed;

            public CommandScope(string command)
            {
                _previous = CommandTag.Value;
                string normalized = (command ?? "").Trim();
                if (normalized.Length > 0) CommandTag.Value = normalized;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                CommandTag.Value = _previous;
            }
        }
    }
}
