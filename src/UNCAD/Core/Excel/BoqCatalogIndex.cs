using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UNCAD.Core.Text;

namespace UNCAD.Core.Excel
{
    public sealed class BoqCatalogIndex
    {
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
        private readonly Dictionary<string, ListItem> _cables;
        private readonly List<ListItem> _cableCandidates;
        private readonly Dictionary<string, ListItem> _bridges;
        private readonly Dictionary<string, ListItem> _rigidConduits;
        private readonly Dictionary<string, ListItem> _flexibleConduits;
        private readonly Dictionary<string, ListItem> _busPlugBoxes;
        private readonly List<ListItem> _breakers;
        private readonly List<ListItem> _outlets;

        public BoqCatalogIndex(IEnumerable<ListItem> items)
        {
            Items = (items ?? Enumerable.Empty<ListItem>()).Where(item => item != null).ToList();
            // A replacement must have a real specification because exact normalized matching
            // is the contract; rows with a blank spec cannot safely identify a cable model.
            _cableCandidates = Items.Where(item => StartsWithCode(item, "1.")
                    && NormalizeSpec(item.Spec).Length > 0)
                .OrderBy(item => item.Code ?? "", StringComparer.Ordinal).ToList();
            _cables = Index(_cableCandidates, item => NormalizeSpec(item.Spec));
            _bridges = Index(Items.Where(item => StartsWithCode(item, "2.")),
                item => NormalizeSpec(item.Spec));
            _rigidConduits = Index(Items.Where(item => StartsWithCode(item, "3.")
                && Contains(item.Name, "线管") && !Contains(item.Name, "软管")),
                item => NormalizeSpec(item.Spec));
            _flexibleConduits = Index(Items.Where(item => StartsWithCode(item, "3.")
                && Contains(item.Name, "软管")), item => NormalizeSpec(item.Spec));
            _busPlugBoxes = Index(Items.Where(item => StartsWithCode(item, "5.")
                && Contains(item.Name, "母线插接箱")), item => NormalizeSpec(item.Spec));
            _breakers = Items.Where(item => StartsWithCode(item, "6.")
                && Contains(item.Name, "断路器")).ToList();
            _outlets = Items.Where(item => StartsWithCode(item, "8.")
                && Contains(item.Name, "插座")).ToList();
        }

        public List<ListItem> Items { get; }
        public IReadOnlyList<ListItem> Cables => _cableCandidates;
        public IReadOnlyList<ListItem> Breakers => _breakers;
        public IReadOnlyList<ListItem> Outlets => _outlets;

        public ListItem FindCable(string cableModel)
            => Find(_cables, NormalizeCable(cableModel));

        public ListItem FindBridge(string spec)
            => Find(_bridges, NormalizeSpec(spec));

        public ListItem FindRigidConduit(string diameter)
            => Find(_rigidConduits, DiameterSpec(diameter));

        public ListItem FindFlexibleConduit(string diameter)
            => Find(_flexibleConduits, DiameterSpec(diameter));

        public ListItem FindBusPlugBox(string rating)
            => Find(_busPlugBoxes, NormalizeSpec(rating));

        public static string NormalizeSpec(string value)
            => Whitespace.Replace(value ?? "", "")
                .Replace("x", "*").Replace("X", "*").ToUpperInvariant();

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

        private static Dictionary<string, ListItem> Index(IEnumerable<ListItem> items,
            Func<ListItem, string> keySelector)
        {
            var result = new Dictionary<string, ListItem>(StringComparer.OrdinalIgnoreCase);
            foreach (ListItem item in items)
            {
                string key = keySelector(item);
                if (key.Length > 0 && !result.ContainsKey(key)) result[key] = item;
            }
            return result;
        }

        private static ListItem Find(Dictionary<string, ListItem> index, string key)
            => key.Length > 0 && index.TryGetValue(key, out ListItem item) ? item : null;

        private static string DiameterSpec(string diameter)
        {
            string value = ConduitDiameter.NormalizeOrEmpty(diameter);
            return value.Length == 0 ? "" : NormalizeSpec(value + "mm");
        }

        private static bool StartsWithCode(ListItem item, string prefix)
            => (item.Code ?? "").StartsWith(prefix, StringComparison.Ordinal);

        private static bool Contains(string value, string part)
            => (value ?? "").IndexOf(part, StringComparison.Ordinal) >= 0;
    }
}
