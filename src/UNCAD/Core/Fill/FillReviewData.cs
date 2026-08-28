using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    public sealed class FillReviewItem
    {
        public bool Included { get; set; } = true;
        public TableFillCategory Category { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Quantity { get; set; } = "";
        public string Code { get; set; } = "";
        public bool CatalogMatched { get; set; }
        public bool RequiresCatalogConfirmation => !CatalogMatched
            && (Category == TableFillCategory.RigidConduit
                || Category == TableFillCategory.FlexibleConduit);

        public TableFillRow ToTableRow()
        {
            return new TableFillRow
            {
                Category = Category,
                Name = Name ?? "",
                Description = Description ?? "",
                Unit = Unit ?? "",
                Quantity = Quantity ?? "",
                Code = Code ?? "",
                CatalogMatched = CatalogMatched
            };
        }
    }

    public sealed class FillReviewData
    {
        public MachineRow Machine { get; set; }
        public string CableMeters { get; set; } = "";
        public List<FillReviewItem> Items { get; } = new List<FillReviewItem>();

        public static FillReviewData Create(MachineRow source, IEnumerable<TableFillRow> rows)
            => Create(source, rows, FillPlanningOptions.Default);

        public static FillReviewData Create(MachineRow source, IEnumerable<TableFillRow> rows,
            FillPlanningOptions options)
        {
            options = options ?? FillPlanningOptions.Default;
            var data = new FillReviewData { Machine = CloneMachine(source) };
            foreach (TableFillRow row in rows ?? Enumerable.Empty<TableFillRow>())
            {
                data.Items.Add(new FillReviewItem
                {
                    Included = row.CatalogMatched
                        || (row.Category != TableFillCategory.RigidConduit
                            && row.Category != TableFillCategory.FlexibleConduit)
                        || options.IncludeUnmatchedConduitsByDefault,
                    Category = row.Category,
                    Name = row.Name ?? "",
                    Description = row.Description ?? "",
                    Unit = row.Unit ?? "",
                    Quantity = row.Quantity ?? "",
                    Code = row.Code ?? "",
                    CatalogMatched = row.CatalogMatched
                });
            }
            data.CableMeters = data.CableItem()?.Quantity ?? "";
            return data;
        }

        public List<TableFillRow> SelectedRows()
            => Items.Where(item => item.Included).Select(item => item.ToTableRow()).ToList();

        public FillReviewItem CableItem()
            => Items.FirstOrDefault(item => item.Category == TableFillCategory.Cable);

        public FillReviewItem FlexibleConduitItem()
            => Items.FirstOrDefault(item =>
                item.Category == TableFillCategory.FlexibleConduit);

        public FillReviewItem AddManualItem(string name, string description,
            string unit, string quantity, string code)
        {
            var item = new FillReviewItem
            {
                Included = true,
                Category = TableFillCategory.Manual,
                Name = (name ?? "").Trim(),
                Description = (description ?? "").Trim(),
                Unit = (unit ?? "").Trim(),
                Quantity = (quantity ?? "").Trim(),
                Code = (code ?? "").Trim(),
                CatalogMatched = false
            };
            Items.Add(item);
            return item;
        }

        public bool RemoveManualItem(FillReviewItem item)
            => item != null && item.Category == TableFillCategory.Manual
                && Items.Remove(item);

        public void SetCableModel(string model)
        {
            string value = (model ?? "").Trim();
            Machine.Cable = value;
            FillReviewItem cable = CableItem();
            if (cable != null && value.Length > 0)
                cable.Description = string.Format(FillTemplates.CableDesc, value);
        }

        public void SetCableMeters(string meters)
        {
            CableMeters = (meters ?? "").Trim();
            FillReviewItem cable = CableItem();
            if (cable != null) cable.Quantity = CableMeters;
        }

        public FillReviewItem SetFlexibleConduitDiameter(
            string diameter, List<ListItem> catalogItems)
            => SetFlexibleConduitDiameter(diameter, new BoqCatalogIndex(catalogItems),
                FillPlanningOptions.Default);

        public FillReviewItem SetFlexibleConduitDiameter(string diameter,
            BoqCatalogIndex catalog, FillPlanningOptions options)
        {
            catalog = catalog ?? new BoqCatalogIndex(null);
            options = options ?? FillPlanningOptions.Default;
            string sourceValue = (diameter ?? "").Trim();
            string normalized = ConduitDiameter.NormalizeOrEmpty(sourceValue);
            string value = normalized.Length > 0 ? normalized : sourceValue;
            Machine.Dia = value;
            FillReviewItem flexible = FlexibleConduitItem();
            if (flexible == null) return null;

            string quantity = flexible.Quantity;
            bool wasMatched = flexible.CatalogMatched;
            TableFillRow planned = TableFillPlanner.BuildFlexibleConduitRow(
                value, catalog, options);
            flexible.Name = planned.Name;
            flexible.Description = planned.Description;
            flexible.Unit = planned.Unit;
            flexible.Code = planned.Code;
            flexible.CatalogMatched = planned.CatalogMatched;
            flexible.Quantity = quantity;
            if (wasMatched != planned.CatalogMatched)
                flexible.Included = planned.CatalogMatched
                    || options.IncludeUnmatchedConduitsByDefault;
            return flexible;
        }

        public static string CategoryName(TableFillCategory category)
        {
            switch (category)
            {
                case TableFillCategory.Cable: return "电缆";
                case TableFillCategory.Bridge: return "桥架";
                case TableFillCategory.RigidConduit: return "线管";
                case TableFillCategory.FlexibleConduit: return "软管";
                case TableFillCategory.BusPlugBox: return "母线插接箱";
                case TableFillCategory.Breaker: return "断路器";
                case TableFillCategory.Outlet: return "插座";
                case TableFillCategory.Manual: return "手动项";
                default: return category.ToString();
            }
        }

        private static MachineRow CloneMachine(MachineRow source)
        {
            source = source ?? new MachineRow();
            return new MachineRow
            {
                Region = source.Region,
                MachineId = source.MachineId,
                CircuitName = source.CircuitName,
                Cable = source.Cable,
                Fr = source.Fr,
                Detail = source.Detail,
                Seq = source.Seq,
                Dia = source.Dia,
                Next = source.Next,
                DownstreamAxis = source.DownstreamAxis,
                UpstreamAxis = source.UpstreamAxis
            };
        }
    }
}
