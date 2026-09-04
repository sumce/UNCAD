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
        /// <summary>行号与本次写入清单的顺序一致;移除的旧行为 0。</summary>
        public int Order { get; set; }
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
            int order = 0;
            foreach (TableFillRow row in planned)
            {
                order++;
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
                        Order = order,
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
                    Order = order,
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
            // 顺序与本次写入的清单完全一致,只有移除的旧行追加在最后。
            return diffs;
        }

        /// <summary>
        /// 并排对比视图:左侧为上次清单(表格现有行,被移除的标红),
        /// 右侧为本次将写入的清单(新增/改量着色,Order 即行号)。
        /// </summary>
        public static Tuple<List<FillRowDiff>, List<FillRowDiff>> BuildSides(
            List<TableFillRow> existing, List<TableFillRow> planned)
        {
            existing = existing ?? new List<TableFillRow>();
            planned = planned ?? new List<TableFillRow>();

            var plannedByKey = new Dictionary<string, TableFillRow>(
                StringComparer.OrdinalIgnoreCase);
            foreach (TableFillRow row in planned)
            {
                string key = MatchKey(row);
                if (key.Length > 0 && !plannedByKey.ContainsKey(key))
                    plannedByKey[key] = row;
            }
            var usedPlanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var left = new List<FillRowDiff>();
            foreach (TableFillRow old in existing)
            {
                string key = MatchKey(old);
                TableFillRow current = key.Length > 0
                    && plannedByKey.TryGetValue(key, out TableFillRow found)
                    ? found : null;
                string oldQty = Normalize(old.Quantity);
                if (current == null)
                {
                    left.Add(new FillRowDiff
                    {
                        Status = FillRowDiff.StatusRemoved,
                        Changed = true,
                        Name = old.Name,
                        Description = old.Description,
                        Unit = old.Unit,
                        OldQuantity = oldQty,
                        Code = old.Code
                    });
                    continue;
                }
                usedPlanned.Add(key);
                string newQty = Normalize(current.Quantity);
                left.Add(new FillRowDiff
                {
                    Status = !string.Equals(oldQty, newQty, StringComparison.Ordinal)
                        ? FillRowDiff.StatusQuantity : FillRowDiff.StatusKept,
                    Changed = !string.Equals(oldQty, newQty, StringComparison.Ordinal),
                    Name = old.Name,
                    Description = old.Description,
                    Unit = old.Unit,
                    OldQuantity = oldQty,
                    NewQuantity = newQty,
                    Code = old.Code
                });
            }

            var right = new List<FillRowDiff>();
            int order = 0;
            foreach (TableFillRow row in planned)
            {
                order++;
                string key = MatchKey(row);
                bool matched = key.Length > 0 && usedPlanned.Contains(key);
                right.Add(new FillRowDiff
                {
                    Status = matched ? FillRowDiff.StatusKept : FillRowDiff.StatusAdded,
                    Changed = !matched,
                    Order = order,
                    Name = row.Name,
                    Description = row.Description,
                    Unit = row.Unit,
                    NewQuantity = Normalize(row.Quantity),
                    Code = row.Code
                });
            }
            return Tuple.Create(left, right);
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
}
