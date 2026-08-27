using System;

namespace UNCAD.Core.Contracts
{
    /// <summary>日志抽象。实现：Infra.Log（文件 + 命令行），供批量处理/图纸检查审计使用。</summary>
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception ex = null);
    }
}
