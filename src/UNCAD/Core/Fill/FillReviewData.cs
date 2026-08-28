using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>Editable BOQ row shown during final fill review.</summary>
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
        // Every row, including a user-added row, must retain a fixed-catalog identity.
        public bool RequiresCatalogConfirmation => !CatalogMatched;

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

    /// <summary>
    /// Review state that separates device identity from procurement substitutions.
    /// </summary>
    public sealed class FillReviewData
    {
        public MachineRow Machine { get; set; }
        public string OriginalCableModel { get; private set; } = "";
        public string BoqCableModel { get; private set; } = "";
        public string CableMeters { get; set; } = "";
        public List<FillReviewItem> Items { get; } = new List<FillReviewItem>();

        public static FillReviewData Create(MachineRow source, IEnumerable<TableFillRow> rows)
            => Create(source, rows, FillPlanningOptions.Default);

        public static FillReviewData Create(MachineRow source, IEnumerable<TableFillRow> rows,
            FillPlanningOptions options)
        {
            options = options ?? FillPlanningOptions.Default;
            MachineRow machine = CloneMachine(source);
            var data = new FillReviewData
            {
                Machine = machine,
                OriginalCableModel = (machine.Cable ?? "").Trim(),
                BoqCableModel = (machine.Cable ?? "").Trim()
            };
            foreach (TableFillRow row in rows ?? Enumerable.Empty<TableFillRow>())
            {
                data.Items.Add(new FillReviewItem
                {
                    // Unmatched rows cannot be generated. The user must replace them with
                    // a database item before the inclusion checkbox becomes available.
                    Included = row.CatalogMatched,
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

        /// <summary>
        /// Creates an independent session snapshot, including a confirmed BOQ substitute.
        /// Restore Defaults must not reconstruct both cable fields from the device attribute.
        /// </summary>
        public FillReviewData Snapshot()
        {
            var snapshot = new FillReviewData
            {
                Machine = CloneMachine(Machine),
                OriginalCableModel = OriginalCableModel,
                BoqCableModel = BoqCableModel,
                CableMeters = CableMeters
            };
            foreach (FillReviewItem item in Items)
            {
                snapshot.Items.Add(new FillReviewItem
                {
                    Included = item.Included,
                    Category = item.Category,
                    Name = item.Name ?? "",
                    Description = item.Description ?? "",
                    Unit = item.Unit ?? "",
                    Quantity = item.Quantity ?? "",
                    Code = item.Code ?? "",
                    CatalogMatched = item.CatalogMatched
                });
            }
            return snapshot;
        }

        /// <summary>
        /// Returns the current review order with a continuous 1-based sequence. Deleted or
        /// unchecked rows leave no gaps; manual rows remain where the user added them.
        /// </summary>
        public List<TableFillRow> SelectedRows()
        {
            var selected = new List<TableFillRow>();
            // This core boundary is the final guard even if a caller bypasses the UI and
            // toggles Included directly on an unmatched row.
            foreach (FillReviewItem item in Items.Where(item =>
                item.Included && item.CatalogMatched))
            {
                TableFillRow row = item.ToTableRow();
                row.SortOrder = selected.Count + 1;
                selected.Add(row);
            }
            return selected;
        }

        public FillReviewItem CableItem()
            => Items.FirstOrDefault(item => item.Category == TableFillCategory.Cable);

        public FillReviewItem FlexibleConduitItem()
            => Items.FirstOrDefault(item =>
                item.Category == TableFillCategory.FlexibleConduit);

        public FillReviewItem AddCatalogItem(ListItem catalogItem, string quantity)
        {
            if (catalogItem == null) throw new ArgumentNullException(nameof(catalogItem));
            if (string.IsNullOrWhiteSpace(catalogItem.Code)
                || string.IsNullOrWhiteSpace(catalogItem.Name))
                throw new ArgumentException("固定清单项目缺少项目编码或名称。", nameof(catalogItem));

            // A user-added row is manual only in how it entered the review. Its material
            // identity is copied verbatim from the database and is never user-authored.
            var item = new FillReviewItem
            {
                Included = true,
                Category = TableFillCategory.Manual,
                Name = catalogItem.Name.Trim(),
                Description = (catalogItem.Feature ?? "").Trim(),
                Unit = (catalogItem.Unit ?? "").Trim(),
                Quantity = (quantity ?? "").Trim(),
                Code = catalogItem.Code.Trim(),
                CatalogMatched = true
            };
            Items.Add(item);
            return item;
        }

        public void ReplaceWithCatalogItem(FillReviewItem item, ListItem catalogItem)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (catalogItem == null) throw new ArgumentNullException(nameof(catalogItem));
            if (string.IsNullOrWhiteSpace(catalogItem.Code)
                || string.IsNullOrWhiteSpace(catalogItem.Name))
                throw new ArgumentException("固定清单项目缺少项目编码或名称。", nameof(catalogItem));

            item.Name = catalogItem.Name.Trim();
            item.Description = (catalogItem.Feature ?? "").Trim();
            item.Unit = (catalogItem.Unit ?? "").Trim();
            item.Code = catalogItem.Code.Trim();
            item.CatalogMatched = true;
            item.Included = true;
            if (item.Category == TableFillCategory.Cable)
                BoqCableModel = (catalogItem.Alias ?? "").Trim();
        }

        public bool RemoveItem(FillReviewItem item)
            => item != null && Items.Remove(item);

        public bool RemoveManualItem(FillReviewItem item)
            => item != null && item.Category == TableFillCategory.Manual
                && RemoveItem(item);

        public void SetCableModel(string model)
            => SetCableModel(model, null);

        public void SetCableModel(string model, BoqCatalogIndex catalog)
        {
            string value = (model ?? "").Trim();
            BoqCableModel = value;
            FillReviewItem cable = CableItem();
            if (cable == null) return;

            ListItem matched = catalog?.FindCable(value);
            cable.Name = string.IsNullOrWhiteSpace(matched?.Name)
                ? "电缆" : matched.Name;
            cable.Description = string.IsNullOrWhiteSpace(matched?.Feature)
                ? (value.Length > 0
                    ? string.Format(FillTemplates.CableDesc, value)
                    : "1.名称:电缆")
                : matched.Feature;
            cable.Unit = string.IsNullOrWhiteSpace(matched?.Unit) ? "M" : matched.Unit;
            cable.Code = matched?.Code ?? "";
            cable.CatalogMatched = matched != null;
            cable.Included = matched != null;
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
            TableFillRow planned = TableFillPlanner.BuildFlexibleConduitRow(
                value, catalog, options);
            flexible.Name = planned.Name;
            flexible.Description = planned.Description;
            flexible.Unit = planned.Unit;
            flexible.Code = planned.Code;
            flexible.CatalogMatched = planned.CatalogMatched;
            flexible.Quantity = quantity;
            flexible.Included = planned.CatalogMatched;
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
                case TableFillCategory.Manual: return "手动添加";
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
