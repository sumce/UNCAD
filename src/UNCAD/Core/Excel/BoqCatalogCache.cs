using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace UNCAD.Core.Excel
{
    /// <summary>固定 Sheet2 清单缓存；文件时间或大小变化时自动重新读取。</summary>
    internal static class BoqCatalogCache
    {
        private sealed class Entry
        {
            public long Length;
            public DateTime LastWriteUtc;
            public List<ListItem> Items;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static List<ListItem> Load(string filePath, out bool cacheHit)
        {
            string path = Path.GetFullPath(filePath ?? "");
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("固定清单 Excel 不存在。", path);

            lock (Gate)
            {
                if (Entries.TryGetValue(path, out Entry existing)
                    && existing.Length == info.Length
                    && existing.LastWriteUtc == info.LastWriteTimeUtc)
                {
                    cacheHit = true;
                    return Clone(existing.Items);
                }

                System.Exception lastError = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    info.Refresh();
                    long length = info.Length;
                    DateTime writeTime = info.LastWriteTimeUtc;
                    try
                    {
                        List<ListItem> loaded = ListItemReader.ReadList(path);
                        info.Refresh();
                        if (info.Exists && info.Length == length
                            && info.LastWriteTimeUtc == writeTime)
                        {
                            Entries[path] = new Entry
                            {
                                Length = info.Length,
                                LastWriteUtc = info.LastWriteTimeUtc,
                                Items = Clone(loaded)
                            };
                            cacheHit = false;
                            return Clone(loaded);
                        }
                    }
                    catch (InvalidDataException) { throw; }
                    catch (System.Exception ex) { lastError = ex; }
                    if (attempt < 2) Thread.Sleep(120);
                }
                throw new IOException("固定清单 Excel 正在更新或无法稳定读取，请保存完成后重试。",
                    lastError);
            }
        }

        internal static void Clear()
        {
            lock (Gate) Entries.Clear();
        }

        private static List<ListItem> Clone(IEnumerable<ListItem> source)
        {
            var result = new List<ListItem>();
            foreach (ListItem item in source ?? new List<ListItem>())
            {
                result.Add(new ListItem
                {
                    Code = item.Code,
                    Name = item.Name,
                    Feature = item.Feature,
                    Unit = item.Unit,
                    Spec = item.Spec
                });
            }
            return result;
        }
    }
}
