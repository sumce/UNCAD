using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace UNCAD.Core.Excel
{
    internal sealed class StableFileCache<T>
    {
        private const int MaxAttempts = 10;

        // 前几次快速重试，之后放慢等待，覆盖 WPS/Excel 自动保存等瞬时占用。
        private static int BackoffMs(int attempt) => attempt < 3 ? 150 : 500;

        private sealed class Entry
        {
            public long Length;
            public DateTime LastWriteUtc;
            public T Value;
        }

        private readonly object _gate = new object();
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
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException(_subject + "不存在。", path);

            lock (_gate)
            {
                if (_entries.TryGetValue(path, out Entry existing)
                    && existing.Length == info.Length
                    && existing.LastWriteUtc == info.LastWriteTimeUtc)
                {
                    cacheHit = true;
                    return _clone(existing.Value);
                }

                Exception lastError = null;
                bool sawLock = false;
                for (int attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    info.Refresh();
                    if (!info.Exists) throw new FileNotFoundException(_subject + "不存在。", path);
                    long length = info.Length;
                    DateTime writeTime = info.LastWriteTimeUtc;
                    try
                    {
                        T loaded = _loader(path);
                        info.Refresh();
                        if (info.Exists && info.Length == length
                            && info.LastWriteTimeUtc == writeTime)
                        {
                            _entries[path] = new Entry
                            {
                                Length = length,
                                LastWriteUtc = writeTime,
                                Value = _clone(loaded)
                            };
                            cacheHit = false;
                            return _clone(loaded);
                        }
                    }
                    catch (InvalidDataException) { throw; }
                    catch (IOException ex) { sawLock = true; lastError = ex; }
                    catch (Exception ex) { lastError = ex; }
                    if (attempt < MaxAttempts - 1) Thread.Sleep(BackoffMs(attempt));
                }
                if (sawLock)
                    throw new IOException(_subject
                        + " 正被其他程序（如 WPS/Excel）占用或正在保存，请保存完成后重试。",
                        lastError);
                throw new IOException(_subject + "正在更新或无法稳定读取，请保存完成后重试。",
                    lastError);
            }
        }

        public void Clear()
        {
            lock (_gate) _entries.Clear();
        }
    }
}
