using System;
using System.IO;
using UNCAD.Core.IO;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FileUpdateLockTests
    {
        [Fact]
        public void Acquire_ReportsTheCallersBusyMessageWhenTheLockStaysTaken()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_lock_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, "target.txt");
            string lockPath = Path.GetFullPath(target) + ".uncad.lock";
            try
            {
                // 直接占住锁文件：同线程再取锁会走"重入"分支，因此必须占用真实文件流。
                using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None))
                {
                    IOException error = Assert.Throws<IOException>(() =>
                        FileUpdateLock.Acquire(target, "BOQ 正在被另一个 UNCAD 用户更新，请稍后重试。"));
                    // 20 次尝试后必须给出调用方的中文忙提示，而不是原始系统异常。
                    Assert.Contains("正在被另一个 UNCAD 用户更新", error.Message);
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }
    }
}
