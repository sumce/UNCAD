using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Geometry;
using UNCAD.Core.Text;

namespace UNCAD.Core.Dwg
{
    /// <summary>One source frame used by XLAYOUT.</summary>
    public sealed class XLayoutFrameItem : DwgFrameLayoutItem
    {
        public XLayoutFrameItem(string machineId, string deviceName, FrameRectangle boundary,
            string handle = "", string upstreamId = "", string circuitKey = "")
            : base(machineId, deviceName, boundary)
        {
            Handle = handle ?? "";
            UpstreamId = upstreamId ?? "";
            CircuitKey = circuitKey ?? "";
        }

        public string Handle { get; }
        /// <summary>
        /// D-057：图框记录的上游编号明文（frameinfo_json）。同机台同名回路靠它
        /// 区分；老图框或复制来的图框可能为空。
        /// </summary>
        public string UpstreamId { get; }
        /// <summary>D-057：回路身份键（机台ID+回路名+上游编号）；老图框可能为空。</summary>
        public string CircuitKey { get; }

        /// <summary>配对用的条目（键/上游取自图框自身记录）。</summary>
        public CircuitSelection ToSelection() => new CircuitSelection
        {
            MachineId = MachineId ?? "",
            CircuitName = DeviceName ?? "",
            UpstreamId = UpstreamId ?? "",
            CircuitKey = CircuitKey ?? "",
            Handle = Handle ?? ""
        };
    }

    /// <summary>One frame translation produced by XLAYOUT.</summary>
    public sealed class XLayoutPlacement
    {
        internal XLayoutPlacement(XLayoutFrameItem item, double translationX,
            double translationY, int rowIndex, string rowLabel)
        {
            Item = item;
            TranslationX = translationX;
            TranslationY = translationY;
            RowIndex = rowIndex;
            RowLabel = rowLabel ?? "";
        }

        public XLayoutFrameItem Item { get; }
        public double TranslationX { get; }
        public double TranslationY { get; }
        public int RowIndex { get; }
        /// <summary>行首标签文本：机台模式=机台 ID；上游模式=上游编号（未配上游的兜底文案）。</summary>
        public string RowLabel { get; }
    }

    /// <summary>XLAYOUT 排列方案（D-045，2026-11-03）。</summary>
    public enum XLayoutArrangeMode
    {
        /// <summary>按机台排列，机台行序按完整性：完整 &gt; 重复/多出 &gt; 缺回路 &gt; 无基准（默认）。</summary>
        MachineReadiness = 0,
        /// <summary>按机台排列，机台行序按机台 ID 字母序。</summary>
        MachineIdOrder = 1,
        /// <summary>按上游排列：同一 U_上游编号 的回路排到一行。</summary>
        Upstream = 2
    }

    /// <summary>排列方案的设置值与中文名解析（选择窗/设置/日志共用一套口径）。</summary>
    public static class XLayoutArrangeModes
    {
        public const string ReadinessValue = "readiness";
        public const string MachineIdValue = "machine-id";
        public const string UpstreamValue = "upstream";

        public static string Value(XLayoutArrangeMode mode)
            => mode == XLayoutArrangeMode.MachineIdOrder ? MachineIdValue
                : mode == XLayoutArrangeMode.Upstream ? UpstreamValue
                : ReadinessValue;

        public static string DisplayName(XLayoutArrangeMode mode)
            => mode == XLayoutArrangeMode.MachineIdOrder ? "按机台ID顺序"
                : mode == XLayoutArrangeMode.Upstream ? "按上游编号"
                : "按机台完整性";

        public static bool TryParseValue(string value, out XLayoutArrangeMode mode)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case MachineIdValue:
                case "machineid":
                    mode = XLayoutArrangeMode.MachineIdOrder;
                    return true;
                case UpstreamValue:
                    mode = XLayoutArrangeMode.Upstream;
                    return true;
                case ReadinessValue:
                case "":
                    mode = XLayoutArrangeMode.MachineReadiness;
                    return true;
                default:
                    mode = XLayoutArrangeMode.MachineReadiness;
                    return false;
            }
        }
    }

    /// <summary>
    /// 排列所需的快照对照数据（D-045）：机台完整性等级、回路在 Excel 里的行序、
    /// 回路的上游编号。全部可缺——缺哪块就退回旧行为（按图上位置排）。
    /// </summary>
    public sealed class XLayoutArrangeContext
    {
        public static readonly XLayoutArrangeContext Empty = new XLayoutArrangeContext();

        /// <summary>机台 ID → 就绪等级（0 完整 / 1 重复或多出 / 2 缺回路 / 3 无基准）。</summary>
        public IDictionary<string, int> ReadinessRank { get; }
            = new Dictionary<string, int>(IdentityTextNormalizer.Comparer);

        /// <summary>机台ID+回路 → Excel 行序（越小越靠前；缺失的回路排在本组末尾）。
        /// 3.3.3.2（审计修复）：键比较换 IdentityTextNormalizer（D-021——忽略全部
        /// Unicode 空白与大小写，与 ReadinessRank 同口径）。旧键只 Trim()，图上
        /// 文字与工作簿存在内部空格/大小写差异时行序静默退回图上序、上游模式
        /// 错分进"未配上游"行。填充侧仍走 MakeKey，两侧经比较器归一后命中。</summary>
        public IDictionary<string, int> RowOrder { get; }
            = new Dictionary<string, int>(IdentityTextNormalizer.Comparer);

        /// <summary>机台ID+回路 → 上游编号（U_上游编号；空白视为未配置）。
        /// 3.3.3.2：同 RowOrder 换 IdentityTextNormalizer（D-021）。
        /// D-057：只在机台内回路名唯一时填充——重名回路的行序/上游走下面两个
        /// 按键字典，否则两条同名回路会共用第一条的行序、最后一条的上游。</summary>
        public IDictionary<string, string> UpstreamOf { get; }
            = new Dictionary<string, string>(IdentityTextNormalizer.Comparer);

        /// <summary>D-057：回路身份键 → Excel 行序（同名回路各有各的行序）。</summary>
        public IDictionary<string, int> RowOrderByKey { get; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>D-057：回路身份键 → 上游编号。</summary>
        public IDictionary<string, string> UpstreamByKey { get; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// D-057：台账里机台内有重名的回路（<see cref="MakeKey"/>）。这些回路
        /// 只按身份键解析行序/上游；图框没有可用的键时归“同名回路待确认”。
        /// </summary>
        public ISet<string> DuplicateNames { get; }
            = new HashSet<string>(IdentityTextNormalizer.Comparer);

        public static string MakeKey(string machineId, string circuitName)
            => (machineId ?? "").Trim() + "\u001f" + (circuitName ?? "").Trim();

        /// <summary>
        /// 登记一条台账行：按身份键记行序/上游；名字在机台内唯一时同时记到
        /// 按名字典（兼容无键的老图框）。调用方按 Excel 行序逐行调用。
        /// </summary>
        public void AddRow(MachineRow row, int order)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.CircuitName)) return;
            string key = UNCAD.Core.Fill.CircuitIdentityKey.Of(row);
            string nameKey = MakeKey(row.MachineId, row.CircuitName);
            RowOrderByKey[key] = order;
            UpstreamByKey[key] = (row.Fr ?? "").Trim();
            if (DuplicateNames.Contains(nameKey)) return;
            if (RowOrder.ContainsKey(nameKey))
            {
                // 同名第二行出现：这个名字不再唯一，撤掉按名映射。
                RowOrder.Remove(nameKey);
                UpstreamOf.Remove(nameKey);
                DuplicateNames.Add(nameKey);
                return;
            }
            RowOrder[nameKey] = order;
            UpstreamOf[nameKey] = (row.Fr ?? "").Trim();
        }
    }

    /// <summary>Arranges frames into rows (one per machine, or per upstream) with a fixed gap.</summary>
    public static class XLayoutLayout
    {
        public const double DefaultSpacing = 10000d;
        /// <summary>按上游排列时，没有上游编号的回路所在的行首标签。</summary>
        public const string UnassignedUpstreamLabel = "未配上游";
        /// <summary>
        /// D-057：按上游排列时，机台内有同名回路而图框没有可用身份键（未记录
        /// 上游编号）的图框所在的行——说不出它属于哪个上游，单独一行提醒
        /// 先做 U1F/U1U 指认，不塞进“未配上游”冒充台账缺项。
        /// </summary>
        public const string AmbiguousUpstreamLabel = "同名回路待确认";

        public static IReadOnlyList<XLayoutPlacement> Arrange(
            IEnumerable<XLayoutFrameItem> items, double spacing = DefaultSpacing)
            => Arrange(items, XLayoutArrangeContext.Empty,
                XLayoutArrangeMode.MachineReadiness, spacing);

        public static IReadOnlyList<XLayoutPlacement> Arrange(
            IEnumerable<XLayoutFrameItem> items, XLayoutArrangeContext context,
            XLayoutArrangeMode mode, double spacing = DefaultSpacing)
        {
            if (spacing < 0) throw new ArgumentOutOfRangeException(nameof(spacing));
            context = context ?? XLayoutArrangeContext.Empty;
            var valid = (items ?? Enumerable.Empty<XLayoutFrameItem>())
                .Where(item => item != null).ToList();

            List<Row> rows = mode == XLayoutArrangeMode.Upstream
                ? BuildUpstreamRows(valid, context)
                : BuildMachineRows(valid, context, mode);
            SortRowMembers(rows, context);

            var result = new List<XLayoutPlacement>();
            double nextRowTop = 0d;
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                Row row = rows[rowIndex];
                double nextX = 0d;
                double rowHeight = row.Items.Count == 0
                    ? 0d : row.Items.Max(item => item.Boundary.Height);
                foreach (XLayoutFrameItem item in row.Items)
                {
                    // Align the top edge of every frame in a row. The next row starts
                    // exactly spacing units below the lowest frame in the current row.
                    result.Add(new XLayoutPlacement(item,
                        nextX - item.Boundary.MinX,
                        nextRowTop - item.Boundary.MaxY,
                        rowIndex, row.Label));
                    nextX += item.Boundary.Width + spacing;
                }
                nextRowTop -= rowHeight + spacing;
            }
            return result;
        }

        private sealed class Row
        {
            public string Label;
            public int SortRank;
            public string SortKey = "";
            public List<XLayoutFrameItem> Items = new List<XLayoutFrameItem>();
        }

        private static List<Row> BuildMachineRows(List<XLayoutFrameItem> items,
            XLayoutArrangeContext context, XLayoutArrangeMode mode)
        {
            return items
                .GroupBy(item => item.MachineId ?? "", IdentityTextNormalizer.Comparer)
                .Select(group =>
                {
                    string machineId = group.Key.Trim();
                    context.ReadinessRank.TryGetValue(machineId, out int rank);
                    return new Row
                    {
                        Label = machineId,
                        // MachineIdOrder 模式全部同组（rank=0），纯按 ID 字母序。
                        SortRank = mode == XLayoutArrangeMode.MachineIdOrder ? 0 : rank,
                        SortKey = machineId,
                        Items = group.ToList()
                    };
                })
                .OrderBy(row => row.SortRank)
                .ThenBy(row => row.SortKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// D-057：按上游排列。同一上游的不同写法（带槽位/轴位标注）归同一行；
        /// 同名回路且图框无可用身份键的归“同名回路待确认”行（排最后）。
        /// </summary>
        private static List<Row> BuildUpstreamRows(List<XLayoutFrameItem> items,
            XLayoutArrangeContext context)
        {
            return items
                .GroupBy(item => UpstreamBucketKey(context, item),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    XLayoutFrameItem first = group.First();
                    bool ambiguous = IsAmbiguous(context, first);
                    string upstream = UpstreamOf(context, first);
                    bool unassigned = !ambiguous && upstream.Length == 0;
                    return new Row
                    {
                        Label = ambiguous ? AmbiguousUpstreamLabel
                            : unassigned ? UnassignedUpstreamLabel
                            : UNCAD.Core.Fill.CircuitIdentityKey.UpstreamDisplay(upstream),
                        // 未配上游的行排最后；同名待确认更后；其余按上游编号字母序。
                        SortRank = ambiguous ? 2 : unassigned ? 1 : 0,
                        SortKey = unassigned ? "" : upstream,
                        Items = group.ToList()
                    };
                })
                .OrderBy(row => row.SortRank)
                .ThenBy(row => row.SortKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string UpstreamBucketKey(XLayoutArrangeContext context,
            XLayoutFrameItem item)
        {
            if (IsAmbiguous(context, item)) return "\u0002ambiguous";
            string key = UNCAD.Core.Fill.CircuitIdentityKey.UpstreamKey(
                UpstreamOf(context, item));
            return key.Length == 0 ? "\u0001none" : key;
        }

        /// <summary>
        /// 图框没有可用的身份键（老图框/上游改挂后键过期），而它的回路名在
        /// 台账里又有重名：行序和上游都无法确定。
        /// </summary>
        private static bool IsAmbiguous(XLayoutArrangeContext context,
            XLayoutFrameItem item)
        {
            if (item.CircuitKey.Length > 0
                && (context.RowOrderByKey.ContainsKey(item.CircuitKey)
                    || context.UpstreamByKey.ContainsKey(item.CircuitKey)))
                return false;
            return context.DuplicateNames.Contains(XLayoutArrangeContext.MakeKey(
                item.MachineId, item.DeviceName));
        }

        /// <summary>图框对应台账行的上游编号：身份键优先，其次机台内唯一的回路名。</summary>
        public static string UpstreamOf(XLayoutArrangeContext context,
            XLayoutFrameItem item)
        {
            if (item == null) return "";
            if (item.CircuitKey.Length > 0
                && context.UpstreamByKey.TryGetValue(item.CircuitKey, out string keyed))
                return keyed ?? "";
            if (IsAmbiguous(context, item)) return "";
            return context.UpstreamOf.TryGetValue(
                XLayoutArrangeContext.MakeKey(item.MachineId, item.DeviceName),
                out string upstream) ? upstream ?? "" : "";
        }

        /// <summary>行内顺序：Excel 行序优先（身份键 → 唯一回路名；缺失映射的排末尾），
        /// 再按图上位置稳定排序。</summary>
        private static void SortRowMembers(List<Row> rows,
            XLayoutArrangeContext context)
        {
            foreach (Row row in rows)
            {
                row.Items = row.Items
                    .OrderBy(item => OrderOf(context, item))
                    .ThenBy(item => item.Boundary.MinX)
                    .ThenBy(item => item.Boundary.MinY)
                    .ThenBy(item => item.Handle, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static int OrderOf(XLayoutArrangeContext context,
            XLayoutFrameItem item)
        {
            if (item.CircuitKey.Length > 0
                && context.RowOrderByKey.TryGetValue(item.CircuitKey, out int keyed))
                return keyed;
            if (IsAmbiguous(context, item)) return int.MaxValue;
            return context.RowOrder.TryGetValue(
                XLayoutArrangeContext.MakeKey(item.MachineId, item.DeviceName),
                out int order) ? order : int.MaxValue;
        }
    }
}
