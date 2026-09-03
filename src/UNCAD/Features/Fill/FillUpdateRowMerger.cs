using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Submission;
using UNCAD.Features.Submit;

namespace UNCAD.Features.Fill
{
    internal static class FillUpdateRowMerger
    {
        public static List<TableFillRow> Merge(CadContext ctx, FillSelection selection,
            List<TableFillRow> planned, CableStatResult statistics)
        {
            planned = planned ?? new List<TableFillRow>();
            statistics = statistics ?? new CableStatResult();
            if (selection?.TableIds == null || selection.TableIds.Length == 0) return planned;
            if (statistics.CableState != MeasurementState.Unknown
                && statistics.BridgeState != MeasurementState.Unknown
                && statistics.ConduitState != MeasurementState.Unknown) return planned;

            SubmissionSourceData source = CadSubmissionReader.Read(ctx, selection.TableIds);
            return MergeRows(planned, ReadRows(source), statistics);
        }

        internal static List<TableFillRow> MergeRows(List<TableFillRow> planned,
            IEnumerable<TableFillRow> existingRows, CableStatResult statistics)
        {
            planned = planned ?? new List<TableFillRow>();
            statistics = statistics ?? new CableStatResult();
            foreach (TableFillRow old in existingRows ?? Enumerable.Empty<TableFillRow>())
            {
                bool preserve = old.Category == TableFillCategory.Cable
                    ? statistics.CableState == MeasurementState.Unknown
                    : old.Category == TableFillCategory.Bridge
                        ? statistics.BridgeState == MeasurementState.Unknown
                        : old.Category == TableFillCategory.RigidConduit
                            && statistics.ConduitState == MeasurementState.Unknown;
                if (!preserve) continue;

                TableFillRow current = planned.FirstOrDefault(row =>
                    row.Category == old.Category && SameMaterial(row, old));
                if (current != null)
                {
                    if (string.IsNullOrWhiteSpace(current.Quantity))
                        current.Quantity = old.Quantity;
                    if (string.IsNullOrWhiteSpace(current.Code)) current.Code = old.Code;
                    if (!current.CatalogMatched && old.CatalogMatched)
                        current.CatalogMatched = true;
                    continue;
                }
                planned.Add(old);
            }
            return planned.OrderBy(row => row.SortOrder)
                .ThenBy(row => row.Code ?? "", StringComparer.Ordinal).ToList();
        }

        private static List<TableFillRow> ReadRows(SubmissionSourceData source)
        {
            var existing = new List<TableFillRow>();
            foreach (List<string> cells in source?.TableRows ?? new List<List<string>>())
            {
                if (cells == null || cells.Count < 5) continue;
                string number = Cell(cells, 0);
                string name = Cell(cells, 1);
                string description = Cell(cells, 2);
                string unit = Cell(cells, 3);
                string quantity = Cell(cells, 4);
                string code = Cell(cells, 5);
                if (TableLayoutClassifier.IsHeaderLike(number, name, code)) continue;
                // The first column is normally only the display ordinal (1, 2, ...).
                // Treat it as a catalog identity only when it has the fixed BOQ form;
                // otherwise the next U1U pass would falsely mark a fallback row as
                // matched and BOQ export would later fail on code "1"/"2".
                string catalogCode = CatalogCode(code) ? code
                    : CatalogCode(number) ? number : "";
                TableFillCategory? category = ExistingCategory(number, name, catalogCode);
                if (!category.HasValue || category.Value == TableFillCategory.FlexibleConduit
                    || category.Value == TableFillCategory.Outlet
                    || category.Value == TableFillCategory.OutletPanel
                    || category.Value == TableFillCategory.Breaker
                    || category.Value == TableFillCategory.BusPlugBox)
                    continue;
                existing.Add(new TableFillRow
                {
                    Category = category.Value,
                    SortOrder = existing.Count + 1,
                    Name = name,
                    Description = description,
                    Unit = unit,
                    Quantity = quantity,
                    Code = catalogCode,
                    CatalogMatched = catalogCode.Length > 0
                });
            }
            return existing;
        }

        private static TableFillCategory? ExistingCategory(string number, string name,
            string code)
        {
            string key = code.Length > 0 ? code : number;
            if (key.StartsWith("1.", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("电缆", StringComparison.OrdinalIgnoreCase) >= 0)
                return TableFillCategory.Cable;
            if (key.StartsWith("2.", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("桥架", StringComparison.OrdinalIgnoreCase) >= 0)
                return TableFillCategory.Bridge;
            if (key.StartsWith("3.8", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("软管", StringComparison.OrdinalIgnoreCase) >= 0)
                return TableFillCategory.FlexibleConduit;
            if (key.StartsWith("3.", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("线管", StringComparison.OrdinalIgnoreCase) >= 0)
                return TableFillCategory.RigidConduit;
            return null;
        }

        private static bool SameMaterial(TableFillRow left, TableFillRow right)
        {
            if (!string.IsNullOrWhiteSpace(left.Code) && !string.IsNullOrWhiteSpace(right.Code))
                return string.Equals(left.Code.Trim(), right.Code.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            return string.Equals((left.Name ?? "").Trim(), (right.Name ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Cell(List<string> row, int index)
            => index >= 0 && index < row.Count ? (row[index] ?? "").Trim() : "";

        private static bool CatalogCode(string value)
            => SubmissionRecordExtractor.IsCatalogCode(value);
    }
}
