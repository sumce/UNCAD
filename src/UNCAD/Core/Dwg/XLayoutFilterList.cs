using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Text;

namespace UNCAD.Core.Dwg
{
    /// <summary>
    /// 清单条目（D-055）：机台ID 必填；回路名称（设备名称）可空——空回路表示
    /// 该机台的全部回路。比对走 <see cref="IdentityTextNormalizer"/> 口径
    /// （D-021：忽略内部空白、大小写不敏感），原始文本保留用于展示。
    /// </summary>
    public sealed class XLayoutFilterEntry
    {
        public XLayoutFilterEntry(string machineId, string circuitName)
        {
            MachineId = (machineId ?? "").Trim();
            CircuitName = (circuitName ?? "").Trim();
        }

        public string MachineId { get; }
        public string CircuitName { get; }

        public string MachineKey => IdentityTextNormalizer.Key(MachineId);
        public string CircuitKey => IdentityTextNormalizer.Key(CircuitName);

        /// <summary>机台级条目（回路为空，命中该机台全部回路）。</summary>
        public bool IsMachineLevel => CircuitName.Length == 0;

        public string DisplayText => IsMachineLevel
            ? MachineId + "（整台）"
            : MachineId + "-" + CircuitName;
    }

    /// <summary>一条清单条目对图框集合的命中结果：命中零个 = 图纸上找不到。</summary>
    public sealed class XLayoutFilterMatch
    {
        public XLayoutFilterMatch(XLayoutFilterEntry entry,
            IReadOnlyList<XLayoutFrameItem> items)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Items = items ?? Array.Empty<XLayoutFrameItem>();
        }

        public XLayoutFilterEntry Entry { get; }
        public IReadOnlyList<XLayoutFrameItem> Items { get; }
        public bool Found => Items.Count > 0;
    }

    /// <summary>
    /// 选择窗「导入清单」回调的返回值（Features 填充，窗口只消费）：
    /// List 非空=导入成功；List 空 + Error 非空=解析失败（窗口显示 Error）；
    /// 两者皆空=用户取消了文件对话框（静默）。
    /// </summary>
    public sealed class XLayoutFilterImportResult
    {
        public XLayoutFilterImportResult(XLayoutFilterList list, string error)
        {
            List = list;
            Error = (error ?? "").Trim();
        }

        public XLayoutFilterList List { get; }
        public string Error { get; }
        public bool Cancelled => List == null && Error.Length == 0;
    }

    /// <summary>
    /// XLAYOUT 导入的机台/回路筛选清单（D-055）。匹配是纯逻辑（可单测）：
    /// 条目命中图框 = 机台号相等且（回路为空或回路名相等）；
    /// 图框被清单命中 = 任一条目命中它。
    /// </summary>
    public sealed class XLayoutFilterList
    {
        public XLayoutFilterList(IReadOnlyList<XLayoutFilterEntry> entries,
            string sourcePath)
        {
            Entries = entries ?? Array.Empty<XLayoutFilterEntry>();
            SourcePath = (sourcePath ?? "").Trim();
        }

        public IReadOnlyList<XLayoutFilterEntry> Entries { get; }
        public string SourcePath { get; }

        public static readonly XLayoutFilterList Empty =
            new XLayoutFilterList(Array.Empty<XLayoutFilterEntry>(), "");

        public bool IsEmpty => Entries.Count == 0;

        /// <summary>条目是否命中该图框（机台相等且回路为空或相等）。</summary>
        public static bool Matches(XLayoutFilterEntry entry, XLayoutFrameItem item)
        {
            if (entry == null || item == null) return false;
            if (!IdentityTextNormalizer.Equals(entry.MachineId, item.MachineId))
                return false;
            return entry.IsMachineLevel
                || IdentityTextNormalizer.Equals(entry.CircuitName, item.DeviceName);
        }

        /// <summary>图框是否被清单中任一条目命中（选择窗预勾选的判据）。</summary>
        public bool IsMatched(XLayoutFrameItem item)
            => Entries.Any(entry => Matches(entry, item));

        /// <summary>逐条目聚合命中（结果导出「以清单为纲」的数据源）。
        /// 顺序与清单一致；匹配在给定图框全集内进行。</summary>
        public IReadOnlyList<XLayoutFilterMatch> MatchAll(
            IReadOnlyList<XLayoutFrameItem> items)
        {
            var pool = items ?? Array.Empty<XLayoutFrameItem>();
            return Entries
                .Select(entry => new XLayoutFilterMatch(entry, pool
                    .Where(item => Matches(entry, item))
                    .ToList()))
                .ToList();
        }
    }
}
