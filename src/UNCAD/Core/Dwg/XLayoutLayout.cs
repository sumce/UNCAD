using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Geometry;
using UNCAD.Core.Text;

namespace UNCAD.Core.Dwg
{
    /// <summary>One source frame used by XLAYOUT.</summary>
    public sealed class XLayoutFrameItem : DwgFrameLayoutItem
    {
        public XLayoutFrameItem(string machineId, string deviceName, FrameRectangle boundary,
            string handle = "") : base(machineId, deviceName, boundary)
        {
            Handle = handle ?? "";
        }

        public string Handle { get; }
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

        /// <summary>机台ID+回路 → Excel 行序（越小越靠前；缺失的回路排在本组末尾）。</summary>
        public IDictionary<string, int> RowOrder { get; }
            = new Dictionary<string, int>();

        /// <summary>机台ID+回路 → 上游编号（U_上游编号；空白视为未配置）。</summary>
        public IDictionary<string, string> UpstreamOf { get; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string MakeKey(string machineId, string circuitName)
            => (machineId ?? "").Trim() + "\u001f" + (circuitName ?? "").Trim();
    }

    /// <summary>Arranges frames into rows (one per machine, or per upstream) with a fixed gap.</summary>
    public static class XLayoutLayout
    {
        public const double DefaultSpacing = 10000d;
        /// <summary>按上游排列时，没有上游编号的回路所在的行首标签。</summary>
        public const string UnassignedUpstreamLabel = "未配上游";

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

        private static List<Row> BuildUpstreamRows(List<XLayoutFrameItem> items,
            XLayoutArrangeContext context)
        {
            return items
                .GroupBy(item => UpstreamOf(context, item),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    string upstream = group.Key.Trim();
                    bool unassigned = upstream.Length == 0;
                    return new Row
                    {
                        Label = unassigned ? UnassignedUpstreamLabel : upstream,
                        // 未配上游的行排最后；其余按上游编号字母序。
                        SortRank = unassigned ? 1 : 0,
                        SortKey = upstream,
                        Items = group.ToList()
                    };
                })
                .OrderBy(row => row.SortRank)
                .ThenBy(row => row.SortKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string UpstreamOf(XLayoutArrangeContext context,
            XLayoutFrameItem item)
        {
            return context.UpstreamOf.TryGetValue(
                XLayoutArrangeContext.MakeKey(item.MachineId, item.DeviceName),
                out string upstream) ? upstream ?? "" : "";
        }

        /// <summary>行内顺序：Excel 行序优先（缺失映射的排末尾），再按图上位置稳定排序。</summary>
        private static void SortRowMembers(List<Row> rows,
            XLayoutArrangeContext context)
        {
            foreach (Row row in rows)
            {
                row.Items = row.Items
                    .OrderBy(item => context.RowOrder.TryGetValue(
                        XLayoutArrangeContext.MakeKey(item.MachineId, item.DeviceName),
                        out int order) ? order : int.MaxValue)
                    .ThenBy(item => item.Boundary.MinX)
                    .ThenBy(item => item.Boundary.MinY)
                    .ThenBy(item => item.Handle, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }
}
