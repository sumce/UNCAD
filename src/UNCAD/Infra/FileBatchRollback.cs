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
                foreach (string path in targets) _entries.Add(CreateEntry(path));
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
            // 待写路径可能来自与建批次时不同的来源：U1U 批量用计划侧机台号建集合，
            // 实际写盘路径却来自 CAD 读出的机台号（D-021 允许两者只差内部空白）。
            // 这种文件必须按需纳入批次——否则它既没有备份也没有锁，回滚保护会漏掉它，
            // 用户还会先看到一句没法照着做的"文件不属于当前回滚批次"。
            Entry entry = FindOrAdd(path);
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
            // Complete() 已经删除了备份并释放了锁；此时再回滚只会拿已删除的备份
            // 去覆盖输出文件，产生一条假的"无法完整恢复"失败，把完好的现场报成
            // 损坏。Rollback 因此必须是幂等的：批次一旦收尾就什么都不做。
            if (_completed) return;
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

        /// <summary>把批次之外的输出路径按需纳入保护（备份 + 锁），返回批次内条目。</summary>
        private Entry FindOrAdd(string path)
        {
            Entry entry = TryFind(path);
            if (entry != null) return entry;
            entry = CreateEntry(path);
            _entries.Add(entry);
            return entry;
        }

        private static Entry CreateEntry(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string folder = Path.GetDirectoryName(fullPath);
            // 批次负责为条目建目录：调用方不必先建，按需纳入的路径也不会因为目录
            // 不存在而拿不到锁侧车文件。
            if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            var entry = new Entry
            {
                Target = fullPath,
                UpdateLock = FileUpdateLock.Acquire(fullPath),
                Existed = File.Exists(fullPath)
            };
            if (entry.Existed)
            {
                entry.Backup = fullPath + ".uncad-batch-"
                    + Guid.NewGuid().ToString("N") + ".bak";
                File.Copy(fullPath, entry.Backup, false);
                entry.InitialFingerprint = ComputeFingerprint(entry.Backup);
            }
            return entry;
        }

        private Entry TryFind(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return _entries.FirstOrDefault(item => string.Equals(item.Target,
                fullPath, StringComparison.OrdinalIgnoreCase));
        }

        private Entry Find(string path)
        {
            Entry entry = TryFind(path);
            if (entry == null)
                throw new InvalidOperationException("文件不属于当前回滚批次："
                    + Path.GetFullPath(path) + "。批次内已知文件："
                    + string.Join("、", _entries.Select(item => item.Target)));
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
