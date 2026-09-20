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
        public FillAutoFillOptions AutoFill { get; private set; } =
            FillAutoFillOptions.Default;
        public List<FillReviewItem> Items { get; } = new List<FillReviewItem>();

        public static FillReviewData Create(MachineRow source, IEnumerable<TableFillRow> rows)
            => Create(source, rows, FillPlanningOptions.Default);

        public static FillReviewData Create(MachineRow source, IEnumerable<TableFillRow> rows,
            FillPlanningOptions options)
        {
            options = options ?? FillPlanningOptions.Default;
            MachineRow machine = CloneMachine(source);
            return Create(machine, rows, options, null);
        }

        /// <summary>
        /// Creates review data from a planning machine whose cable may already be the
        /// confirmed BOQ alias. The persisted machine copy still keeps the original
        /// workbook/device cable, while the BOQ field retains the planning alias.
        /// </summary>
        public static FillReviewData Create(MachineRow source, IEnumerable<TableFillRow> rows,
            FillPlanningOptions options, string originalCableModel)
            => Create(source, rows, options, originalCableModel,
                FillAutoFillOptions.Default);

        public static FillReviewData Create(MachineRow source, IEnumerable<TableFillRow> rows,
            FillPlanningOptions options, string originalCableModel,
            FillAutoFillOptions autoFill)
        {
            options = options ?? FillPlanningOptions.Default;
            MachineRow machine = CloneMachine(source);
            string planningCable = (machine.Cable ?? "").Trim();
            string original = FirstNonEmpty(originalCableModel, planningCable);
            string boq = FirstNonEmpty(planningCable, original);
            machine.Cable = original;
            var data = new FillReviewData
            {
                Machine = machine,
                OriginalCableModel = original,
                BoqCableModel = boq,
                AutoFill = autoFill ?? FillAutoFillOptions.Default
            };
            foreach (TableFillRow row in rows ?? Enumerable.Empty<TableFillRow>())
            {
                data.Items.Add(new FillReviewItem
                {
                    // Unmatched rows stay excluded until a fixed-catalog replacement is
                    // selected. There is no uncoded fallback because CAD and BOQ must agree.
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
                CableMeters = CableMeters,
                AutoFill = AutoFill
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

        public FillReviewItem BusPlugBoxItem()
            => Items.FirstOrDefault(item => item.Category == TableFillCategory.BusPlugBox);

        /// <summary>Restores a confirmed catalog choice only for the same frame identity and supply data.</summary>
        internal void RestoreBusPlugBoxChoice(FrameInfoJsonRecord previous, BoqCatalogIndex catalog)
        {
            FillReviewItem item = BusPlugBoxItem();
            if (previous == null || item == null
                || string.IsNullOrWhiteSpace(previous.BoqBusPlugBoxCode)
                || !IdentityTextNormalizer.Equals(previous.MachineId, Machine.MachineId)
                || !IdentityTextNormalizer.Equals(previous.DeviceName, Machine.CircuitName)
                || !string.Equals(previous.Next, Machine.Next, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(previous.Detail, Machine.Detail, StringComparison.OrdinalIgnoreCase))
                return;

            ListItem selected = catalog?.FindByCode(previous.BoqBusPlugBoxCode);
            if (selected == null || !string.Equals(selected.Category?.Trim(),
                    CategoryName(TableFillCategory.BusPlugBox), StringComparison.Ordinal)) return;
            ReplaceWithCatalogItem(item, selected, catalog);
        }

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
            => ReplaceWithCatalogItem(item, catalogItem, null);

        public void ReplaceWithCatalogItem(FillReviewItem item, ListItem catalogItem,
            BoqCatalogIndex catalog)
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
            {
                string model = (catalogItem.Alias ?? catalogItem.Spec ?? "").Trim();
                BoqCableModel = model;
                // The legacy overload has no catalog index, so it can confirm the
                // cable row but cannot safely create a catalog-backed hose row.  The
                // indexed overload used by U1F/U1U performs the derived hose update.
                if (catalog == null) return;
                // A cable replacement changes the derived hose diameter as well.  Keep
                // the editable BOQ identity and the device identity separate, but make
                // the hose row follow the newly confirmed catalog cable immediately.
                SetCableModel(model, catalog, catalogItem);
            }
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
            SetCableModel(value, catalog, matched);
        }

        private void SetCableModel(string model, BoqCatalogIndex catalog,
            ListItem explicitMatch)
        {
            string value = (model ?? "").Trim();
            BoqCableModel = value;
            FillReviewItem cable = CableItem();
            if (cable == null) return;

            ListItem matched = explicitMatch ?? catalog?.FindCable(value);
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

            // The fixed hose map is keyed by the confirmed cable alias/core model.  When
            // the cable was unknown, the initial plan has no hose row; after a user picks
            // a known cable we create/rematch it so a valid Ruanguan length is not lost.
            if (matched != null && FlexibleConduitCableMap.TryGetDiameter(
                    matched.Alias ?? value, out string diameter))
            {
                Machine.Dia = diameter;
                if (AutoFill.FlexibleConduit)
                    SetFlexibleConduitDiameter(diameter, catalog,
                        FillPlanningOptions.Default);
                else
                {
                    FillReviewItem flexible = FlexibleConduitItem();
                    if (flexible != null) RemoveItem(flexible);
                }
            }
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
            if (value.Length == 0)
            {
                if (flexible != null) RemoveItem(flexible);
                return null;
            }
            if (flexible == null)
            {
                TableFillRow generated = TableFillPlanner.BuildFlexibleConduitRow(
                    value, catalog, options);
                flexible = new FillReviewItem
                {
                    Included = generated.CatalogMatched,
                    Category = TableFillCategory.FlexibleConduit,
                    Name = generated.Name,
                    Description = generated.Description,
                    Unit = generated.Unit,
                    Quantity = generated.Quantity,
                    Code = generated.Code,
                    CatalogMatched = generated.CatalogMatched
                };
                Items.Add(flexible);
                return flexible;
            }

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
                case TableFillCategory.Panel: return "电盘";
                case TableFillCategory.OutletPanel: return "插座盘";
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
                Batch = source.Batch,
                Cable = source.Cable,
                Fr = source.Fr,
                Detail = source.Detail,
                Seq = source.Seq,
                Dia = source.Dia,
                Next = source.Next,
                DownstreamAxis = source.DownstreamAxis,
                UpstreamAxis = source.UpstreamAxis,
                DeviceFloor = source.DeviceFloor,
                PanelFloor = source.PanelFloor,
                FacilitySwitch = source.FacilitySwitch
            };
        }

        private static string FirstNonEmpty(params string[] values)
            => values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }
}
