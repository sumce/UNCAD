using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UNCAD.Core.Excel;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 图框块属性填充：按属性标签把机台回路数据写进选中的块参照。
    /// 标签与 FRAME_20261119 图框一致：
    ///   MACHINEID-DEVICE   机台ID-设备名称
    ///   MACHINEID-POWER    机台ID-POWER，例如 "MDAPT01-POWER"
    ///   CABLE_INFO         电缆 型号mm²: 实测分段(mm)+盘内+设备内+预留=总长mm
    ///   CONDUIT_INFO       旧属性兼容写入：线管型号及分段毫米数
    ///   INFO2              桥架/刚性线管型号及分段毫米数，单行显示
    /// （BRIDGE_FRAME_INFO 是旧属性；桥架明细现写入 INFO2，完整清单仍写入 BOQ 表与
    /// DATA 工作表。）
    /// 可选统计标签始终返回；无数据时写空字符串，避免保留上一次填充内容。
    /// </summary>
    public static class FrameBlockFiller
    {
        public const string TagDevice = "MACHINEID-DEVICE";
        public const string TagPower = "MACHINEID-POWER";
        public const string TagCable = "CABLE_INFO";
        public const string TagConduit = "CONDUIT_INFO";
        public const string TagInfo2 = "INFO2";

        private static readonly HashSet<string> KnownTags = new HashSet<string>(
            new[] { TagDevice, TagPower, TagCable, TagConduit, TagInfo2 },
            System.StringComparer.OrdinalIgnoreCase);
        private static readonly Regex Info2SectionPattern = new Regex(
            @"^(?<model>.+?)(?:\s*:\s*|\s+)(?:\d+(?:\.\d+)?"
                + @"(?:\s*\+\s*\d+(?:\.\d+)?)*(?:\s*=\s*\d+(?:\.\d+)?)?)mm$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        /// CABLE_INFO 写电缆公式；INFO2 写桥架和刚性线管的毫米分段。
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row, CableStatResult stat)
            => BuildValues(row, stat, false, false, false, false, null);

        /// <summary>
        /// Builds frame values while optionally leaving statistics-backed attributes
        /// untouched when an update did not receive a fresh measurement text.
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row, CableStatResult stat,
            bool preserveMissingStatistics)
            => BuildValues(row, stat, preserveMissingStatistics,
                preserveMissingStatistics, preserveMissingStatistics,
                preserveMissingStatistics, null);

        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row, CableStatResult stat,
            bool preserveCable, bool preserveConduit)
            => BuildValues(row, stat, preserveCable, preserveConduit,
                preserveCable || preserveConduit,
                preserveCable || preserveConduit, null);

        public static System.Collections.Generic.Dictionary<string, string> BuildValues(
            MachineRow row, CableStatResult stat, bool preserveCable,
            bool preserveConduit, bool preserveBridgeInfo2,
            bool preserveConduitInfo2, string existingInfo2)
        {
            var d = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            string dev = DeviceText(row);
            if (dev.Length > 0) d[TagDevice] = dev;

            string pwr = PowerText(row);
            if (pwr.Length > 0) d[TagPower] = pwr;

            string cable = (row.Cable ?? "").Trim();
            string cableDisplay = cable.Length > 0
                ? "电缆 " + CableModelDisplay.WithTypePrefix(cable) : "";
            if (stat != null && stat.CableSum > 0)
            {
                // 与现场约定一致：全部 mm，明细统一写成「数值（说明）」，最后 =总长mm。
                // 例：电缆 ZB-YJVR-5*4mm²: 1000 +700 +3400（盘内） +2000（设备内） +3000（预留）=93300mm
                // 3.0.0.15：段间用「 +」连接（空格在加号前）——MText 只能在空格/
                // 汉字处断行，"1000+5400+…"纯数字串是一整个不可断词，段数一多
                // 首行放不下整串就整串下移（首行只剩型号，用户报告 3 行实际 2 行
                // 就够）。空格给每个段边界一个断点后，型号与数字同排一行、贪心
                // 填满，行数最少；读回方只认「数值（设备内）」紧贴结构与型号
                // 前缀，段间空格不影响。
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
                    formula = string.Join(" +", parts) + "="
                        + FormatMillimeters(stat.CableSum * 1000d) + "mm";
                }
                else
                {
                    formula = string.Join(" +", parts) + "mm";
                }
                // 图框电缆信息使用“电缆 型号”格式；工程型号前缀只出现在图框属性，
                // BOQ 清单编码与表格项目特征继续使用原清单别名。
                d[TagCable] = cable.Length > 0
                    ? cableDisplay + "mm²: " + formula
                    : formula;
            }
            else
            {
                if (!preserveCable)
                    d[TagCable] = cableDisplay;
            }

            // FRAME_20261119 的 INFO2 只显示桥架和刚性线管；旧图框若仍有
            // CONDUIT_INFO，也写相同的毫米分段格式，避免旧属性继续显示米制。
            if (stat != null && stat.Conduits.Count > 0)
                d[TagConduit] = string.Join("，", stat.Conduits.Select(conduit =>
                    FormatMillimeterSection(
                        string.IsNullOrWhiteSpace(conduit.CatalogModel)
                            ? conduit.Spec : conduit.CatalogModel,
                        conduit.LengthsMm, conduit.TotalMm)));
            else if (!preserveConduit)
                d[TagConduit] = "";

            List<string> info2 = BuildInfo2Sections(stat);
            if (existingInfo2 != null || info2.Count > 0
                || !(preserveBridgeInfo2 || preserveConduitInfo2))
            {
                List<string> preserved = PreserveInfo2Sections(existingInfo2,
                    preserveBridgeInfo2, preserveConduitInfo2);
                List<string> updated = info2.Where(section => !PreserveInfo2Category(
                    section, preserveBridgeInfo2, preserveConduitInfo2)).ToList();
                d[TagInfo2] = string.Join("；", preserved.Concat(updated));
            }

            return d;
        }

        private static List<string> BuildInfo2Sections(CableStatResult stat)
        {
            var sections = new List<string>();
            if (stat != null)
            {
                foreach (BridgeStat bridge in stat.Bridges)
                {
                    string section = FormatInfo2Section(bridge.CatalogModel,
                        bridge.Grids.Select(grid => grid * bridge.MmPerGrid),
                        bridge.TotalMm);
                    if (section.Length > 0) sections.Add(section);
                }

                foreach (ConduitStat conduit in stat.Conduits)
                {
                    string diameter = ConduitDiameter.NormalizeOrEmpty(conduit.Spec);
                    string model = diameter.Length == 0
                        || string.IsNullOrWhiteSpace(conduit.CatalogModel)
                        ? "" : "镀锌穿线管" + diameter + "mm";
                    string section = FormatInfo2Section(model,
                        conduit.LengthsMm, conduit.TotalMm);
                    if (section.Length > 0) sections.Add(section);
                }
            }
            return sections;
        }

        private static string FormatInfo2Section(string model,
            IEnumerable<double> segmentLengthsMm, double totalMm)
        {
            if (string.IsNullOrWhiteSpace(model)) return "";
            string[] parts = (segmentLengthsMm ?? Enumerable.Empty<double>())
                .Select(FormatMillimeters)
                .Where(value => value.Length > 0).ToArray();
            if (parts.Length == 0) return "";

            string lengths = parts.Length == 1
                ? parts[0] + "mm"
                : string.Join(" +", parts) + " = "
                    + FormatMillimeters(totalMm) + "mm";
            return model.Trim() + " " + lengths;
        }

        private static string FormatMillimeterSection(string model,
            IEnumerable<double> segmentLengthsMm, double totalMm)
        {
            if (string.IsNullOrWhiteSpace(model)) return "";
            string[] parts = (segmentLengthsMm ?? Enumerable.Empty<double>())
                .Select(FormatMillimeters)
                .Where(value => value.Length > 0).ToArray();
            if (parts.Length == 0) return "";

            string lengths = parts.Length == 1
                ? parts[0] + "mm"
                : string.Join(" +", parts) + " = "
                    + FormatMillimeters(totalMm) + "mm";
            return model.Trim() + ": " + lengths;
        }

        private static List<string> PreserveInfo2Sections(string existingInfo2,
            bool preserveBridge, bool preserveConduit)
        {
            var preserved = new List<string>();
            if (string.IsNullOrWhiteSpace(existingInfo2)) return preserved;
            foreach (string source in existingInfo2.Split('；'))
            {
                string section = (source ?? "").Trim();
                if (section.Length == 0) continue;
                if (PreserveInfo2Category(section, preserveBridge, preserveConduit))
                    preserved.Add(section);
            }
            return preserved;
        }

        private static bool PreserveInfo2Category(string section, bool preserveBridge,
            bool preserveConduit)
        {
            string model = Info2Model(section);
            if (model.StartsWith("梯形桥架", StringComparison.OrdinalIgnoreCase))
                return preserveBridge;
            if (model.StartsWith("镀锌穿线管", StringComparison.OrdinalIgnoreCase))
                return preserveConduit;
            return false;
        }

        private static string Info2Model(string section)
        {
            Match match = Info2SectionPattern.Match((section ?? "").Trim());
            return match.Success ? match.Groups["model"].Value.Trim() : "";
        }

        private static string FormatMillimeters(double millimetres)
            => double.IsNaN(millimetres) || double.IsInfinity(millimetres)
                ? "" : TextFormatter.FormatNum(Math.Round(millimetres, 0,
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
