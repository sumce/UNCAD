using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 图框块属性填充：按属性标签把机台回路数据写进选中的块参照。
    /// 标签与 TEXTS.dwg 图框（frame_20260812）一致：
    ///   MACHINEID-DEVICE   机台ID-设备名称
    ///   MACHINEID-POWER    机台ID-POWER，例如 "MDAPT01-POWER"
    ///   CABLE_INFO         电缆型号mm²: 分段长度求和公式=总长度
    ///   BRIDGE_FRAME_INFO  UNADD 桥架规格 + 总长度（无统计结果时回退配置）
    ///   CONDUIT_INFO       各管径线管总长度，多个用中文逗号分隔
    /// 可选统计标签始终返回；无数据时写空字符串，避免保留上一次填充内容。
    /// </summary>
    public static class FrameBlockFiller
    {
        public const string TagDevice = "MACHINEID-DEVICE";
        public const string TagPower = "MACHINEID-POWER";
        public const string TagCable = "CABLE_INFO";
        public const string TagBridge = "BRIDGE_FRAME_INFO";
        public const string TagConduit = "CONDUIT_INFO";

        private static readonly HashSet<string> KnownTags = new HashSet<string>(
            new[] { TagDevice, TagPower, TagCable, TagBridge, TagConduit },
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
            MachineRow row, string bridgeInfo)
        {
            return BuildValues(row, bridgeInfo, null);
        }

        /// <summary>
        /// 构建 标签→值 映射。统计结果来自 U1F 同一次框选，
        /// CABLE_INFO 写电缆公式，BRIDGE_FRAME_INFO 写桥架总长，CONDUIT_INFO 写各管径线管总长。
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row, string bridgeInfo, CableStatResult stat)
            => BuildValues(row, bridgeInfo, stat, false);

        /// <summary>
        /// Builds frame values while optionally leaving statistics-backed attributes
        /// untouched when an update did not receive a fresh measurement text.
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row, string bridgeInfo, CableStatResult stat,
            bool preserveMissingStatistics)
            => BuildValues(row, bridgeInfo, stat, preserveMissingStatistics,
                preserveMissingStatistics, preserveMissingStatistics);

        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row, string bridgeInfo, CableStatResult stat,
            bool preserveCable, bool preserveBridge, bool preserveConduit)
        {
            var d = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            string dev = DeviceText(row);
            if (dev.Length > 0) d[TagDevice] = dev;

            string pwr = PowerText(row);
            if (pwr.Length > 0) d[TagPower] = pwr;

            string cable = (row.Cable ?? "").Trim();
            if (stat != null && stat.CableSum > 0)
            {
                string total = TextFormatter.FormatNum(stat.CableSum);
                string formula = stat.CableFormatted.Count > 1
                    ? TextFormatter.Join(stat.CableFormatted, "+") + "=" + total + "M"
                    : total + "M";
                // 图框显示带工程前缀的完整型号（ZB-YJV- / ZB-YJVR-，由型号是否含
                // "+" 决定）。该前缀只出现在图框属性上；BOQ 清单编码与表格项目
                // 特征继续使用不带前缀的清单别名，两者不得互相污染。
                d[TagCable] = cable.Length > 0
                    ? CableModelDisplay.WithTypePrefix(cable) + "mm²: " + formula
                    : formula;
            }
            else
            {
                if (!preserveCable)
                    d[TagCable] = CableModelDisplay.WithTypePrefix(cable);
            }

            if (stat != null && stat.Bridges.Count > 0)
            {
                // 图框显示固定清单的 BOQ 型号（梯形桥架200Wx100H）加长度，而不是图上
                // 量的规格写法（桥架200*100）。CatalogModel 由规划阶段查清单后填入；
                // 为空时（例如未经规划直接写框）回退到规格写法，避免写出空型号。
                d[TagBridge] = string.Join("; ", stat.Bridges.Select(b =>
                    (string.IsNullOrWhiteSpace(b.CatalogModel) ? b.Spec : b.CatalogModel)
                        + " " + TextFormatter.FormatNum(b.TotalM) + "M"));
            }
            else
            {
                if (!preserveBridge) d[TagBridge] = (bridgeInfo ?? "").Trim();
            }

            if (stat != null && stat.Conduits.Count > 0)
                d[TagConduit] = string.Join("，", stat.Conduits.Select(c =>
                    c.Spec + " " + TextFormatter.FormatNum(c.TotalM) + "M"));
            else if (!preserveConduit)
                d[TagConduit] = "";

            return d;
        }
    }
}
