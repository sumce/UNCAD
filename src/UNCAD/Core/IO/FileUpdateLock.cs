using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace UNCAD.Core.IO
{
    /// <summary>
    /// Cooperative cross-process lock for files written by UNCAD. Locks are re-entrant on the
    /// current thread so a batch transaction can hold the lock while an individual writer runs.
    /// </summary>
    internal static class FileUpdateLock
    {
        [ThreadStatic]
        private static Dictionary<string, LockState> _owned;

        private sealed class LockState
        {
            public FileStream Stream { get; set; }
            public int Count { get; set; }
        }

        private sealed class Lease : IDisposable
        {
            private readonly string _lockPath;
            private bool _disposed;

            public Lease(string lockPath) => _lockPath = lockPath;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Release(_lockPath);
            }
        }

        public static IDisposable Acquire(string targetPath, string busyMessage = null)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("文件路径为空。", nameof(targetPath));

            string lockPath = Path.GetFullPath(targetPath) + ".uncad.lock";
            Dictionary<string, LockState> owned = _owned
                ?? (_owned = new Dictionary<string, LockState>(StringComparer.OrdinalIgnoreCase));
            if (owned.TryGetValue(lockPath, out LockState existing))
            {
                existing.Count++;
                return new Lease(lockPath);
            }

            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    var stream = new FileStream(lockPath, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);
                    try
                    {
                        File.SetAttributes(lockPath,
                            File.GetAttributes(lockPath) | FileAttributes.Hidden);
                    }
                    catch { }
                    owned[lockPath] = new LockState { Stream = stream, Count = 1 };
                    return new Lease(lockPath);
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(150);
                }
            }
            throw new IOException(string.IsNullOrWhiteSpace(busyMessage)
                ? "文件正在被另一个 UNCAD 进程更新，请稍后重试。"
                : busyMessage);
        }

        private static void Release(string lockPath)
        {
            Dictionary<string, LockState> owned = _owned;
            if (owned == null || !owned.TryGetValue(lockPath, out LockState state)) return;
            state.Count--;
            if (state.Count > 0) return;

            owned.Remove(lockPath);
            state.Stream.Dispose();
            try { if (File.Exists(lockPath)) File.Delete(lockPath); }
            catch { }
            if (owned.Count == 0) _owned = null;
        }
    }
}
