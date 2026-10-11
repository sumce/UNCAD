using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.Fill
{
    public static class DynamicBlockStatePolicy
    {
        public const string DeviceBlockName = "Device_Build20260716";
        public const string UpstreamBlockName = "upstream";

        public static bool IsDeviceBlock(string effectiveName)
            => NormalizeName(effectiveName) == NormalizeName(DeviceBlockName);

        public static bool IsUpstreamBlock(string effectiveName)
            => NormalizeName(effectiveName) == NormalizeName(UpstreamBlockName);

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
