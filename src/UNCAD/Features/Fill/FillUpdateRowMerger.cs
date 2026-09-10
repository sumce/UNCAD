using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Submission;
using UNCAD.Core.Text;
using UNCAD.Features.Submit;

namespace UNCAD.Features.Fill
{
    internal static class FillUpdateRowMerger
    {
        public static List<TableFillRow> Merge(CadContext ctx, FillSelection selection,
            List<TableFillRow> planned, CableStatResult statistics)
        {
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                List<TableFillRow> result = Merge(transaction, selection, planned, statistics);
                transaction.Commit();
                return result;
            }
        }

        internal static List<TableFillRow> Merge(Transaction transaction,
            FillSelection selection, List<TableFillRow> planned, CableStatResult statistics)
        {
            planned = planned ?? new List<TableFillRow>();
            statistics = statistics ?? new CableStatResult();
            if (selection?.TableIds == null || selection.TableIds.Length == 0) return planned;
            if (statistics.CableState != MeasurementState.Unknown
                && statistics.BridgeState != MeasurementState.Unknown
                && statistics.ConduitState != MeasurementState.Unknown) return planned;

            SubmissionSourceData source = CadSubmissionReader.Read(transaction,
                selection.TableIds);
            return MergeRows(planned, ReadRows(source), statistics);
        }

        internal static List<TableFillRow> MergeRows(List<TableFillRow> planned,
            IEnumerable<TableFillRow> existingRows, CableStatResult statistics)
        {
            planned = planned ?? new List<TableFillRow>();
            statistics = statistics ?? new CableStatResult();
            var matchedPlanned = new HashSet<TableFillRow>();
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
                    !matchedPlanned.Contains(row) && row.Category == old.Category
                    && SameMaterial(row, old));
                if (current != null)
                {
                    matchedPlanned.Add(current);
                    // 没测到就沿用旧数量，这是这条分支存在的理由。
                    if (string.IsNullOrWhiteSpace(current.Quantity))
                        current.Quantity = old.Quantity;
                    // 清单身份不继承。能走到这里说明规划行还没有编码，匹配就只可能是
                    // 靠名称（SameMaterial 在两边都有编码时比的是编码，那种情况
                    // CatalogMatched 本来就是 true）。而名称相同——比如都叫“电缆”
                    // ——证明不了两行是同一项材料，借编码等于给一个规格盖上另一个
                    // 规格的编码；写出前的两道闸只看 CatalogMatched，会直接放行，
                    // 于是错编码进 CAD 表格并导出到 BOQ（违反 D-016）。
                    // 旧行本身仍由下面的 planned.Add(old) 原样保留。
                    continue;
                }
                planned.Add(old);
                matchedPlanned.Add(old);
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
                string number = CleanCell(cells, 0);
                string name = CleanCell(cells, 1);
                string description = Cell(cells, 2);
                string unit = CleanCell(cells, 3);
                string quantity = CleanCell(cells, 4);
                string code = CleanCell(cells, 5);
                if (TableLayoutClassifier.IsHeaderLike(number, name, code)) continue;
                // The first column is normally only the display ordinal (1, 2, ...).
                // Treat it as a catalog identity only when it has the fixed BOQ form;
                // otherwise the next U1U pass would falsely mark a fallback row as
                // matched and BOQ export would later fail on code "1"/"2".
                string catalogCode = CatalogCode(code) ? code
                    : CatalogCode(number) ? number : "";
                TableFillCategory? category = ExistingCategory(name, catalogCode);
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

        internal static TableFillCategory? ExistingCategory(string name, string code)
        {
            code = TextParser.CleanMText(code ?? "").Trim();
            name = TextParser.CleanMText(name ?? "").Trim();
            if (code.Length > 0)
            {
                if (code.StartsWith("1.", StringComparison.OrdinalIgnoreCase))
                    return TableFillCategory.Cable;
                if (code.StartsWith("2.", StringComparison.OrdinalIgnoreCase))
                    return TableFillCategory.Bridge;
                if (code.StartsWith("3.8", StringComparison.OrdinalIgnoreCase))
                    return TableFillCategory.FlexibleConduit;
                if (code.StartsWith("3.", StringComparison.OrdinalIgnoreCase))
                    return TableFillCategory.RigidConduit;
                return null;
            }

            if (name.IndexOf("桥架", StringComparison.OrdinalIgnoreCase) >= 0)
                return TableFillCategory.Bridge;
            if (name.IndexOf("电缆", StringComparison.OrdinalIgnoreCase) >= 0)
                return TableFillCategory.Cable;
            if (name.IndexOf("软管", StringComparison.OrdinalIgnoreCase) >= 0)
                return TableFillCategory.FlexibleConduit;
            if (name.IndexOf("线管", StringComparison.OrdinalIgnoreCase) >= 0)
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

        private static string CleanCell(List<string> row, int index)
            => TextParser.CleanMText(Cell(row, index)).Trim();

        private static bool CatalogCode(string value)
            => SubmissionRecordExtractor.IsCatalogCode(value);
    }
}
