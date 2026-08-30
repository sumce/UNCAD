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

        private static string NormalizeName(string value)
            => new string((value ?? "").Where(character =>
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
    }
}
