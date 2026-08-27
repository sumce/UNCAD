using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    public static class ConnectionBlockFiller
    {
        public const string TagUpstreamInfo = "UPSTREAM_INFO";
        public const string TagUpstreamAxis = "US";
        public const string TagDownstreamAxis = "DS";

        public static string UpstreamInfo(MachineRow row)
        {
            string fr = (row?.Fr ?? "").Trim();
            string detail = (row?.Detail ?? "").Trim();
            if (fr.Length == 0) return detail;
            if (detail.Length == 0) return fr;
            return fr + "\\P" + detail;
        }

        public static string UpstreamAxis(MachineRow row)
            => (row?.UpstreamAxis ?? "").Trim();

        public static string DownstreamAxis(MachineRow row)
            => (row?.DownstreamAxis ?? "").Trim();
    }
}
