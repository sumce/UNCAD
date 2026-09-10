using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UNCAD.Core.Text;

namespace UNCAD.Core.Excel
{
    public sealed class BoqCatalogIndex
    {
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
        private readonly Dictionary<string, ListItem> _migrationAliases;
        private readonly Dictionary<string, ListItem> _cables;
        private readonly List<ListItem> _cableCandidates;
        private readonly Dictionary<string, ListItem> _bridges;
        private readonly Dictionary<string, ListItem> _rigidConduits;
        private readonly Dictionary<string, ListItem> _flexibleConduits;
        private readonly Dictionary<string, ListItem> _busPlugBoxes;
        private readonly List<ListItem> _breakers;
        private readonly List<ListItem> _outlets;
        private readonly List<ListItem> _outletPanels;

        // Socket-panel rows are deliberately mapped by the approved BOQ code.  The
        // catalog contains two other panel shapes that mention 16A/30A, so a broad
        // range/substring match would silently select the wrong physical panel.
        private static readonly IReadOnlyDictionary<int, string> OutletPanelCodes =
            new Dictionary<int, string>
            {
                { 20, "4.11" },
                { 16, "4.12" }
            };

        public BoqCatalogIndex(IEnumerable<ListItem> items)
        {
            Items = (items ?? Enumerable.Empty<ListItem>()).Where(item => item != null).ToList();
            _migrationAliases = MigrationIndex(Items);
            // “类”是自动判定的唯一边界，“别名”是边界内唯一比较值。项目编码、
            // 名称和项目特征都不能反推类别；没有“类”的清单行因此不会被自动命中。
            _cableCandidates = CategoryItems("电缆").Where(HasPrimaryAlias)
                .OrderBy(item => item.Code ?? "", StringComparer.Ordinal).ToList();
            _cables = AliasIndex(_cableCandidates, "电缆");
            _bridges = AliasIndex(CategoryItems("桥架"), "桥架");
            _rigidConduits = AliasIndex(CategoryItems("线管"), "线管");
            _flexibleConduits = AliasIndex(CategoryItems("软管"), "软管");
            _busPlugBoxes = AliasIndex(CategoryItems("母线插接箱"), "母线插接箱");
            _breakers = CategoryItems("断路器").ToList();
            _outlets = CategoryItems("插座").ToList();
            _outletPanels = CategoryItems("插座盘").ToList();
        }

        public List<ListItem> Items { get; }
        public IReadOnlyList<ListItem> SelectableItems => Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Code)
                && !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(item => item.Code ?? "", StringComparer.Ordinal).ToList();
        public IReadOnlyList<ListItem> Cables => _cableCandidates;
        public IReadOnlyList<ListItem> Breakers => _breakers;
        public IReadOnlyList<ListItem> BusPlugBoxes => _busPlugBoxes.Values.ToList();
        public IReadOnlyList<ListItem> Outlets => _outlets;
        public IReadOnlyList<ListItem> OutletPanels => _outletPanels;

        public ListItem FindCable(string cableModel)
            => FindWithMigration(_cables, NormalizeCable(cableModel));

        /// <summary>Exact code lookup (e.g. "8.5" 设备接地独立连接)。</summary>
        public ListItem FindByCode(string code)
            => Items.FirstOrDefault(item => string.Equals(item.Code, (code ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase));

        /// <summary>Matches a cable row by its complete project feature, not by its display model.</summary>
        public ListItem FindCableByFeature(string projectFeature)
        {
            string key = NormalizeFeature(projectFeature);
            if (key.Length == 0) return null;
            List<ListItem> matches = _cableCandidates.Where(item =>
                NormalizeFeature(item.Feature) == key).ToList();
            if (matches.Count > 1)
                throw new InvalidDataException("固定清单电缆项目特征重复，无法唯一匹配。");
            return matches.Count == 1 ? matches[0] : null;
        }

        /// <summary>Normalizes CAD/Excel line breaks and harmless unit/zero variants for feature comparison.</summary>
        public static string NormalizeFeature(string value)
        {
            string text = (value ?? "").Replace(@"\P", "\n").Replace("²", "2");
            text = text.Replace("o.6/", "0.6/").Replace("O.6/", "0.6/");
            text = string.Join("", TextParser.SplitMTextLines(text).Select(TextParser.CleanMText));
            return Whitespace.Replace(text, "").ToUpperInvariant();
        }

        public ListItem FindBridge(string spec)
            => FindWithMigration(_bridges, NormalizeSpec(spec));

        public ListItem FindRigidConduit(string diameter)
            => FindWithMigration(_rigidConduits, DiameterSpec(diameter));

        public ListItem FindFlexibleConduit(string diameter)
            => FindWithMigration(_flexibleConduits, DiameterSpec(diameter));

        public ListItem FindBusPlugBox(string rating)
            => FindWithMigration(_busPlugBoxes, NormalizeSpec(rating));

        /// <summary>
        /// Finds the fixed socket-panel row for a feeder rating.  Only the two
        /// approved mappings are supported; an unknown rating intentionally returns
        /// null instead of selecting a merely similar panel specification.
        /// </summary>
        public ListItem FindOutletPanel(int amps)
        {
            if (!OutletPanelCodes.TryGetValue(amps, out string code)) return null;
            List<ListItem> matches = _outletPanels.Where(item =>
                string.Equals((item.Code ?? "").Trim(), code,
                    StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 1)
                throw new InvalidDataException("固定清单插座盘项目编码重复：" + code);
            return matches.Count == 1 ? matches[0] : null;
        }

        /// <summary>Returns the approved BOQ code for a supported socket-panel rating.</summary>
        public static string OutletPanelCode(int amps)
            => OutletPanelCodes.TryGetValue(amps, out string code) ? code : "";

        public ListItem FindMigrationAlias(string value)
            => Find(_migrationAliases, NormalizeAlias(value));

        public static string NormalizeSpec(string value)
            => Whitespace.Replace(value ?? "", "")
                .Replace("×", "*").Replace("x", "*").Replace("X", "*")
                .ToUpperInvariant();

        /// <summary>
        /// 桥架规格查清单用的键：图上写法带“桥架”前缀（桥架200*100），
        /// 固定清单别名不带（200*100）。图上标注、统计与清单查询共用这一条规则。
        /// </summary>
        public static string NormalizeBridgeSpec(string value)
        {
            string text = value ?? "";
            return text.StartsWith("桥架", StringComparison.Ordinal)
                ? text.Substring(2) : text;
        }

        public static string NormalizeCable(string cable)
        {
            string value = (cable ?? "").Trim();
            if (value.Length == 0) return "";
            int separator = value.IndexOf('-');
            if (separator >= 0) value = value.Substring(separator + 1);
            separator = value.IndexOf('-');
            if (separator >= 0) value = value.Substring(separator + 1);
            return NormalizeSpec(value);
        }

        private static Dictionary<string, ListItem> AliasIndex(IEnumerable<ListItem> items,
            string categoryName)
        {
            var result = new Dictionary<string, ListItem>(StringComparer.OrdinalIgnoreCase);
            foreach (ListItem item in items)
            {
                string key = NormalizeSpec(item.Alias);
                if (key.Length == 0) continue;
                if (result.TryGetValue(key, out ListItem existing))
                    throw new InvalidDataException("固定清单“" + categoryName + "”别名重复："
                        + item.Alias + " 同时指向 " + existing.Code + " 和 " + item.Code + "。");
                result[key] = item;
            }
            return result;
        }

        private ListItem FindWithMigration(Dictionary<string, ListItem> primary, string key)
        {
            ListItem direct = Find(primary, key);
            if (direct != null) return direct;

            // Migration aliases are stored globally for validation, but the result must
            // still belong to the category represented by this primary index.  Without
            // this guard an alias such as 32mm could map a rigid conduit row into the
            // flexible-conduit (hose) slot.
            ListItem migrated = Find(_migrationAliases, key);
            return migrated != null && primary.Values.Contains(migrated) ? migrated : null;
        }

        private static ListItem Find(Dictionary<string, ListItem> index, string key)
            => key.Length > 0 && index.TryGetValue(key, out ListItem item) ? item : null;

        private static Dictionary<string, ListItem> MigrationIndex(IEnumerable<ListItem> items)
        {
            var result = new Dictionary<string, ListItem>(StringComparer.OrdinalIgnoreCase);
            foreach (ListItem item in items)
            {
                string key = NormalizeAlias(item.Alias1);
                if (key.Length == 0) continue;
                if (result.TryGetValue(key, out ListItem existing))
                    throw new InvalidDataException("固定清单别名1重复：" + item.Alias1
                        + " 同时指向 " + existing.Code + " 和 " + item.Code + "。");
                result[key] = item;
            }
            return result;
        }

        private static string NormalizeAlias(string value)
        {
            string diameter = ConduitDiameter.NormalizeOrEmpty(value);
            return diameter.Length > 0 ? NormalizeSpec(diameter + "mm")
                : NormalizeSpec(value);
        }

        private static string DiameterSpec(string diameter)
        {
            string value = ConduitDiameter.NormalizeOrEmpty(diameter);
            return value.Length == 0 ? "" : NormalizeSpec(value + "mm");
        }

        private IEnumerable<ListItem> CategoryItems(string category)
            => Items.Where(item => HasCategory(item, category));

        private static bool HasPrimaryAlias(ListItem item)
            => NormalizeSpec(item?.Alias).Length > 0;

        private static bool HasCategory(ListItem item, params string[] categories)
        {
            string value = (item?.Category ?? "").Trim();
            return categories.Any(category => string.Equals(value, category,
                StringComparison.Ordinal));
        }
    }
}
