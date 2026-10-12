using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.Fill
{
    /// <summary>Downstream 块的可见性状态（与 upstream 同一套）。</summary>
    public enum DownstreamState
    {
        /// <summary>没有 Downstream 块、状态读不出来，或状态不是已知三态。</summary>
        Unknown = 0,
        /// <summary>socket box：额外加一行 4.x 插座箱/盘。</summary>
        SocketBox = 1,
        /// <summary>I-line_Panel：额外加一行电盘（**不加断路器**）。</summary>
        ILinePanel = 2,
        /// <summary>busbar connector：只上色，不产生物料。</summary>
        BusbarConnector = 3
    }

    public static class DynamicBlockStatePolicy
    {
        public const string DeviceBlockName = "Device_Build20260716";
        public const string UpstreamBlockName = "upstream";
        public const string DownstreamBlockName = "Downstream";

        public static bool IsDeviceBlock(string effectiveName)
            => NormalizeName(effectiveName) == NormalizeName(DeviceBlockName);

        public static bool IsUpstreamBlock(string effectiveName)
            => NormalizeName(effectiveName) == NormalizeName(UpstreamBlockName);

        /// <summary>
        /// 下游端块族：精确名 <c>Downstream</c>（含 Xmerge 的 <c>$0$</c> 后缀）。
        /// 刻意不做前缀/模糊匹配：DS 轴位等以 ds 开头的块、upstream 块都不能卷进来。
        /// </summary>
        public static bool IsDownstreamBlock(string effectiveName)
            => NormalizeName(effectiveName) == NormalizeName(DownstreamBlockName);

        /// <summary>
        /// Downstream 块的三态与 upstream 完全一致（I-line_Panel / socket box /
        /// busbar connector，同一个可见性属性）。
        ///
        /// 物料规则见 <see cref="DownstreamStatePolicy"/>：
        /// <c>socket box</c> → 额外一行 4.x 插座箱/盘；<c>I-line_Panel</c> → 额外一行电盘
        /// （额定电流来自 U1SET，默认 1000A）；<c>busbar connector</c> 只上色。
        /// <b>Downstream 永不产生断路器</b>——断路器只由上游 U_上游类型=I-Line盘 产生，
        /// 这条是用户明确要求，不要"顺手补齐"。
        /// </summary>
        public static bool TryClassifyDownstreamState(string state, out DownstreamState result)
        {
            switch (NormalizeName(state))
            {
                case "socketbox":
                    result = DownstreamState.SocketBox;
                    return true;
                case "ilinepanel":
                    result = DownstreamState.ILinePanel;
                    return true;
                case "busbarconnector":
                    result = DownstreamState.BusbarConnector;
                    return true;
                default:
                    result = DownstreamState.Unknown;
                    return false;
            }
        }

        public static bool TryClassifyDeviceState(string state, out bool hasOutlet)
        {
            string value = (state ?? "").Trim();
            if (value.IndexOf("插座", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                hasOutlet = true;
                return true;
            }
            if (value.Equals("设备", StringComparison.OrdinalIgnoreCase))
            {
                hasOutlet = false;
                return true;
            }
            hasOutlet = false;
            return false;
        }

        public static string UpstreamVisibilityState(string next)
        {
            string value = NormalizeName(next);
            if (value == "iline盘") return "I-line_Panel";
            if (value == "插座盘") return "socket box";
            if (value == "母线插接口") return "busbar connector";
            return "";
        }

        /// <summary>
        /// 下游是否为 I-Line 电盘。与动态块可见性使用同一套规范化，
        /// 使 "I-Line盘"/"Iline盘"/"I-Line 盘" 在同一个命令里得到同一结论
        /// （以前清单规划用精确比较，这三种写法只有第一种会生成电盘/断路器行）。
        /// </summary>
        public static bool IsILinePanel(string next) => NormalizeName(next) == "iline盘";

        internal static string NormalizeName(string value)
            => new string(BlockNameNormalizer.RemoveMangledSuffix(value).Where(character =>
                !char.IsWhiteSpace(character) && character != '_' && character != '-').ToArray())
                .ToLowerInvariant();
    }

    public static class DeviceOutletPolicy
    {
        public static List<TableFillRow> Apply(IEnumerable<TableFillRow> rows,
            bool hasOutlet, TableFillRow generatedOutlet)
        {
            var result = (rows ?? Enumerable.Empty<TableFillRow>())
                .Where(row => row != null && !UpdateOutletPolicy.IsOutlet(row)).ToList();
            if (hasOutlet && generatedOutlet != null) result.Add(generatedOutlet);
            result = result.OrderBy(row => row.SortOrder).ToList();
            for (int index = 0; index < result.Count; index++) result[index].SortOrder = index + 1;
            return result;
        }

        /// <summary>
        /// U1U variant that uses the current CAD table as the outlet quantity authority.
        /// An existing outlet row is retained verbatim; a new row is created with quantity
        /// one only when the device state says an outlet exists and no row is present.
        /// </summary>
        public static List<TableFillRow> ApplyForUpdate(
            IEnumerable<TableFillRow> rows, bool hasOutlet, TableFillRow generatedOutlet,
            IEnumerable<TableFillRow> existingOutlets)
            => UpdateOutletPolicy.PreserveExistingOrAdd(
                rows, hasOutlet, generatedOutlet, existingOutlets);
    }
}
