using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.Fill
{
    /// <summary>Defines the U1U contract for outlet rows already edited in the CAD BOQ.</summary>
    public static class UpdateOutletPolicy
    {
        /// <summary>
        /// Ignores newly planned outlets and restores only outlets that currently exist in CAD.
        /// Therefore deleting an outlet from the drawing is a durable user decision.
        /// </summary>
        public static List<TableFillRow> PreserveExisting(
            IEnumerable<TableFillRow> plannedRows, IEnumerable<TableFillRow> existingOutlets)
        {
            List<TableFillRow> planned = (plannedRows ?? Enumerable.Empty<TableFillRow>())
                .Where(row => row != null).ToList();
            int insertionIndex = planned.FindIndex(IsOutlet);
            if (insertionIndex < 0) insertionIndex = planned.Count;
            planned.RemoveAll(IsOutlet);

            List<TableFillRow> outlets = (existingOutlets ?? Enumerable.Empty<TableFillRow>())
                .Where(row => row != null && IsOutlet(row))
                .Select(CloneAsTrustedOutlet).ToList();
            planned.InsertRange(Math.Min(insertionIndex, planned.Count), outlets);
            for (int index = 0; index < planned.Count; index++)
                planned[index].SortOrder = index + 1;
            return planned;
        }

        public static bool IsOutlet(TableFillRow row)
        {
            if (row == null) return false;
            if (row.Category == TableFillCategory.Outlet) return true;
            string name = (row.Name ?? "").Trim();
            string code = (row.Code ?? "").Trim();
            return name.IndexOf("插座", StringComparison.OrdinalIgnoreCase) >= 0
                || code.StartsWith("8.", StringComparison.OrdinalIgnoreCase);
        }

        private static TableFillRow CloneAsTrustedOutlet(TableFillRow row)
            => new TableFillRow
            {
                Category = TableFillCategory.Outlet,
                SortOrder = row.SortOrder,
                Name = row.Name ?? "",
                Description = row.Description ?? "",
                Unit = row.Unit ?? "",
                Quantity = row.Quantity ?? "",
                Code = row.Code ?? "",
                // Existing CAD content is preserved, not rematched or regenerated from the catalog.
                CatalogMatched = true
            };
    }
}
