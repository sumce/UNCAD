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
        Outlet,
        Manual
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
        public bool CatalogMatched { get; set; }
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
            => Build(machine, new BoqCatalogIndex(items), stat, FillPlanningOptions.Default);

        public static List<TableFillRow> Build(MachineRow machine,
            BoqCatalogIndex catalog, CableStatResult stat, FillPlanningOptions options)
        {
            machine = machine ?? new MachineRow();
            catalog = catalog ?? new BoqCatalogIndex(null);
            stat = stat ?? new CableStatResult();
            options = options ?? FillPlanningOptions.Default;
            var rows = new List<TableFillRow>();

            AddCable(rows, machine, catalog, stat);
            AddBridges(rows, catalog, stat);
            AddRigidConduits(rows, catalog, stat);
            AddFlexibleConduit(rows, machine, catalog, stat, options);
            AddNextEquipment(rows, machine, catalog);

            return rows.OrderBy(r => r.SortOrder)
                .ThenBy(r => r.Code ?? "", StringComparer.Ordinal)
                .ToList();
        }

        private static void AddCable(List<TableFillRow> rows, MachineRow machine,
            BoqCatalogIndex catalog, CableStatResult stat)
        {
            string model = (machine.Cable ?? "").Trim();
            if (model.Length == 0 && stat.CableSum <= 0) return;
            ListItem item = catalog.FindCable(model);
            rows.Add(FromItem(TableFillCategory.Cable, 100, item,
                "电缆",
                model.Length > 0 ? string.Format(FillTemplates.CableDesc, model) : "1.名称:电缆",
                "M", TableFillFormatter.CableQuantity(stat)));
        }

        private static void AddBridges(List<TableFillRow> rows, BoqCatalogIndex catalog,
            CableStatResult stat)
        {
            int index = 0;
            foreach (BridgeStat bridge in stat.Bridges)
            {
                string spec = NormalizeBridgeSpec(bridge.Spec);
                ListItem item = catalog.FindBridge(spec);
                rows.Add(FromItem(TableFillCategory.Bridge, 200 + index++, item,
                    bridge.Spec, "1.名称:" + bridge.Spec, "M",
                    TextFormatter.FormatNum(bridge.TotalM)));
            }
        }

        private static void AddRigidConduits(List<TableFillRow> rows,
            BoqCatalogIndex catalog, CableStatResult stat)
        {
            int index = 0;
            foreach (ConduitStat conduit in stat.Conduits)
            {
                string diameter = ExtractNumber(conduit.Spec);
                ListItem item = catalog.FindRigidConduit(diameter);
                rows.Add(FromItem(TableFillCategory.RigidConduit, 300 + index++, item,
                    conduit.Spec, "1.名称:" + conduit.Spec, "M",
                    TextFormatter.FormatNum(conduit.TotalM)));
            }
        }

        private static void AddFlexibleConduit(List<TableFillRow> rows, MachineRow machine,
            BoqCatalogIndex catalog, CableStatResult stat, FillPlanningOptions options)
        {
            string sourceDiameter = (machine.Dia ?? "").Trim();
            string diameter = ConduitDiameter.NormalizeOrEmpty(sourceDiameter);
            if (diameter.Length == 0)
                diameter = sourceDiameter.Length == 0
                    ? InferSingleConduitDiameter(stat)
                    : sourceDiameter;
            rows.Add(BuildFlexibleConduitRow(diameter, catalog, options));
        }

        public static TableFillRow BuildFlexibleConduitRow(
            string diameter, List<ListItem> items)
            => BuildFlexibleConduitRow(diameter, new BoqCatalogIndex(items),
                FillPlanningOptions.Default);

        public static TableFillRow BuildFlexibleConduitRow(string diameter,
            BoqCatalogIndex catalog, FillPlanningOptions options)
        {
            string sourceDiameter = (diameter ?? "").Trim();
            string normalized = ConduitDiameter.NormalizeOrEmpty(sourceDiameter);
            diameter = normalized.Length > 0 ? normalized : sourceDiameter;
            catalog = catalog ?? new BoqCatalogIndex(null);
            options = options ?? FillPlanningOptions.Default;
            ListItem item = catalog.FindFlexibleConduit(diameter);
            string description = diameter.Length > 0
                ? string.Format(FillTemplates.ConduitDesc, diameter)
                : "1.名称:包塑金属软管";
            return FromItem(TableFillCategory.FlexibleConduit, 400, item,
                "包塑金属软管", description, "M",
                TableFillFormatter.FlexibleConduitQuantity(
                    options.FlexibleConduitMeters));
        }

        private static void AddNextEquipment(List<TableFillRow> rows,
            MachineRow machine, BoqCatalogIndex catalog)
        {
            string next = (machine.Next ?? "").Trim();
            TryExtractRating(machine.Detail, out int poles, out int amps);

            if (string.Equals(next, "I-Line盘", StringComparison.OrdinalIgnoreCase))
            {
                ListItem item = catalog.Breakers.FirstOrDefault(i =>
                    BreakerRangeContains(i.Spec, poles, amps));
                string model = poles > 0 && amps > 0 ? poles + "P" + amps + "A" : "";
                rows.Add(FromItem(TableFillCategory.Breaker, 600, item,
                    "断路器", "1.名称:断路器 " + model + "(I-LINE)", "个", "1"));
                return;
            }

            if (string.Equals(next, "插座盘", StringComparison.OrdinalIgnoreCase))
            {
                ListItem item = catalog.Outlets.FirstOrDefault(i =>
                    RangeContains(i.Spec, amps));
                string description = "1.名称:插座"
                    + (amps > 0 ? "\\P2.额定电流:" + amps + "A" : "");
                rows.Add(FromItem(TableFillCategory.Outlet, 800, item,
                    "插座", description, "个", "1"));
                return;
            }

            if (string.Equals(next, "母线插接口", StringComparison.OrdinalIgnoreCase))
            {
                string model = amps > 0 ? amps + "A" : "";
                ListItem item = catalog.FindBusPlugBox(model);
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
                Code = item?.Code ?? "",
                CatalogMatched = item != null
            };
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
            Match match = BreakerSpecRegex.Match(BoqCatalogIndex.NormalizeSpec(spec));
            if (!match.Success) return false;
            if (!int.TryParse(match.Groups[1].Value, out int itemPoles)
                || !int.TryParse(match.Groups[2].Value, out int min)
                || !int.TryParse(match.Groups[3].Value, out int max)) return false;
            return itemPoles == poles && amps >= min && amps <= max;
        }

        private static bool RangeContains(string spec, int amps)
        {
            if (amps <= 0) return false;
            Match match = RangeRegex.Match(spec ?? "");
            if (!match.Success) return false;
            if (!int.TryParse(match.Groups[1].Value, out int min)
                || !int.TryParse(match.Groups[2].Value, out int max)) return false;
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
