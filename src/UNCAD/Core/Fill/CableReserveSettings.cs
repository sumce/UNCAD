using System;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 电缆盘内/箱内预留参数（毫米）：I-Line 电盘、插座盘、母线插接箱。
    /// 现场默认 3400 / 1000 / 500，允许在 U1SET 中按项目调整；不接受负值与超大值。
    /// </summary>
    public sealed class CableReserveSettings
    {
        public const double DefaultILinePanelMillimeters = 3400d;
        public const double DefaultSocketPanelMillimeters = 1000d;
        public const double DefaultBusbarConnectorMillimeters = 500d;
        public const double MaximumReserveMillimeters = 100000d;

        public static readonly CableReserveSettings Default = new CableReserveSettings(
            DefaultILinePanelMillimeters, DefaultSocketPanelMillimeters,
            DefaultBusbarConnectorMillimeters);

        public CableReserveSettings(double iLinePanelMillimeters,
            double socketPanelMillimeters, double busbarConnectorMillimeters)
        {
            ILinePanelMillimeters = Clamp(iLinePanelMillimeters,
                DefaultILinePanelMillimeters);
            SocketPanelMillimeters = Clamp(socketPanelMillimeters,
                DefaultSocketPanelMillimeters);
            BusbarConnectorMillimeters = Clamp(busbarConnectorMillimeters,
                DefaultBusbarConnectorMillimeters);
        }

        public double ILinePanelMillimeters { get; }
        public double SocketPanelMillimeters { get; }
        public double BusbarConnectorMillimeters { get; }

        private static double Clamp(double value, double fallback)
            => value >= 0 && value <= MaximumReserveMillimeters
                && !double.IsNaN(value) && !double.IsInfinity(value) ? value : fallback;
    }
}