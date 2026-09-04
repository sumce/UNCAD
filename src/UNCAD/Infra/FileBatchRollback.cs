using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UNCAD.Core.IO;

namespace UNCAD.Infra
{
    /// <summary>Snapshots output files so a multi-file operation can restore its prior state.</summary>
    internal sealed class FileBatchRollback : IDisposable
    {
        private sealed class Entry
        {
            public string Target { get; set; }
            public string Backup { get; set; }
            public bool Existed { get; set; }
            public IDisposable UpdateLock { get; set; }
            public byte[] InitialFingerprint { get; set; }
            public bool WriteStarted { get; set; }
            public bool WriteCompleted { get; set; }
            public bool WrittenFileExists { get; set; }
            public byte[] WrittenFingerprint { get; set; }
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private bool _completed;

        public FileBatchRollback(IEnumerable<string> paths)
        {
            string[] targets = (paths ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Path.GetFullPath(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            try
            {
                foreach (string path in targets)
                {
                    var entry = new Entry
                    {
                        Target = path,
                        UpdateLock = FileUpdateLock.Acquire(path),
                        Existed = File.Exists(path)
                    };
                    _entries.Add(entry);
                    if (entry.Existed)
                    {
                        entry.Backup = path + ".uncad-batch-" + Guid.NewGuid().ToString("N") + ".bak";
                        File.Copy(path, entry.Backup, false);
                        entry.InitialFingerprint = ComputeFingerprint(entry.Backup);
                    }
                }
            }
            catch
            {
                DeleteBackups();
                ReleaseLocks();
                throw;
            }
        }

        public void Complete()
        {
            _completed = true;
            try { DeleteBackups(); }
            finally { ReleaseLocks(); }
        }

        public void BeginWrite(string path)
        {
            Entry entry = Find(path);
            ValidateInitialSnapshot(entry);
            entry.WriteStarted = true;
            entry.WriteCompleted = false;
            entry.WrittenFileExists = false;
            entry.WrittenFingerprint = null;
        }

        public void MarkWritten(string path)
        {
            Entry entry = Find(path);
            entry.WriteStarted = true;
            entry.WriteCompleted = true;
            entry.WrittenFileExists = File.Exists(entry.Target);
            entry.WrittenFingerprint = entry.WrittenFileExists
                ? ComputeFingerprint(entry.Target) : null;
        }

        public void MarkWritten(string path, byte[] writtenFingerprint)
        {
            if (writtenFingerprint == null || writtenFingerprint.Length == 0)
                throw new ArgumentException("写入文件指纹为空。", nameof(writtenFingerprint));
            Entry entry = Find(path);
            entry.WriteStarted = true;
            entry.WriteCompleted = true;
            entry.WrittenFileExists = true;
            entry.WrittenFingerprint = (byte[])writtenFingerprint.Clone();
        }

        public void CancelWrite(string path)
        {
            Entry entry = Find(path);
            entry.WriteStarted = false;
            entry.WriteCompleted = false;
            entry.WrittenFileExists = false;
            entry.WrittenFingerprint = null;
        }

        public void Rollback()
        {
            var failures = new List<Exception>();
            for (int index = _entries.Count - 1; index >= 0; index--)
            {
                Entry entry = _entries[index];
                if (!entry.WriteStarted) continue;
                try
                {
                    if (entry.WriteCompleted && !StillMatchesWrittenFile(entry))
                        throw new IOException("输出文件在 UNCAD 写入后又被外部修改，"
                            + "未使用旧备份覆盖最新版：" + entry.Target);
                    if (entry.Existed)
                    {
                        File.Copy(entry.Backup, entry.Target, true);
                    }
                    else if (File.Exists(entry.Target))
                    {
                        File.Delete(entry.Target);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new IOException("无法恢复输出文件：" + entry.Target, ex));
                }
            }
            try { DeleteBackups(); }
            finally
            {
                _completed = true;
                ReleaseLocks();
            }
            if (failures.Count > 0)
                throw new AggregateException("批次输出失败后无法完整恢复原文件。", failures);
        }

        public void Dispose()
        {
            if (_completed) return;
            try
            {
                Rollback();
            }
            catch (Exception ex)
            {
                Log.Error("批次失败后自动恢复输出文件失败", ex);
            }
        }

        private void DeleteBackups()
        {
            foreach (Entry entry in _entries)
            {
                try
                {
                    if (!string.IsNullOrEmpty(entry.Backup) && File.Exists(entry.Backup))
                        File.Delete(entry.Backup);
                }
                catch
                {
                    Log.Warn("无法删除批次备份文件：" + entry.Backup);
                }
            }
        }

        private void ReleaseLocks()
        {
            for (int index = _entries.Count - 1; index >= 0; index--)
            {
                try { _entries[index].UpdateLock?.Dispose(); }
                catch (Exception ex) { Log.Warn("无法释放输出文件锁：" + ex.Message); }
                _entries[index].UpdateLock = null;
            }
        }

        private Entry Find(string path)
        {
            string fullPath = Path.GetFullPath(path);
            Entry entry = _entries.FirstOrDefault(item => string.Equals(item.Target,
                fullPath, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                throw new InvalidOperationException("文件不属于当前回滚批次：" + fullPath);
            return entry;
        }

        private static bool StillMatchesWrittenFile(Entry entry)
        {
            bool exists = File.Exists(entry.Target);
            if (exists != entry.WrittenFileExists) return false;
            return !exists || SameFingerprint(entry.WrittenFingerprint,
                ComputeFingerprint(entry.Target));
        }

        private static void ValidateInitialSnapshot(Entry entry)
        {
            bool exists = File.Exists(entry.Target);
            if (exists != entry.Existed)
                throw new IOException("输出文件在批次准备后被外部创建或删除，未开始写入："
                    + entry.Target);
            if (exists && !SameFingerprint(entry.InitialFingerprint,
                ComputeFingerprint(entry.Target)))
                throw new IOException("输出文件在批次准备后被外部修改，未开始写入："
                    + entry.Target);
        }

        private static byte[] ComputeFingerprint(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (SHA256 sha256 = SHA256.Create())
                return sha256.ComputeHash(stream);
        }

        private static bool SameFingerprint(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++)
                if (left[index] != right[index]) return false;
            return true;
        }
    }
}
