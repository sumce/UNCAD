using System;
using UNCAD.Core.Stat;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 电缆预留规则（U1F/U1U）：
    /// 总长 = 图上实测 + 盘内/箱内预留（按 U_上游类型）+ 设备内预留 + 业主统一预留。
    /// 盘内/箱内：I-Line 盘 3400（盘内）、插座盘 1000（盘内）、母线插接箱 500（箱内）；
    /// 业主统一预留：所有回路默认 3000（U1SET 可改）。
    /// </summary>
    public static class CableReservePolicy
    {
        public const double ILinePanelReserveMillimeters = 3400d;
        public const double SocketPanelReserveMillimeters = 1000d;
        public const double BusbarConnectorReserveMillimeters = 500d;
        public const double DefaultOwnerReserveMillimeters = 3000d;

        /// <summary>按 U_上游类型返回盘内/箱内预留（毫米）；未知类型返回 0。</summary>
        public static double PanelReserveMillimeters(string next)
        {
            string value = Normalize(next);
            if (value.Length == 0) return 0d;
            if (IsILine(value)) return ILinePanelReserveMillimeters;
            if (value.IndexOf("插座盘", StringComparison.Ordinal) >= 0)
                return SocketPanelReserveMillimeters;
            if (IsBusbar(value)) return BusbarConnectorReserveMillimeters;
            return 0d;
        }

        /// <summary>盘内/箱内标签：I-Line、插座盘=盘内；母线插接箱=箱内。</summary>
        public static string PanelReserveLabel(string next)
        {
            string value = Normalize(next);
            if (value.Length == 0) return "";
            if (IsILine(value) || value.IndexOf("插座盘", StringComparison.Ordinal) >= 0)
                return "盘内";
            if (IsBusbar(value)) return "箱内";
            return "";
        }

        private static bool IsILine(string value)
            => value.IndexOf("iline", StringComparison.Ordinal) >= 0
                || value.IndexOf("i-line", StringComparison.Ordinal) >= 0;

        private static bool IsBusbar(string value)
            => value.IndexOf("母线插接", StringComparison.Ordinal) >= 0
                || value.IndexOf("插接口", StringComparison.Ordinal) >= 0
                || value.IndexOf("busbar", StringComparison.Ordinal) >= 0;

        /// <summary>
        /// 计算并把预留写入统计结果：CABLE_INFO 统一由 FrameBlockFiller 格式化为
        /// 「分段 + 盘内/箱内 + 设备内 + 预留 = 总长」。
        /// </summary>
        public static CableReserveInfo Apply(CableStatResult statistics, string next,
            double deviceReserveMillimeters,
            double ownerReserveMillimeters = DefaultOwnerReserveMillimeters)
        {
            double measured = statistics == null ? 0d : statistics.CableSum;
            double panel = PanelReserveMillimeters(next);
            double device = ClampReserve(deviceReserveMillimeters);
            double owner = ClampReserve(ownerReserveMillimeters);
            var reserve = new CableReserveInfo
            {
                MeasuredMeters = measured,
                PanelReserveMillimeters = panel,
                PanelReserveLabel = PanelReserveLabel(next),
                DeviceReserveMillimeters = device,
                OwnerReserveMillimeters = owner,
                TotalMeters = measured + (panel + device + owner) / 1000d
            };
            if (statistics == null) return reserve;

            statistics.MeasuredCableSum = measured;
            statistics.PanelReserveMillimeters = panel;
            statistics.PanelReserveLabel = reserve.PanelReserveLabel;
            statistics.DeviceReserveMillimeters = device;
            statistics.OwnerReserveMillimeters = owner;
            statistics.CableReserveApplied = true;
            statistics.CableSum = reserve.TotalMeters;
            return reserve;
        }

        private static double ClampReserve(double value)
            => double.IsNaN(value) || double.IsInfinity(value) || value < 0d ? 0d : value;
        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string text = value.Trim().ToLowerInvariant();
            text = text.Replace(" ", "").Replace("\t", "").Replace("_", "")
                .Replace("　", "");
            return text;
        }
    }
}
