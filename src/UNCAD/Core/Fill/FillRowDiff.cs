using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Submission;

namespace UNCAD.Core.Fill
{
    /// <summary>One row-level difference between the existing table and this update.</summary>
    public sealed class FillRowDiff
    {
        public const string StatusKept = "保留";
        public const string StatusAdded = "新增";
        public const string StatusRemoved = "移除";
        public const string StatusQuantity = "改量";

        public string Status { get; set; } = StatusKept;
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public string OldQuantity { get; set; } = "";
        public string NewQuantity { get; set; } = "";
        public string Code { get; set; } = "";
        public bool Changed { get; set; }
    }

    /// <summary>
    /// Builds the row-by-row list comparison shown by U1U before overwriting a
    /// frame: the rows currently in the CAD table versus the planned rows.
    /// </summary>
    public static class FillRowDiffBuilder
    {
        public static List<FillRowDiff> Build(List<TableFillRow> existing,
            List<TableFillRow> planned)
        {
            existing = existing ?? new List<TableFillRow>();
            planned = planned ?? new List<TableFillRow>();

            var oldByKey = new Dictionary<string, TableFillRow>(StringComparer.OrdinalIgnoreCase);
            foreach (TableFillRow row in existing)
            {
                string key = MatchKey(row);
                if (key.Length > 0 && !oldByKey.ContainsKey(key)) oldByKey[key] = row;
            }
            var usedOld = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var diffs = new List<FillRowDiff>();
            foreach (TableFillRow row in planned)
            {
                string key = MatchKey(row);
                if (key.Length > 0 && oldByKey.TryGetValue(key, out TableFillRow old))
                {
                    usedOld.Add(key);
                    string oldQty = Normalize(old.Quantity);
                    string newQty = Normalize(row.Quantity);
                    bool changed = !string.Equals(oldQty, newQty, StringComparison.Ordinal);
                    diffs.Add(new FillRowDiff
                    {
                        Status = changed ? FillRowDiff.StatusQuantity : FillRowDiff.StatusKept,
                        Changed = changed,
                        Name = row.Name,
                        Description = row.Description,
                        Unit = row.Unit,
                        OldQuantity = oldQty,
                        NewQuantity = newQty,
                        Code = string.IsNullOrWhiteSpace(row.Code) ? old.Code : row.Code
                    });
                    continue;
                }
                diffs.Add(new FillRowDiff
                {
                    Status = FillRowDiff.StatusAdded,
                    Changed = true,
                    Name = row.Name,
                    Description = row.Description,
                    Unit = row.Unit,
                    NewQuantity = Normalize(row.Quantity),
                    Code = row.Code
                });
            }
            foreach (TableFillRow old in existing)
            {
                string key = MatchKey(old);
                if (key.Length > 0 && usedOld.Contains(key)) continue;
                if (key.Length == 0 && planned.Any(row =>
                        SameName(row, old) && usedOld.Add(MatchKey(old)))) continue;
                diffs.Add(new FillRowDiff
                {
                    Status = FillRowDiff.StatusRemoved,
                    Changed = true,
                    Name = old.Name,
                    Description = old.Description,
                    Unit = old.Unit,
                    OldQuantity = Normalize(old.Quantity),
                    Code = old.Code
                });
            }
            // 变化优先,其余按 新增 → 保留 → 移除,组内保持计划/表格顺序。
            int Rank(FillRowDiff diff)
            {
                if (diff.Status == FillRowDiff.StatusQuantity) return 0;
                if (diff.Status == FillRowDiff.StatusAdded) return 1;
                if (diff.Status == FillRowDiff.StatusKept) return 2;
                return 3;
            }
            diffs.StableSort((left, right) => Rank(left).CompareTo(Rank(right)));
            return diffs;
        }

        /// <summary>Reads every non-header table row (no category filtering) for display.</summary>
        public static List<TableFillRow> ReadRows(SubmissionSourceData source)
        {
            var existing = new List<TableFillRow>();
            foreach (List<string> cells in source?.TableRows ?? new List<List<string>>())
            {
                if (cells == null || cells.Count < 5) continue;
                string number = Cell(cells, 0);
                string name = Cell(cells, 1);
                if (name.Length == 0) continue;
                if (TableLayoutClassifier.IsHeaderLike(number, name, Cell(cells, 5)))
                    continue;
                string code = Cell(cells, 5);
                if (!SubmissionRecordExtractor.IsCatalogCode(code)) code = "";
                existing.Add(new TableFillRow
                {
                    SortOrder = existing.Count + 1,
                    Name = name,
                    Description = Cell(cells, 2),
                    Unit = Cell(cells, 3),
                    Quantity = Cell(cells, 4),
                    Code = code
                });
            }
            return existing;
        }

        private static string MatchKey(TableFillRow row)
        {
            if (row == null) return "";
            if (!string.IsNullOrWhiteSpace(row.Code)) return "C:" + row.Code.Trim();
            return string.IsNullOrWhiteSpace(row.Name) ? "" : "N:" + row.Name.Trim();
        }

        private static bool SameName(TableFillRow left, TableFillRow right)
            => string.Equals((left.Name ?? "").Trim(), (right.Name ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string value) => (value ?? "").Trim();

        private static string Cell(List<string> row, int index)
            => index >= 0 && index < row.Count ? (row[index] ?? "").Trim() : "";
    }

    internal static class ListExtensions
    {
        /// <summary>Order-preserving sort (List.Sort is unstable).</summary>
        public static void StableSort<T>(this List<T> list, Comparison<T> comparison)
        {
            var indexed = list.Select((item, index) => (item, index)).ToList();
            indexed.Sort((left, right) =>
            {
                int result = comparison(left.item, right.item);
                return result != 0 ? result : left.index.CompareTo(right.index);
            });
            for (int i = 0; i < list.Count; i++) list[i] = indexed[i].item;
        }
    }
}
