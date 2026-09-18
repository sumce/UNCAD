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
                new VersionChangeLogEntry("2.4.8", "2026-09-18",
                    "修复：桥架 BOQ 两行标注可识别 MTEXT 格式码及常见空白差异，长度不再误计为电缆，并恢复正常求和。",
                    "修复：两行标注严格按固定清单型号与纯长度配对，孤立长度或带前缀文本不会误加入桥架清单。",
                    "修复：U1Q 在大坐标、多角度斜线批量标注时改用稳定的直线端点计算，避免部分线段因 eInvalidInput 无法生成。",
                    "新增：统一机台表在前 51 个物理行内自动识别表头，兼容第二行表头；固定清单生成脚本同步支持。",
                    "修复：Windows 150%/200% 缩放及多显示器切换时，插件窗口按当前屏幕 DPI 和工作区自适应，不再超出屏幕。",
                    "维护：移除未使用的 JSON 依赖，减少宿主加载冲突并缩小发行包。"),
                new VersionChangeLogEntry("2.4.7", "2026-09-11",
                    "新增：U1SET 的 BOQ 自动填充新增独立“电盘”开关；电盘、断路器与插座盘分别控制，默认开启电盘与断路器。",
                    "修复：I-Line盘按固定清单分别生成电盘和断路器，不再用断路器开关代替电盘。",
                    "修复：长两行线管 MTEXT 按文字插入或对齐点归属图框，U1U 可重新识别并自动更新线管数量。",
                    "修复：U1F/U1U 自动把旧桥架标注升级为 BOQ 两行样式，并保留或修复标注角度。",
                    "优化：U1SET、在线授权与关于窗口采用更清晰的现代 WinForms 布局。",
                    "修正：构建时间统一按 UTC+8 显示。"),
                new VersionChangeLogEntry("2.4.6", "2026-09-11",
                    "新增：U1Q*/U1C 标注改为两行 MTEXT：第一行写固定清单「1.名称」的完整型号，第二行写长度；对齐仍为中下，锚点与原单行 DBText 同点。",
                    "新增：统计引擎按 MTEXT 为单位把两行配对还原成单行写法（桥架200*100 2500mm / ⌀20线管 2000mm）；配对严格——第一行必须是清单里的桥架/线管型号、第二行必须是纯长度，孤立的 2000mm 与老单行标注照旧按电缆统计。",
                    "新增：重跑 U1Q*/U1C 时自动把作用范围内的旧单行标注替换成两行；不重跑就不动。",
                    "修复：两行 MTEXT 行距固定为 0.75 倍字高（原默认 1.66 倍，两行显得太散）。",
                    "修复：U1U 更新时，规划行与旧行仅凭名称（如都叫“电缆”）匹配时不再继承旧行的清单编码与匹配标记，避免错编码进 CAD 表格并导出到 BOQ。",
                    "修复：桥架旧格数写法迁移成毫米时，若结果不以 0 结尾（如 12.5格×250=3125mm）则不迁移，保留旧格数写法——统计照样认，避免下一次 U1F/U1U 静默丢掉这条桥架。",
                    "修复：固定清单 8.11「插座漏电相序检测仪」名字含“插座”却被当成插座行，在清单确认后被策略静默删掉；有固定编码的行现在由编码判定是否插座。",
                    "修正：README 命令表把 U1C 从“绘制软管”改为“按管径绘制线管标注”。"),
                new VersionChangeLogEntry("2.4.5", "2026-09-09",
                    "新增：U1F/U1U/UNADD 统计支持识别对齐/转角标注中人工输入的文字（如 2000mm）；自动测量的标注不参与统计。",
                    "新增：U1SET 统计汇总提供“尺寸标注文字 (DIMENSION)”独立开关，默认开启。",
                    "新增：U1D 对齐标注转文字，将标注文字转换为可编辑单行文字并保留原尺寸线。",
                    "修复：150% 及以上屏幕缩放下 U1SET 等窗口输入框和页面显示不全的问题。",
                    "优化：U1Q/U1LX 标注重跑不再重复生成，跳过原因逐项提示；Xmerge 保持块样式并增强批量导入。"),
                new VersionChangeLogEntry("2.4.4", "2026-09-07",
                    "修复：U1F/U1U/U1S 批量流程统一使用固定清单项目编码，避免 CAD 与 BOQ 对账不一致。",
                    "修复：替代型号、图框身份和变更记录写入 frameinfo_json，后续 U1U 会复用已确认的数据。",
                    "修复：强化旧图框/xframe 识别、图框迁移、Xlayout 身份读取和 XSTS 统计异常报告。",
                    "修复：Xmerge 合并过程的块定义冲突与批量回滚问题。"),
                new VersionChangeLogEntry("2.4.3", "2026-09-06",
                    "新增：AutoCAD 启动后显示全屏 UNCAD 品牌动画，由 .NET 在 5 秒后自动关闭，可在 U1SET 中关闭。",
                    "修复 U1LX：从末端线任一端点击都能遍历完整连通路线。",
                    "U1LX 将末尾为 00mm 的标注继续视为可修改占位符，便于重新执行后纠正误填值。"),
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
