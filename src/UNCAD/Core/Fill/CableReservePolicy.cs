using System;
using UNCAD.Core.Stat;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 电缆预留规则（U1U）：
    /// 总长 = 图上实测长度 + 盘内预留（按 U_上游类型）+ 设备内预留（用户首次确认后写入 frameinfo_json）。
    /// 盘内预留：I-Line 盘 3400mm、插座盘 1200mm、母线插接口 500mm。
    /// </summary>
    public static class CableReservePolicy
    {
        public const double ILinePanelReserveMillimeters = 3400d;
        public const double SocketPanelReserveMillimeters = 1200d;
        public const double BusbarConnectorReserveMillimeters = 500d;

        /// <summary>按 U_上游类型返回盘内预留（毫米）；未知类型返回 0。</summary>
        public static double PanelReserveMillimeters(string next)
        {
            string value = Normalize(next);
            if (value.Length == 0) return 0d;
            if (value.IndexOf("iline", StringComparison.Ordinal) >= 0
                || value.IndexOf("i-line", StringComparison.Ordinal) >= 0)
                return ILinePanelReserveMillimeters;
            if (value.IndexOf("插座盘", StringComparison.Ordinal) >= 0)
                return SocketPanelReserveMillimeters;
            if (value.IndexOf("母线插接", StringComparison.Ordinal) >= 0
                || value.IndexOf("插接口", StringComparison.Ordinal) >= 0
                || value.IndexOf("busbar", StringComparison.Ordinal) >= 0)
                return BusbarConnectorReserveMillimeters;
            return 0d;
        }

        /// <summary>
        /// 把盘内预留和设备内预留追加到统计结果（CableFormatted 同步追加分段，保证
        /// CABLE_INFO 的「分段和=总长」仍然成立），并返回结构化预留信息供 JSON 记录。
        /// </summary>
        public static CableReserveInfo Apply(CableStatResult statistics, string next,
            double deviceReserveMillimeters)
        {
            double measured = statistics == null ? 0d : statistics.CableSum;
            double panel = PanelReserveMillimeters(next);
            double device = double.IsNaN(deviceReserveMillimeters)
                || double.IsInfinity(deviceReserveMillimeters)
                || deviceReserveMillimeters < 0
                ? 0d
                : deviceReserveMillimeters;
            var reserve = new CableReserveInfo
            {
                MeasuredMeters = measured,
                PanelReserveMillimeters = panel,
                DeviceReserveMillimeters = device,
                TotalMeters = measured + (panel + device) / 1000d
            };
            if (statistics == null) return reserve;

            // CABLE_INFO 由 FrameBlockFiller 单独格式化为「实测 + 盘内预留 + 设备预留」，
            // 因此这里只把明细和总长写到统计结果，不往 CableFormatted 里塞隐藏分段。
            statistics.MeasuredCableSum = measured;
            statistics.PanelReserveMillimeters = panel;
            statistics.DeviceReserveMillimeters = device;
            statistics.CableReserveApplied = true;
            statistics.CableSum = reserve.TotalMeters;
            return reserve;
        }

        /// <summary>把同一统计结果上的预留恢复到实测值（避免同一命令里重复叠加）。</summary>
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
