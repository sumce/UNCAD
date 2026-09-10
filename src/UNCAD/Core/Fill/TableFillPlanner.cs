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
        Manual,
        // Appended to preserve the numeric values of the existing public categories.
        OutletPanel
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
            @"(\d+)\s*P\s*(\d+)\s*A", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
            // Keep every planner caller on the same cable-only hose diameter contract.
            FlexibleConduitCableMap.ApplyTo(machine);
            catalog = catalog ?? new BoqCatalogIndex(null);
            stat = stat ?? new CableStatResult();
            options = options ?? FillPlanningOptions.Default;
            var rows = new List<TableFillRow>();

            AddCable(rows, machine, catalog, stat);
            AddBridges(rows, catalog, stat);
            AddRigidConduits(rows, catalog, stat);
            AddFlexibleConduit(rows, machine, catalog, options);
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
            // 特殊清单 8.5「设备接地独立连接」:电缆型号为 1*16(单芯 16mm²
            // 接地线)时不走电缆类,固定映射到 8.5。
            if (IsGroundingCableModel(model))
            {
                ListItem grounding = catalog.FindByCode("8.5");
                rows.Add(FromItem(TableFillCategory.Manual, 100, grounding,
                    "设备接地独立连接",
                    grounding?.Feature ?? "1.名称:设备独立接地连接",
                    string.IsNullOrWhiteSpace(grounding?.Unit) ? "m" : grounding.Unit,
                    TableFillFormatter.CableQuantity(stat)));
                return;
            }
            ListItem item = catalog.FindCable(model);
            rows.Add(FromItem(TableFillCategory.Cable, 100, item,
                "电缆",
                model.Length > 0 ? string.Format(FillTemplates.CableDesc, model) : "1.名称:电缆",
                "M", TableFillFormatter.CableQuantity(stat)));
        }

        /// <summary>
        /// 1*16(含 1x16 / 1*16mm2 及 "ZB-YJVR-1*16" 等完整型号写法)=
        /// 设备接地独立连接专用接地线,不属于电缆类清单。
        /// </summary>
        internal static bool IsGroundingCableModel(string model)
        {
            string text = (model ?? "").Trim().ToLowerInvariant()
                .Replace(" ", "").Replace("×", "*").Replace("x", "*")
                .Replace("mm2", "").Replace("mm²", "");
            if (text == "1*16") return true;
            // 完整型号前缀(ZB-YJVR-1*16 等)按固定清单同款规则剥前缀。
            return BoqCatalogIndex.NormalizeCable(text) == "1*16";
        }

        private static void AddBridges(List<TableFillRow> rows, BoqCatalogIndex catalog,
            CableStatResult stat)
        {
            int index = 0;
            foreach (BridgeStat bridge in stat.Bridges)
            {
                string spec = NormalizeBridgeSpec(bridge.Spec);
                ListItem item = catalog.FindBridge(spec);
                // D-016：未匹配的桥架没有固定清单编码，不得写出一行无编码的清单。
                // 在 CAD 事务之前失败，由调用方整体中止本次操作。
                if (item == null)
                {
                    throw new System.IO.InvalidDataException(
                        "固定清单中没有桥架规格“" + bridge.Spec
                            + "”。请在固定清单中补充对应项目后重试。");
                }
                // 图框显示 BOQ 型号（梯形桥架200Wx100H）；型号存在项目特征的 1.名称 段。
                bridge.CatalogModel = BoqFeatureName.Extract(item.Feature);
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
            BoqCatalogIndex catalog, FillPlanningOptions options)
        {
            // Hose diameter is derived exclusively from the cable specification.
            // Never reuse the workbook hose column or infer it from rigid-conduit statistics.
            string diameter = ConduitDiameter.NormalizeOrEmpty(machine?.Dia);
            if (diameter.Length == 0) return;
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
                // NEXT identifies the upstream physical panel as well as the
                // downstream outlet state.  Keep the two material rows separate:
                // 4.11/4.12 is the panel, while 8.2/8.3 is the outlet itself.
                TableFillRow panel = BuildOutletPanelRow(machine.Detail, catalog);
                if (panel != null) rows.Add(panel);
                rows.Add(BuildOutletRow(machine.Detail, catalog));
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

        public static TableFillRow BuildOutletRow(string detail, BoqCatalogIndex catalog)
        {
            catalog = catalog ?? new BoqCatalogIndex(null);
            TryExtractRating(detail, out _, out int amps);
            ListItem item = catalog.Outlets.FirstOrDefault(candidate =>
                RangeContains(candidate.Spec, amps));
            string description = "1.名称:插座"
                + (amps > 0 ? @"\P2.额定电流:" + amps + "A" : "");
            return FromItem(TableFillCategory.Outlet, 800, item,
                "插座", description, "个", "1");
        }

        /// <summary>
        /// Builds the upstream socket-panel row for NEXT=插座盘.  A panel row is
        /// emitted only when the catalog has panel entries; with a partial/legacy
        /// test catalog there is no safe material identity to write.  When panel
        /// entries exist but the rating is unsupported, an unmatched row is kept
        /// for explicit review rather than guessing 4.11 or 4.12.
        /// </summary>
        public static TableFillRow BuildOutletPanelRow(string detail,
            BoqCatalogIndex catalog)
        {
            catalog = catalog ?? new BoqCatalogIndex(null);
            if (catalog.OutletPanels.Count == 0) return null;

            TryExtractRating(detail, out _, out int amps);
            ListItem item = catalog.FindOutletPanel(amps);
            string description = "1.名称:插座盘"
                + (amps > 0 ? @"\P2.额定电流:" + amps + "A" : "");
            return FromItem(TableFillCategory.OutletPanel, 450, item,
                "插座盘", description, "个", "1");
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
