using System;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// U1F/U1U cable reserve result:
    /// total = measured + 盘内/箱内（按上游类型）+ 设备内（用户确认）+ 业主统一预留。
    /// Distances are stored in meters; reserves are kept in millimetres because the
    /// business rules (I-Line 3400 盘内 / 插座盘 1000 盘内 / 插接箱 500 箱内 / 业主预留 3000) are
    /// authored in millimetres.
    /// </summary>
    public sealed class CableReserveInfo
    {
        /// <summary>Measured cable length from the drawing, in meters.</summary>
        public double MeasuredMeters { get; set; }

        /// <summary>Panel-side reserve selected from the U_ upstream type, in millimetres.</summary>
        public double PanelReserveMillimeters { get; set; }

        /// <summary>盘内/箱内：I-Line、插座盘=盘内；母线插接箱=箱内。</summary>
        public string PanelReserveLabel { get; set; } = "";

        /// <summary>Device-side reserve confirmed by the user, in millimetres.</summary>
        public double DeviceReserveMillimeters { get; set; }

        /// <summary>业主统一预留（毫米，全局配置，默认 3000）。</summary>
        public double OwnerReserveMillimeters { get; set; }

        /// <summary>Total cable length (measured + reserves), in meters.</summary>
        public double TotalMeters { get; set; }

        /// <summary>An all-zero reserve info.</summary>
        public static CableReserveInfo Empty => new CableReserveInfo();
    }
}
