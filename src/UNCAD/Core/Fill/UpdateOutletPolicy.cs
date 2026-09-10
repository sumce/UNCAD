using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

        /// <summary>
        /// Applies the Device_Build20260716 socket state during U1U.
        ///
        /// The device block can tell us whether an outlet exists, but it cannot tell us
        /// how many outlets the user entered.  Consequently an existing CAD outlet row is
        /// authoritative (including its quantity and selected material).  A generated row
        /// is used only when the block is in an outlet state and no outlet row exists yet.
        /// </summary>
        public static List<TableFillRow> PreserveExistingOrAdd(
            IEnumerable<TableFillRow> plannedRows, bool hasOutlet,
            TableFillRow generatedOutlet, IEnumerable<TableFillRow> existingOutlets)
        {
            List<TableFillRow> planned = (plannedRows ?? Enumerable.Empty<TableFillRow>())
                .Where(row => row != null).ToList();
            int insertionIndex = planned.FindIndex(IsOutlet);
            int outletSortOrder = insertionIndex >= 0
                ? planned[insertionIndex].SortOrder
                : generatedOutlet?.SortOrder ?? 800;
            if (insertionIndex < 0)
            {
                // Keep a newly generated outlet after the upstream equipment rows and
                // before any manually appended rows whenever the planner supplied no
                // outlet placeholder.
                insertionIndex = planned.FindIndex(row => row.SortOrder > outletSortOrder);
                if (insertionIndex < 0)
                    insertionIndex = planned.FindIndex(row => row.Category == TableFillCategory.Manual);
                if (insertionIndex < 0) insertionIndex = planned.Count;
            }
            planned.RemoveAll(IsOutlet);

            List<TableFillRow> existing = (existingOutlets ?? Enumerable.Empty<TableFillRow>())
                .Where(row => row != null && IsOutlet(row))
                .Select(CloneAsTrustedOutlet).ToList();
            if (hasOutlet)
            {
                if (existing.Count > 0)
                {
                    planned.InsertRange(Math.Min(insertionIndex, planned.Count), existing);
                }
                else if (generatedOutlet != null)
                {
                    // Clone the generated row so later sort-order normalization cannot
                    // mutate a caller-owned preview/planner object.
                    planned.Insert(Math.Min(insertionIndex, planned.Count),
                        CloneAsTrustedGeneratedOutlet(generatedOutlet));
                }
            }

            return NormalizeOrder(planned);
        }

        /// <summary>8.x 段里真正属于插座的固定编码(8.1 封堵、8.4 变压器、
        /// 8.5 接地、8.6 停复电等都不是插座,绝不能按前缀误删)。</summary>
        private static readonly HashSet<string> OutletCodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "8.2", "8.3" };

        private static readonly Regex CatalogCode = new Regex(@"^\d+\.\d+$",
            RegexOptions.Compiled);

        /// <summary>
        /// 判定一行是不是受 Device 状态管控的 8.x 插座行。
        ///
        /// 有固定清单编码的行由编码说了算：只有 8.2/8.3 是插座。名字兜底只用于
        /// 没有编码的老行——否则固定清单里的 8.11「插座漏电相序检测仪」会因为名字
        /// 里带“插座”被当成插座行，用户在清单确认里加进来之后会被策略静默删掉。
        /// </summary>
        public static bool IsOutlet(TableFillRow row)
        {
            if (row == null) return false;
            // Socket-panel materials (4.9-4.14) are upstream equipment, not the
            // 8.x outlet rows controlled by Device_Build20260716.  Keep them when
            // U1U reapplies the device outlet policy.
            if (IsOutletPanel(row)) return false;
            if (row.Category == TableFillCategory.Outlet) return true;
            string code = (row.Code ?? "").Trim();
            if (CatalogCode.IsMatch(code)) return OutletCodes.Contains(code);
            string name = (row.Name ?? "").Trim();
            return name.IndexOf("插座", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsOutletPanel(TableFillRow row)
        {
            if (row == null) return false;
            if (row.Category == TableFillCategory.OutletPanel) return true;
            string code = (row.Code ?? "").Trim();
            if (code.Equals("4.9", StringComparison.OrdinalIgnoreCase)
                || code.Equals("4.10", StringComparison.OrdinalIgnoreCase)
                || code.Equals("4.11", StringComparison.OrdinalIgnoreCase)
                || code.Equals("4.12", StringComparison.OrdinalIgnoreCase)
                || code.Equals("4.13", StringComparison.OrdinalIgnoreCase)
                || code.Equals("4.14", StringComparison.OrdinalIgnoreCase))
                return true;
            // A malformed/manual 8.x row can still be named “插座盘”; its fixed
            // code is stronger evidence that it is the downstream outlet row.
            if (code.StartsWith("8.", StringComparison.OrdinalIgnoreCase)) return false;
            return (row.Name ?? "").IndexOf("插座盘", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private static TableFillRow CloneAsTrustedGeneratedOutlet(TableFillRow row)
            => new TableFillRow
            {
                Category = TableFillCategory.Outlet,
                SortOrder = row.SortOrder,
                Name = row.Name ?? "",
                Description = row.Description ?? "",
                Unit = row.Unit ?? "",
                Quantity = string.IsNullOrWhiteSpace(row.Quantity) ? "1" : row.Quantity,
                Code = row.Code ?? "",
                CatalogMatched = row.CatalogMatched
            };

        private static List<TableFillRow> NormalizeOrder(List<TableFillRow> rows)
        {
            // The caller's sequence is already the reviewed/planned order.  Sorting here
            // would move rows read from CAD (whose original row index is used as a
            // SortOrder) ahead of cable and conduit rows.
            List<TableFillRow> result = (rows ?? new List<TableFillRow>()).ToList();
            for (int index = 0; index < result.Count; index++)
                result[index].SortOrder = index + 1;
            return result;
        }
    }
}
