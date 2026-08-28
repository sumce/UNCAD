using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UNCAD.Core.Excel;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    public enum TableFillCategory
    {
        Cable,
        Bridge,
        RigidConduit,
        FlexibleConduit,
        BusPlugBox,
        Breaker,
        Outlet
    }

    public sealed class TableFillRow
    {
        public TableFillCategory Category { get; set; }
        public int SortOrder { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public string Quantity { get; set; }
        public string Code { get; set; }
    }

    /// <summary>根据 Excel 回路、框选统计和 BOQ 清单生成有序表格行。</summary>
    public static class TableFillPlanner
    {
        private static readonly Regex DetailRatingRegex = new Regex(
            @"(\d+)P(\d+)A", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BreakerSpecRegex = new Regex(
            @"^(\d+)P(\d+)\s*[~～-]\s*(\d+)A$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RangeRegex = new Regex(
            @"(\d+)\s*[~～-]\s*(\d+)A", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new Regex(
            @"([0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);

        public static List<TableFillRow> Build(
            MachineRow machine, List<ListItem> items, CableStatResult stat)
        {
            machine = machine ?? new MachineRow();
            items = items ?? new List<ListItem>();
            stat = stat ?? new CableStatResult();
            var rows = new List<TableFillRow>();

            AddCable(rows, machine, items, stat);
            AddBridges(rows, items, stat);
            AddRigidConduits(rows, items, stat);
            AddFlexibleConduit(rows, machine, items, stat);
            AddNextEquipment(rows, machine, items);

            return rows.OrderBy(r => r.SortOrder)
                .ThenBy(r => r.Code ?? "", StringComparer.Ordinal)
                .ToList();
        }

        private static void AddCable(List<TableFillRow> rows, MachineRow machine,
            List<ListItem> items, CableStatResult stat)
        {
            string model = (machine.Cable ?? "").Trim();
            if (model.Length == 0 && stat.CableSum <= 0) return;
            ListItem item = ListItemReader.FindCable(items, model);
            rows.Add(FromItem(TableFillCategory.Cable, 100, item,
                "电缆",
                model.Length > 0 ? string.Format(FillTemplates.CableDesc, model) : "1.名称:电缆",
                "M", TableFillFormatter.CableQuantity(stat)));
        }

        private static void AddBridges(List<TableFillRow> rows, List<ListItem> items, CableStatResult stat)
        {
            int index = 0;
            foreach (BridgeStat bridge in stat.Bridges)
            {
                string spec = NormalizeBridgeSpec(bridge.Spec);
                ListItem item = items.FirstOrDefault(i => StartsWithCode(i, "2.")
                    && NormalizeSpec(i.Spec) == NormalizeSpec(spec));
                rows.Add(FromItem(TableFillCategory.Bridge, 200 + index++, item,
                    bridge.Spec, "1.名称:" + bridge.Spec, "M",
                    TextFormatter.FormatNum(bridge.TotalM)));
            }
        }

        private static void AddRigidConduits(List<TableFillRow> rows,
            List<ListItem> items, CableStatResult stat)
        {
            int index = 0;
            foreach (ConduitStat conduit in stat.Conduits)
            {
                string diameter = ExtractNumber(conduit.Spec);
                ListItem item = FindRigidConduit(items, diameter);
                rows.Add(FromItem(TableFillCategory.RigidConduit, 300 + index++, item,
                    conduit.Spec, "1.名称:" + conduit.Spec, "M",
                    TextFormatter.FormatNum(conduit.TotalM)));
            }
        }

        private static void AddFlexibleConduit(List<TableFillRow> rows,
            MachineRow machine, List<ListItem> items, CableStatResult stat)
        {
            string diameter = (machine.Dia ?? "").Trim();
            if (diameter.Length == 0) diameter = InferSingleConduitDiameter(stat);
            rows.Add(BuildFlexibleConduitRow(diameter, items));
        }

        public static TableFillRow BuildFlexibleConduitRow(
            string diameter, List<ListItem> items)
        {
            diameter = (diameter ?? "").Trim();
            items = items ?? new List<ListItem>();
            ListItem item = FindFlexibleConduit(items, diameter);
            string description = diameter.Length > 0
                ? string.Format(FillTemplates.ConduitDesc, diameter)
                : "1.名称:包塑金属软管";
            return FromItem(TableFillCategory.FlexibleConduit, 400, item,
                "包塑金属软管", description, "M",
                TableFillFormatter.FlexibleConduitQuantity());
        }

        private static void AddNextEquipment(List<TableFillRow> rows,
            MachineRow machine, List<ListItem> items)
        {
            string next = (machine.Next ?? "").Trim();
            TryExtractRating(machine.Detail, out int poles, out int amps);

            if (string.Equals(next, "I-Line盘", StringComparison.OrdinalIgnoreCase))
            {
                ListItem item = items.FirstOrDefault(i => StartsWithCode(i, "6.")
                    && (i.Name ?? "").IndexOf("断路器", StringComparison.Ordinal) >= 0
                    && BreakerRangeContains(i.Spec, poles, amps));
                string model = poles > 0 && amps > 0 ? poles + "P" + amps + "A" : "";
                rows.Add(FromItem(TableFillCategory.Breaker, 600, item,
                    "断路器", "1.名称:断路器 " + model + "(I-LINE)", "个", "1"));
                return;
            }

            if (string.Equals(next, "插座盘", StringComparison.OrdinalIgnoreCase))
            {
                ListItem item = items.FirstOrDefault(i => StartsWithCode(i, "8.")
                    && (i.Name ?? "").IndexOf("插座", StringComparison.Ordinal) >= 0
                    && RangeContains(i.Spec, amps));
                string description = "1.名称:插座"
                    + (amps > 0 ? "\\P2.额定电流:" + amps + "A" : "");
                rows.Add(FromItem(TableFillCategory.Outlet, 800, item,
                    "插座", description, "个", "1"));
                return;
            }

            if (string.Equals(next, "母线插接口", StringComparison.OrdinalIgnoreCase))
            {
                string model = amps > 0 ? amps + "A" : "";
                ListItem item = items.FirstOrDefault(i => StartsWithCode(i, "5.")
                    && (i.Name ?? "").IndexOf("母线插接箱", StringComparison.Ordinal) >= 0
                    && string.Equals(NormalizeSpec(i.Spec), NormalizeSpec(model),
                        StringComparison.OrdinalIgnoreCase));
                string description = "1.名称:SQ-D PLUG-IN " + model + " 母线插接开关箱";
                rows.Add(FromItem(TableFillCategory.BusPlugBox, 500, item,
                    "母线插接箱", description, "个", "1"));
            }
        }

        private static TableFillRow FromItem(TableFillCategory category, int order,
            ListItem item, string fallbackName, string fallbackDescription,
            string fallbackUnit, string quantity)
        {
            return new TableFillRow
            {
                Category = category,
                SortOrder = order,
                Name = ValueOrFallback(item?.Name, fallbackName),
                Description = ToCadText(ValueOrFallback(item?.Feature, fallbackDescription)),
                Unit = NormalizeUnit(ValueOrFallback(item?.Unit, fallbackUnit)),
                Quantity = quantity ?? "",
                Code = item?.Code ?? ""
            };
        }

        private static bool StartsWithCode(ListItem item, string prefix)
        {
            return (item?.Code ?? "").StartsWith(prefix, StringComparison.Ordinal);
        }

        private static ListItem FindRigidConduit(List<ListItem> items, string diameter)
        {
            if (string.IsNullOrWhiteSpace(diameter)) return null;
            List<ListItem> rigid = items.Where(item => StartsWithCode(item, "3.")
                && (item.Name ?? "").IndexOf("线管", StringComparison.Ordinal) >= 0
                && (item.Name ?? "").IndexOf("软管", StringComparison.Ordinal) < 0)
                .ToList();
            ListItem exact = rigid.FirstOrDefault(item =>
                NormalizeSpec(item.Spec) == NormalizeSpec(diameter + "mm"));
            if (exact != null) return exact;

            // 图纸沿用公称直径32，现有BOQ模板使用1-1/2英寸对应的38mm。
            if (string.Equals(diameter, "32", StringComparison.Ordinal))
                return rigid.FirstOrDefault(item =>
                    NormalizeSpec(item.Spec) == NormalizeSpec("38mm"));
            return null;
        }

        private static ListItem FindFlexibleConduit(List<ListItem> items, string diameter)
        {
            if (string.IsNullOrWhiteSpace(diameter)) return null;
            List<ListItem> flexible = items.Where(item => StartsWithCode(item, "3.")
                && (item.Name ?? "").IndexOf("软管", StringComparison.Ordinal) >= 0)
                .ToList();
            ListItem exact = flexible.FirstOrDefault(item =>
                NormalizeSpec(item.Spec) == NormalizeSpec(diameter + "mm"));
            if (exact != null) return exact;
            if (string.Equals(diameter, "32", StringComparison.Ordinal))
                return flexible.FirstOrDefault(item =>
                    NormalizeSpec(item.Spec) == NormalizeSpec("38mm"));
            return null;
        }

        private static string InferSingleConduitDiameter(CableStatResult stat)
        {
            List<string> diameters = (stat?.Conduits ?? new List<ConduitStat>())
                .Select(item => ExtractNumber(item.Spec))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return diameters.Count == 1 ? diameters[0] : "";
        }

        private static string NormalizeBridgeSpec(string value)
        {
            string s = value ?? "";
            if (s.StartsWith("桥架", StringComparison.Ordinal)) s = s.Substring(2);
            return s;
        }

        private static string NormalizeSpec(string value)
        {
            return Regex.Replace(value ?? "", @"\s+", "")
                .Replace("x", "*").Replace("X", "*").ToUpperInvariant();
        }

        private static string ExtractNumber(string value)
        {
            var match = NumberRegex.Match(value ?? "");
            return match.Success ? match.Groups[1].Value : "";
        }

        private static bool TryExtractRating(string detail, out int poles, out int amps)
        {
            MatchCollection matches = DetailRatingRegex.Matches(detail ?? "");
            if (matches.Count == 0)
            {
                poles = 0;
                amps = 0;
                return false;
            }
            Match match = matches[matches.Count - 1];
            bool hasPoles = int.TryParse(match.Groups[1].Value, out poles);
            bool hasAmps = int.TryParse(match.Groups[2].Value, out amps);
            return hasPoles && hasAmps;
        }

        private static bool BreakerRangeContains(string spec, int poles, int amps)
        {
            if (poles <= 0 || amps <= 0) return false;
            Match match = BreakerSpecRegex.Match(NormalizeSpec(spec));
            if (!match.Success) return false;
            int itemPoles = int.Parse(match.Groups[1].Value);
            int min = int.Parse(match.Groups[2].Value);
            int max = int.Parse(match.Groups[3].Value);
            return itemPoles == poles && amps >= min && amps <= max;
        }

        private static bool RangeContains(string spec, int amps)
        {
            if (amps <= 0) return false;
            Match match = RangeRegex.Match(spec ?? "");
            if (!match.Success) return false;
            int min = int.Parse(match.Groups[1].Value);
            int max = int.Parse(match.Groups[2].Value);
            return amps >= min && amps <= max;
        }

        private static string ValueOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback ?? "" : value.Trim();
        }

        private static string NormalizeUnit(string value)
        {
            string unit = (value ?? "").Trim();
            return unit.Equals("m", StringComparison.OrdinalIgnoreCase) ? "M" : unit;
        }

        private static string ToCadText(string value)
        {
            return (value ?? "").Replace("\r\n", "\n").Replace("\r", "\n")
                .Replace("\n", "\\P");
        }
    }
}
