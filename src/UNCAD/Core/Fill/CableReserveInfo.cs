using System;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// U1U cable reserve calculation result:
    /// total cable = measured drawing length + panel reserve + device reserve.
    /// Distances are stored in meters; reserves are kept in millimetres because the
    /// business rules (I-Line 3400 / socket panel 1200 / busbar connector 500) are
    /// authored in millimetres.
    /// </summary>
    public sealed class CableReserveInfo
    {
        /// <summary>Measured cable length from the drawing, in meters.</summary>
        public double MeasuredMeters { get; set; }

        /// <summary>Panel-side reserve selected from the U_ upstream type, in millimetres.</summary>
        public double PanelReserveMillimeters { get; set; }

        /// <summary>Device-side reserve confirmed by the user, in millimetres.</summary>
        public double DeviceReserveMillimeters { get; set; }

        /// <summary>Total cable length (measured + reserves), in meters.</summary>
        public double TotalMeters { get; set; }

        /// <summary>An all-zero reserve info.</summary>
        public static CableReserveInfo Empty => new CableReserveInfo();
    }
}
