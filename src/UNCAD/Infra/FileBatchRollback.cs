using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public void Rollback()
        {
            var failures = new List<Exception>();
            for (int index = _entries.Count - 1; index >= 0; index--)
            {
                Entry entry = _entries[index];
                try
                {
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
    }
}
