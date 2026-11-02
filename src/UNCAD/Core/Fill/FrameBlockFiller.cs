using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 图框块属性填充：按属性标签把机台回路数据写进选中的块参照。
    /// 标签与 xframe_v2 图框一致：
    ///   MACHINEID-DEVICE   机台ID-设备名称
    ///   MACHINEID-POWER    机台ID-POWER，例如 "MDAPT01-POWER"
    ///   CABLE_INFO         电缆型号mm²: 实测分段(mm)+盘内+设备内+预留=总长mm
    ///   CONDUIT_INFO       各管径线管总长度，多个用中文逗号分隔
    /// （BRIDGE_FRAME_INFO 已随 xframe_v2 迁移移除：桥架数据只进 BOQ 表与
    /// DATA 工作表，D-035/D-033 之后的图框不再携带该属性。）
    /// 可选统计标签始终返回；无数据时写空字符串，避免保留上一次填充内容。
    /// </summary>
    public static class FrameBlockFiller
    {
        public const string TagDevice = "MACHINEID-DEVICE";
        public const string TagPower = "MACHINEID-POWER";
        public const string TagCable = "CABLE_INFO";
        public const string TagConduit = "CONDUIT_INFO";

        private static readonly HashSet<string> KnownTags = new HashSet<string>(
            new[] { TagDevice, TagPower, TagCable, TagConduit },
            System.StringComparer.OrdinalIgnoreCase);

        public static bool IsKnownTag(string tag) => KnownTags.Contains(tag ?? "");

        /// <summary>机台ID-设备名称，如 "MDAPT01-泵1"。</summary>
        public static string DeviceText(MachineRow row)
        {
            string machine = (row.MachineId ?? "").Trim();
            string circuit = (row.CircuitName ?? "").Trim();
            if (machine.Length == 0) return circuit;
            if (circuit.Length == 0) return machine;
            return machine + "-" + circuit;
        }

        /// <summary>MACHINEID-POWER 固定写成“机台ID-POWER”。</summary>
        public static string PowerText(MachineRow row)
        {
            string machine = (row?.MachineId ?? "").Trim();
            return machine.Length > 0 ? machine + "-POWER" : "";
        }

        /// <summary>兼容入口：无框选统计结果时只填 Excel 可确定的属性。</summary>
        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row)
        {
            return BuildValues(row, null);
        }

        /// <summary>
        /// 构建 标签→值 映射。统计结果来自 U1F 同一次框选，
        /// CABLE_INFO 写电缆公式，CONDUIT_INFO 写各管径线管总长。
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row, CableStatResult stat)
            => BuildValues(row, stat, false);

        /// <summary>
        /// Builds frame values while optionally leaving statistics-backed attributes
        /// untouched when an update did not receive a fresh measurement text.
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row, CableStatResult stat,
            bool preserveMissingStatistics)
            => BuildValues(row, stat, preserveMissingStatistics,
                preserveMissingStatistics);

        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row, CableStatResult stat,
            bool preserveCable, bool preserveConduit)
        {
            var d = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            string dev = DeviceText(row);
            if (dev.Length > 0) d[TagDevice] = dev;

            string pwr = PowerText(row);
            if (pwr.Length > 0) d[TagPower] = pwr;

            string cable = (row.Cable ?? "").Trim();
            if (stat != null && stat.CableSum > 0)
            {
                // 与现场约定一致：全部 mm，明细统一写成「数值（说明）」，最后 =总长mm。
                // 例：电缆ZB-YJVR-5*4mm²: 1000+700+3400（盘内）+2000（设备内）+3000（预留）=93300mm
                double measuredMm = (stat.CableReserveApplied
                    ? stat.MeasuredCableSum : stat.CableSum) * 1000d;
                var parts = new List<string>();
                if (stat.CableFormatted.Count > 1)
                {
                    foreach (string value in stat.CableFormatted)
                        parts.Add(FormatMillimeters(ParseMeters(value) * 1000d));
                }
                else
                {
                    parts.Add(FormatMillimeters(measuredMm));
                }
                string formula;
                if (stat.CableReserveApplied)
                {
                    if (stat.PanelReserveMillimeters > 0)
                        parts.Add(FormatMillimeters(stat.PanelReserveMillimeters) + "（"
                            + (string.IsNullOrWhiteSpace(stat.PanelReserveLabel)
                                ? "盘内" : stat.PanelReserveLabel) + "）");
                    if (stat.DeviceReserveMillimeters > 0)
                        parts.Add(FormatMillimeters(stat.DeviceReserveMillimeters) + "（设备内）");
                    if (stat.OwnerReserveMillimeters > 0)
                        parts.Add(FormatMillimeters(stat.OwnerReserveMillimeters) + "（预留）");
                    formula = string.Join("+", parts) + "="
                        + FormatMillimeters(stat.CableSum * 1000d) + "mm";
                }
                else
                {
                    formula = string.Join("+", parts) + "mm";
                }
                // 图框显示带工程前缀的完整型号（ZB-YJV- / ZB-YJVR-，由型号是否含
                // "+" 决定）。该前缀只出现在图框属性上；BOQ 清单编码与表格项目
                // 特征继续使用不带前缀的清单别名，两者不得互相污染。
                d[TagCable] = cable.Length > 0
                    ? "电缆" + CableModelDisplay.WithTypePrefix(cable) + "mm²: " + formula
                    : formula;
            }
            else
            {
                if (!preserveCable)
                    d[TagCable] = cable.Length > 0
                        ? "电缆" + CableModelDisplay.WithTypePrefix(cable)
                        : "";
            }

            // 桥架不再写图框属性：BRIDGE_FRAME_INFO 已随 xframe_v2 移除，
            // 桥架数据只进 BOQ 清单表与 DATA 工作表。
            if (stat != null && stat.Conduits.Count > 0)
                d[TagConduit] = string.Join("，", stat.Conduits.Select(c =>
                    c.Spec + " " + TextFormatter.FormatNum(c.TotalM) + "M"));
            else if (!preserveConduit)
                d[TagConduit] = "";

            return d;
        }

        private static string FormatMillimeters(double millimetres)
            => TextFormatter.FormatNum(Math.Round(millimetres, 0,
                MidpointRounding.AwayFromZero));

        private static double ParseMeters(string value)
        {
            string text = (value ?? "").Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double invariant))
                return invariant;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture,
                out double current) ? current : 0d;
        }

    }
}
