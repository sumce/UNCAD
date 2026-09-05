using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Infra
{
    /// <summary>One released version and its user-facing update notes.</summary>
    public sealed class VersionChangeLogEntry
    {
        public VersionChangeLogEntry(string version, string dateUtc,
            params string[] changes)
        {
            Version = version;
            DateUtc = dateUtc;
            Changes = (changes ?? Array.Empty<string>())
                .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        }

        public string Version { get; }
        public string DateUtc { get; }
        public string[] Changes { get; }
    }

    /// <summary>
    /// Hard-coded update notes shown after upgrading. Newest version first; the
    /// newest entry must match <see cref="ProductMetadata.VersionLabel"/> — the
    /// version contract test enforces this so a release cannot ship stale notes.
    /// </summary>
    public static class VersionChangeLog
    {
        public static readonly IReadOnlyList<VersionChangeLogEntry> Entries =
            new List<VersionChangeLogEntry>
            {
                new VersionChangeLogEntry("2.4.2", "2026-09-06",
                    "性能优化：U1U、XLAYOUT、XSTS、U1S、U1DWG、Xmerge 批量读取共用一个 CAD 事务和块定义缓存，大幅减少大图批量操作的等待时间。",
                    "图框识别支持旧版 frame 块（frame_20260812/frame/xframe 均可）。",
                    "U1LX 快速标注在密集图纸上的标签匹配更快。",
                    "新增：更新到新版本后首次打开 CAD 会显示本更新日志。"),
                new VersionChangeLogEntry("2.4.1", "2026-09-05",
                    "机台数据改为每用户 SQLite 快照：U1SET 刷新是唯一解析入口，命令执行时不再重复加载整本 Excel。",
                    "U1F、U1U、XSTS、XLAYOUT 按需查询快照，源文件变化在下一次手动刷新后生效。",
                    "刷新过程采用事务校验，失败时保留上一份有效快照。"),
                new VersionChangeLogEntry("2.4.0", "2026-09-05",
                    "强化 U1F/U1U 的 CAD 与 BOQ 回滚流程，失败时不再覆盖用户并发保存的新版工作簿。",
                    "电缆与桥架按编码和名称互斥分类；重复 BOQ 行按出现次数一对一匹配。",
                    "Ruanguan、设备、上游、图框及 frameinfo_json 支持 Xmerge 重命名后的块名。",
                    "改进在线授权离线宽限、网络工作簿缓存、Ribbon 重建和发行载荷校验。")
            };

        /// <summary>Returns notes for every version newer than the given version label.</summary>
        public static List<VersionChangeLogEntry> EntriesNewerThan(string seenVersion)
        {
            Version seen = ParseOrNull(seenVersion);
            IEnumerable<VersionChangeLogEntry> entries = Entries;
            if (seen != null)
                entries = entries.Where(entry =>
                    ParseOrNull(entry.Version) == null || ParseOrNull(entry.Version) > seen);
            else
                entries = entries.Take(1); // First install: only show the latest notes.
            return entries.ToList();
        }

        private static Version ParseOrNull(string value)
        {
            Version parsed;
            return Version.TryParse((value ?? "").Trim(), out parsed) ? parsed : null;
        }
    }
}
