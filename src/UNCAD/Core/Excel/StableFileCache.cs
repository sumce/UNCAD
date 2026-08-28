using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace UNCAD.Core.Excel
{
    /// <summary>
    /// 按文件路径缓存解析结果，并在加载前后确认文件内容未发生变化。
    /// 不同路径之间互不阻塞；同一路径仍保证只有一个加载操作提交缓存。
    /// </summary>
    internal sealed class StableFileCache<T>
    {
        private const int MaxAttempts = 10;

        // 前几次快速重试，之后放慢等待，只覆盖文件系统报告的瞬时 IO 失败。
        private static int BackoffMs(int attempt) => attempt < 3 ? 150 : 500;

        private sealed class FileFingerprint
        {
            public long Length;
            public DateTime LastWriteUtc;
            public byte[] Hash;
        }

        private sealed class Entry
        {
            public FileFingerprint Fingerprint;
            public T Value;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, object> _pathGates =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<string, T> _loader;
        private readonly Func<T, T> _clone;
        private readonly string _subject;

        public StableFileCache(string subject, Func<string, T> loader, Func<T, T> clone)
        {
            _subject = string.IsNullOrWhiteSpace(subject) ? "文件" : subject.Trim();
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _clone = clone ?? throw new ArgumentNullException(nameof(clone));
        }

        public T Load(string filePath, out bool cacheHit)
        {
            string path = Path.GetFullPath(filePath ?? "");
            object pathGate = GetPathGate(path);
            lock (pathGate)
            {
                FileFingerprint current = null;
                try
                {
                    current = ReadFingerprint(path);
                }
                catch (FileNotFoundException)
                {
                    throw;
                }
                catch (IOException)
                {
                    // 首次指纹读取也可能遇到瞬时 IO 失败，交给统一重试循环处理。
                }
                if (current != null)
                {
                    lock (_gate)
                    {
                        if (_entries.TryGetValue(path, out Entry existing)
                            && SameFingerprint(existing.Fingerprint, current))
                        {
                            cacheHit = true;
                            return _clone(existing.Value);
                        }
                    }
                }

                Exception lastError = null;
                bool sawIoFailure = false;
                for (int attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    try
                    {
                        FileFingerprint before = ReadFingerprint(path);
                        T loaded = _loader(path);
                        FileFingerprint after = ReadFingerprint(path);
                        if (!SameFingerprint(before, after))
                        {
                            lastError = new IOException(_subject + "在读取期间发生变化。", null);
                        }
                        else
                        {
                            lock (_gate)
                            {
                                _entries[path] = new Entry
                                {
                                    Fingerprint = after,
                                    Value = _clone(loaded)
                                };
                            }
                            cacheHit = false;
                            return _clone(loaded);
                        }
                    }
                    catch (InvalidDataException)
                    {
                        throw;
                    }
                    catch (IOException ex)
                    {
                        // 文件锁、网络文件短暂不可读和文件变化都属于可重试的 IO 情况。
                        sawIoFailure = true;
                        lastError = ex;
                    }
                    catch (Exception)
                    {
                        // 解析、参数和程序集错误不是临时文件占用，立即保留原始类型和堆栈。
                        throw;
                    }
                    if (attempt < MaxAttempts - 1) Thread.Sleep(BackoffMs(attempt));
                }

                if (sawIoFailure)
                    throw new IOException(_subject
                        + " 被其他程序占用或正在保存，请确认文件可读后重试。", lastError);
                throw new IOException(_subject + "正在更新或无法稳定读取，请保存完成后重试。",
                    lastError);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                // 保留路径锁对象，避免 Clear 与正在进行的同路径加载形成双重锁。
                _entries.Clear();
            }
        }

        private object GetPathGate(string path)
        {
            lock (_gate)
            {
                if (!_pathGates.TryGetValue(path, out object gate))
                {
                    gate = new object();
                    _pathGates[path] = gate;
                }
                return gate;
            }
        }

        private static FileFingerprint ReadFingerprint(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("文件不存在。", path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (SHA256 sha256 = SHA256.Create())
            {
                return new FileFingerprint
                {
                    Length = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    Hash = sha256.ComputeHash(stream)
                };
            }
        }

        private static bool SameFingerprint(FileFingerprint left, FileFingerprint right)
        {
            if (left == null || right == null || left.Length != right.Length
                || left.LastWriteUtc != right.LastWriteUtc
                || left.Hash == null || right.Hash == null
                || left.Hash.Length != right.Hash.Length) return false;
            for (int i = 0; i < left.Hash.Length; i++)
                if (left.Hash[i] != right.Hash[i]) return false;
            return true;
        }
    }
}
